using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/kimlik")]
public class KimlikController : ControllerBase
{
    public const string GirisSiniriPolitikasi = "giris";

    // İşletme, kullanıcı ya da şifreden hangisinin yanlış olduğu söylenmez; var olan kodlar ve hesaplar tahmin edilemesin.
    private const string HataliGiris = "İşletme kodu, kullanıcı adı veya şifre hatalı.";

    private readonly PlatformBaglami _platform;
    private readonly TokenServisi _tokenlar;
    private readonly GirisKilidi _kilit;
    private readonly Oturumlar _oturumlar;
    private readonly ILogger<KimlikController> _log;

    public KimlikController(PlatformBaglami platform, TokenServisi tokenlar, GirisKilidi kilit, Oturumlar oturumlar,
        ILogger<KimlikController> log)
    {
        _platform = platform;
        _tokenlar = tokenlar;
        _kilit = kilit;
        _oturumlar = oturumlar;
        _log = log;
    }

    [HttpPost("giris")]
    [AllowAnonymous]
    [EnableRateLimiting(GirisSiniriPolitikasi)]
    public async Task<ActionResult<OturumYaniti>> Giris(GirisIstegi istek, CancellationToken iptal)
    {
        var kod = istek.IsletmeKodu.Trim().ToLowerInvariant();
        var ad = KullaniciAdlari.Normalle(istek.KullaniciAdi);

        // Kilitli hesapta şifre hiç denenmez; doğru şifre de kabul edilmez.
        if (_kilit.KalanKilit(kod, ad) is { } kalan)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(kalan.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return Problem(detail: $"Çok fazla yanlış deneme yapıldı. {Math.Ceiling(kalan.TotalMinutes):0} dakika sonra yeniden deneyin.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var sonuc = kod == IsletmeKurallari.PlatformKodu
            ? await YoneticiGirisi(ad, istek.Sifre, iptal)
            : await PersonelGirisi(kod, ad, istek.Sifre, iptal);

        if (sonuc.Value is not null)
        {
            _kilit.Sifirla(kod, ad);
        }
        else if (sonuc.Result is ObjectResult { StatusCode: StatusCodes.Status401Unauthorized })
        {
            var kilitlendi = _kilit.YanlisDeneme(kod, ad);
            _log.LogWarning("Başarısız giriş: {Kod}/{Ad}, adres {Adres}{Kilit}",
                Metin.GunlukIcin(kod), Metin.GunlukIcin(ad), HttpContext.Connection.RemoteIpAddress, kilitlendi ? " - hesap geçici olarak kilitlendi" : "");
        }

        return sonuc;
    }

    private async Task<ActionResult<OturumYaniti>> PersonelGirisi(string kod, string ad, string sifre, CancellationToken iptal)
    {
        var isletme = await _platform.Isletmeler.Find(i => i.Kod == kod).FirstOrDefaultAsync(iptal);
        var kullanici = isletme is null
            ? null
            : await _platform.IsletmeBaglami(isletme).Kullanicilar.Find(k => k.KullaniciAdi == ad).FirstOrDefaultAsync(iptal);

        if (!Sifreler.Dogrula(sifre, kullanici?.SifreHash) || isletme is null || kullanici is null)
            return Problem(detail: HataliGiris, statusCode: StatusCodes.Status401Unauthorized);

        // Askıya alma ve devre dışı bilgisi yalnızca şifreyi bilen kişiye söylenir.
        if (!isletme.AktifMi)
            return Problem(detail: "İşletme hesabı askıya alınmış. Lütfen vulu ile iletişime geçin.", statusCode: StatusCodes.Status403Forbidden);
        if (!kullanici.AktifMi)
            return Problem(detail: "Hesabınız devre dışı.", statusCode: StatusCodes.Status403Forbidden);

        return await OturumAc(kullanici, isletme, iptal);
    }

    private async Task<ActionResult<OturumYaniti>> YoneticiGirisi(string ad, string sifre, CancellationToken iptal)
    {
        var yonetici = await _platform.Yoneticiler.Find(y => y.KullaniciAdi == ad).FirstOrDefaultAsync(iptal);

        if (!Sifreler.Dogrula(sifre, yonetici?.SifreHash) || yonetici is null)
            return Problem(detail: HataliGiris, statusCode: StatusCodes.Status401Unauthorized);
        if (!yonetici.AktifMi)
            return Problem(detail: "Hesabınız devre dışı.", statusCode: StatusCodes.Status403Forbidden);

        return await OturumAc(yonetici, iptal);
    }

    [HttpPost("yenile")]
    [AllowAnonymous]
    public async Task<ActionResult<OturumYaniti>> Yenile(YenilemeIstegi istek, CancellationToken iptal)
    {
        // Token tek kullanımlıktır: bulunduğu anda kullanıldı diye işaretlenir, yerine yenisi verilir.
        var hash = TokenServisi.Hashle(istek.YenilemeTokeni);
        var kayit = await _platform.YenilemeTokenlari.FindOneAndUpdateAsync(
            t => t.TokenHash == hash && t.KullanildiTarihi == null,
            Builders<YenilemeTokeni>.Update.Set(t => t.KullanildiTarihi, DateTime.UtcNow),
            cancellationToken: iptal);

        if (kayit is null)
        {
            await CalinmisTokeniIsle(hash, iptal);
            return OturumGecersiz();
        }

        if (kayit.SonGecerlilik <= DateTime.UtcNow)
            return OturumGecersiz();

        if (kayit.IsletmeId is null)
        {
            var yonetici = await _platform.Yoneticiler.Find(y => y.Id == kayit.KullaniciId).FirstOrDefaultAsync(iptal);
            return yonetici is { AktifMi: true } ? await OturumAc(yonetici, iptal) : OturumGecersiz();
        }

        var isletme = await _platform.Isletmeler.Find(i => i.Id == kayit.IsletmeId).FirstOrDefaultAsync(iptal);
        if (isletme is not { AktifMi: true })
            return OturumGecersiz();

        var kullanici = await _platform.IsletmeBaglami(isletme).Kullanicilar
            .Find(k => k.Id == kayit.KullaniciId).FirstOrDefaultAsync(iptal);
        return kullanici is { AktifMi: true } ? await OturumAc(kullanici, isletme, iptal) : OturumGecersiz();
    }

    // Daha önce kullanılmış bir token yeniden sunulduysa token çalınmıştır: biri meşru sahibi, diğeri hırsızdır
    // ve hangisinin hangisi olduğu bilinemez. Hesabın bütün oturumları kapatılır; gerçek sahibi yeniden giriş yapar.
    private async Task CalinmisTokeniIsle(string hash, CancellationToken iptal)
    {
        var kullanilmis = await _platform.YenilemeTokenlari.Find(t => t.TokenHash == hash).FirstOrDefaultAsync(iptal);
        if (kullanilmis is null) return;

        _log.LogWarning("Kullanılmış yenileme tokenı yeniden sunuldu; hesabın oturumları kapatıldı: {Kullanici} (işletme {Isletme})",
            kullanilmis.KullaniciId, kullanilmis.IsletmeId ?? "vulu");

        var damga = Sifreler.YeniOturumDamgasi();
        if (kullanilmis.IsletmeId is null)
        {
            await _platform.Yoneticiler.UpdateOneAsync(y => y.Id == kullanilmis.KullaniciId,
                Builders<PlatformYoneticisi>.Update.Set(y => y.OturumDamgasi, damga), cancellationToken: iptal);
        }
        else if (await _platform.Isletmeler.Find(i => i.Id == kullanilmis.IsletmeId).FirstOrDefaultAsync(iptal) is { } isletme)
        {
            await _platform.IsletmeBaglami(isletme).Kullanicilar.UpdateOneAsync(k => k.Id == kullanilmis.KullaniciId,
                Builders<Kullanici>.Update.Set(k => k.OturumDamgasi, damga), cancellationToken: iptal);
        }

        await _oturumlar.KapatAsync(kullanilmis.IsletmeId, kullanilmis.KullaniciId, iptal);
    }

    [HttpPost("cikis")]
    [AllowAnonymous]
    public async Task<IActionResult> Cikis(YenilemeIstegi istek, CancellationToken iptal)
    {
        var hash = TokenServisi.Hashle(istek.YenilemeTokeni);
        await _platform.YenilemeTokenlari.DeleteOneAsync(t => t.TokenHash == hash, iptal);
        return NoContent();
    }

    // Oturumdaki personelin güncel bilgisi. İşletmeye bağlı olmayan oturumlar (vulu yöneticisi) bu uca erişemez.
    [HttpGet("ben")]
    public async Task<ActionResult<KullaniciOzeti>> Ben([FromServices] MongoBaglami db, CancellationToken iptal)
    {
        var id = User.FindFirstValue(TokenServisi.KimlikTalebi);
        var kullanici = await db.Kullanicilar.Find(k => k.Id == id).FirstOrDefaultAsync(iptal);
        if (kullanici is null || !kullanici.AktifMi)
            return OturumGecersiz();

        return kullanici.OzeteCevir();
    }

    // Oturumdaki kişi kendi şifresini değiştirir (işletme personeli ya da vulu yöneticisi). Diğer cihazlardaki
    // oturumları kapanır; bu istemciye yeni bir oturum döner. Mevcut şifre denemeleri giriş gibi sınırlanır.
    [HttpPost("sifre")]
    [Authorize]
    [EnableRateLimiting(GirisSiniriPolitikasi)]
    public async Task<ActionResult<OturumYaniti>> SifreDegistir(SifreDegistirmeIstegi istek,
        [FromServices] GecerliIsletme gecerli, CancellationToken iptal)
    {
        var id = User.FindFirstValue(TokenServisi.KimlikTalebi)!;
        var isletme = gecerli.Isletme;
        var kullanicilar = isletme is null ? null : _platform.IsletmeBaglami(isletme).Kullanicilar;

        var kullanici = kullanicilar is null ? null : await kullanicilar.Find(k => k.Id == id).FirstOrDefaultAsync(iptal);
        var yonetici = kullanicilar is null ? await _platform.Yoneticiler.Find(y => y.Id == id).FirstOrDefaultAsync(iptal) : null;

        if (!Sifreler.Dogrula(istek.MevcutSifre, kullanici?.SifreHash ?? yonetici?.SifreHash))
            return Problem(detail: "Mevcut şifre yanlış.", statusCode: StatusCodes.Status400BadRequest);
        if (istek.YeniSifre == istek.MevcutSifre)
            return Problem(detail: "Yeni şifre mevcut şifreyle aynı olamaz.", statusCode: StatusCodes.Status400BadRequest);

        var hash = Sifreler.Hashle(istek.YeniSifre);
        var damga = Sifreler.YeniOturumDamgasi();

        if (kullanici is not null)
        {
            kullanici.OturumDamgasi = damga;
            await kullanicilar!.UpdateOneAsync(k => k.Id == id,
                Builders<Kullanici>.Update.Set(k => k.SifreHash, hash).Set(k => k.OturumDamgasi, damga), cancellationToken: iptal);
        }
        else
        {
            yonetici!.OturumDamgasi = damga;
            await _platform.Yoneticiler.UpdateOneAsync(y => y.Id == id,
                Builders<PlatformYoneticisi>.Update.Set(y => y.SifreHash, hash).Set(y => y.OturumDamgasi, damga), cancellationToken: iptal);
        }

        await _oturumlar.KapatAsync(isletme?.Id, id, iptal);
        _log.LogInformation("Şifre değiştirildi: {Kullanici} (işletme {Isletme})", id, isletme?.Kod ?? "vulu");

        return kullanici is not null ? await OturumAc(kullanici, isletme!, iptal) : await OturumAc(yonetici!, iptal);
    }

    private async Task<OturumYaniti> OturumAc(Kullanici kullanici, Isletme isletme, CancellationToken iptal)
    {
        var (erisimTokeni, bitis) = _tokenlar.ErisimTokeniUret(
            kullanici.Id, kullanici.KullaniciAdi, kullanici.Rol.ToString(), kullanici.OturumDamgasi, isletme.Id);
        var yenilemeTokeni = await YenilemeTokeniKaydet(kullanici.Id, isletme.Id, iptal);

        return new OturumYaniti(erisimTokeni, bitis, yenilemeTokeni, kullanici.OzeteCevir(),
            new IsletmeOzeti(isletme.Id, isletme.Kod, isletme.Ad));
    }

    private async Task<OturumYaniti> OturumAc(PlatformYoneticisi yonetici, CancellationToken iptal)
    {
        var (erisimTokeni, bitis) = _tokenlar.ErisimTokeniUret(
            yonetici.Id, yonetici.KullaniciAdi, TokenServisi.PlatformRolu, yonetici.OturumDamgasi, isletmeId: null);
        var yenilemeTokeni = await YenilemeTokeniKaydet(yonetici.Id, isletmeId: null, iptal);

        // Vulu yöneticisi bir işletme personeli değildir; istemci bunu Isletme alanının boş olmasından anlar.
        var ozet = new KullaniciOzeti(yonetici.Id, yonetici.KullaniciAdi, KullaniciRol.Admin, yonetici.AktifMi,
            yonetici.AdSoyad, null, null);
        return new OturumYaniti(erisimTokeni, bitis, yenilemeTokeni, ozet, Isletme: null);
    }

    private async Task<string> YenilemeTokeniKaydet(string kullaniciId, string? isletmeId, CancellationToken iptal)
    {
        var token = TokenServisi.YenilemeTokeniUret();
        await _platform.YenilemeTokenlari.InsertOneAsync(new YenilemeTokeni
        {
            KullaniciId = kullaniciId,
            IsletmeId = isletmeId,
            TokenHash = TokenServisi.Hashle(token),
            SonGecerlilik = _tokenlar.YenilemeBitisi()
        }, cancellationToken: iptal);
        return token;
    }

    private ObjectResult OturumGecersiz() =>
        Problem(detail: "Oturum geçersiz, yeniden giriş yapın.", statusCode: StatusCodes.Status401Unauthorized);
}
