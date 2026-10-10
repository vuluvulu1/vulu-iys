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

    // Kullanıcı bulunamadığında da bir hash doğrulaması yapılır ki yanıt süresi kullanıcının varlığını ele vermesin.
    private static readonly string SahteHash = BCrypt.Net.BCrypt.HashPassword("sahte-sifre");

    private readonly MongoBaglami _db;
    private readonly TokenServisi _tokenlar;

    public KimlikController(MongoBaglami db, TokenServisi tokenlar)
    {
        _db = db;
        _tokenlar = tokenlar;
    }

    [HttpPost("giris")]
    [AllowAnonymous]
    [EnableRateLimiting(GirisSiniriPolitikasi)]
    public async Task<ActionResult<OturumYaniti>> Giris(GirisIstegi istek, CancellationToken iptal)
    {
        var ad = KullaniciAdlari.Normalle(istek.KullaniciAdi);
        var kullanici = await _db.Kullanicilar.Find(k => k.KullaniciAdi == ad).FirstOrDefaultAsync(iptal);

        var sifreDogru = BCrypt.Net.BCrypt.Verify(istek.Sifre, kullanici?.SifreHash ?? SahteHash);
        if (kullanici is null || !sifreDogru)
            return Problem(detail: "Kullanıcı adı veya şifre hatalı.", statusCode: StatusCodes.Status401Unauthorized);

        if (!kullanici.AktifMi)
            return Problem(detail: "Hesabınız devre dışı.", statusCode: StatusCodes.Status403Forbidden);

        return await OturumAc(kullanici, iptal);
    }

    [HttpPost("yenile")]
    [AllowAnonymous]
    public async Task<ActionResult<OturumYaniti>> Yenile(YenilemeIstegi istek, CancellationToken iptal)
    {
        // Token tek kullanımlıktır: bulunduğu anda silinir, yerine yenisi verilir.
        var hash = TokenServisi.Hashle(istek.YenilemeTokeni);
        var kayit = await _db.YenilemeTokenlari.FindOneAndDeleteAsync(t => t.TokenHash == hash, cancellationToken: iptal);
        if (kayit is null || kayit.SonGecerlilik <= DateTime.UtcNow)
            return OturumGecersiz();

        var kullanici = await _db.Kullanicilar.Find(k => k.Id == kayit.KullaniciId).FirstOrDefaultAsync(iptal);
        if (kullanici is null || !kullanici.AktifMi)
            return OturumGecersiz();

        return await OturumAc(kullanici, iptal);
    }

    [HttpPost("cikis")]
    [AllowAnonymous]
    public async Task<IActionResult> Cikis(YenilemeIstegi istek, CancellationToken iptal)
    {
        var hash = TokenServisi.Hashle(istek.YenilemeTokeni);
        await _db.YenilemeTokenlari.DeleteOneAsync(t => t.TokenHash == hash, iptal);
        return NoContent();
    }

    [HttpGet("ben")]
    public async Task<ActionResult<KullaniciOzeti>> Ben(CancellationToken iptal)
    {
        var id = User.FindFirstValue(TokenServisi.KimlikTalebi);
        var kullanici = await _db.Kullanicilar.Find(k => k.Id == id).FirstOrDefaultAsync(iptal);
        if (kullanici is null || !kullanici.AktifMi)
            return OturumGecersiz();

        return kullanici.OzeteCevir();
    }

    private async Task<OturumYaniti> OturumAc(Kullanici kullanici, CancellationToken iptal)
    {
        var (erisimTokeni, bitis) = _tokenlar.ErisimTokeniUret(kullanici);
        var yenilemeTokeni = TokenServisi.YenilemeTokeniUret();

        await _db.YenilemeTokenlari.InsertOneAsync(new YenilemeTokeni
        {
            KullaniciId = kullanici.Id,
            TokenHash = TokenServisi.Hashle(yenilemeTokeni),
            SonGecerlilik = _tokenlar.YenilemeBitisi()
        }, cancellationToken: iptal);

        return new OturumYaniti(erisimTokeni, bitis, yenilemeTokeni, kullanici.OzeteCevir());
    }

    private ObjectResult OturumGecersiz() =>
        Problem(detail: "Oturum geçersiz, yeniden giriş yapın.", statusCode: StatusCodes.Status401Unauthorized);
}
