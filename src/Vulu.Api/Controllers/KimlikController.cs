using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Driver;
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

    // Kullanıcı bulunamadığında da bir hash doğrulaması yapılır ki yanıt süresi kullanıcının varlığını ele vermesin.
    private static readonly string SahteHash = BCrypt.Net.BCrypt.HashPassword("sahte-sifre");

    private readonly PlatformBaglami _platform;
    private readonly TokenServisi _tokenlar;

    public KimlikController(PlatformBaglami platform, TokenServisi tokenlar)
    {
        _platform = platform;
        _tokenlar = tokenlar;
    }

    [HttpPost("giris")]
    [AllowAnonymous]
    [EnableRateLimiting(GirisSiniriPolitikasi)]
    public async Task<ActionResult<OturumYaniti>> Giris(GirisIstegi istek, CancellationToken iptal)
    {
        var kod = istek.IsletmeKodu.Trim().ToLowerInvariant();
        var ad = KullaniciAdlari.Normalle(istek.KullaniciAdi);

        return kod == IsletmeKurallari.PlatformKodu
            ? await YoneticiGirisi(ad, istek.Sifre, iptal)
            : await PersonelGirisi(kod, ad, istek.Sifre, iptal);
    }

    private async Task<ActionResult<OturumYaniti>> PersonelGirisi(string kod, string ad, string sifre, CancellationToken iptal)
    {
        var isletme = await _platform.Isletmeler.Find(i => i.Kod == kod).FirstOrDefaultAsync(iptal);
        var kullanici = isletme is null
            ? null
            : await _platform.IsletmeBaglami(isletme).Kullanicilar.Find(k => k.KullaniciAdi == ad).FirstOrDefaultAsync(iptal);

        var sifreDogru = BCrypt.Net.BCrypt.Verify(sifre, kullanici?.SifreHash ?? SahteHash);
        if (isletme is null || kullanici is null || !sifreDogru)
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

        var sifreDogru = BCrypt.Net.BCrypt.Verify(sifre, yonetici?.SifreHash ?? SahteHash);
        if (yonetici is null || !sifreDogru)
            return Problem(detail: HataliGiris, statusCode: StatusCodes.Status401Unauthorized);
        if (!yonetici.AktifMi)
            return Problem(detail: "Hesabınız devre dışı.", statusCode: StatusCodes.Status403Forbidden);

        return await OturumAc(yonetici, iptal);
    }

    [HttpPost("yenile")]
    [AllowAnonymous]
    public async Task<ActionResult<OturumYaniti>> Yenile(YenilemeIstegi istek, CancellationToken iptal)
    {
        // Token tek kullanımlıktır: bulunduğu anda silinir, yerine yenisi verilir.
        var hash = TokenServisi.Hashle(istek.YenilemeTokeni);
        var kayit = await _platform.YenilemeTokenlari.FindOneAndDeleteAsync(t => t.TokenHash == hash, cancellationToken: iptal);
        if (kayit is null || kayit.SonGecerlilik <= DateTime.UtcNow)
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

    private async Task<OturumYaniti> OturumAc(Kullanici kullanici, Isletme isletme, CancellationToken iptal)
    {
        var (erisimTokeni, bitis) = _tokenlar.ErisimTokeniUret(
            kullanici.Id, kullanici.KullaniciAdi, kullanici.Rol.ToString(), isletme.Id);
        var yenilemeTokeni = await YenilemeTokeniKaydet(kullanici.Id, isletme.Id, iptal);

        return new OturumYaniti(erisimTokeni, bitis, yenilemeTokeni, kullanici.OzeteCevir(),
            new IsletmeOzeti(isletme.Id, isletme.Kod, isletme.Ad));
    }

    private async Task<OturumYaniti> OturumAc(PlatformYoneticisi yonetici, CancellationToken iptal)
    {
        var (erisimTokeni, bitis) = _tokenlar.ErisimTokeniUret(
            yonetici.Id, yonetici.KullaniciAdi, TokenServisi.PlatformRolu, isletmeId: null);
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
