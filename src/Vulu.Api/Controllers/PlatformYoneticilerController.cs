using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

// Vulu yöneticisi hesaplarının yönetimi. Her vulu yöneticisi diğerlerini ekleyip çıkarabilir.
[ApiController]
[Route("api/platform/yoneticiler")]
[Authorize(Roles = TokenServisi.PlatformRolu)]
public class PlatformYoneticilerController : ControllerBase
{
    private readonly PlatformBaglami _platform;

    public PlatformYoneticilerController(PlatformBaglami platform) => _platform = platform;

    [HttpGet]
    public async Task<ActionResult<List<VuluYoneticisiYaniti>>> Listele(CancellationToken iptal)
    {
        var yoneticiler = await _platform.Yoneticiler.Find(FilterDefinition<PlatformYoneticisi>.Empty)
            .SortBy(y => y.KullaniciAdi).ToListAsync(iptal);
        return yoneticiler.Select(YanitaCevir).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<VuluYoneticisiYaniti>> Olustur(VuluYoneticisiOlusturIstegi istek, CancellationToken iptal)
    {
        var yonetici = new PlatformYoneticisi
        {
            KullaniciAdi = KullaniciAdlari.Normalle(istek.KullaniciAdi),
            SifreHash = BCrypt.Net.BCrypt.HashPassword(istek.Sifre),
            AdSoyad = istek.AdSoyad.Trim()
        };

        try
        {
            await _platform.Yoneticiler.InsertOneAsync(yonetici, cancellationToken: iptal);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Problem(detail: "Bu kullanıcı adı zaten kullanılıyor.", statusCode: StatusCodes.Status409Conflict);
        }

        return CreatedAtAction(nameof(Listele), YanitaCevir(yonetici));
    }

    [HttpPut("{id:oid}")]
    public async Task<ActionResult<VuluYoneticisiYaniti>> Guncelle(string id, VuluYoneticisiGuncelleIstegi istek, CancellationToken iptal)
    {
        var yonetici = await _platform.Yoneticiler.Find(y => y.Id == id).FirstOrDefaultAsync(iptal);
        if (yonetici is null) return Bulunamadi();

        if (yonetici.AktifMi && !istek.AktifMi && !await BaskaAktifYoneticiVarMi(id, iptal))
            return SonYonetici();

        var oturumlarKapansin = !istek.AktifMi || !string.IsNullOrEmpty(istek.YeniSifre);

        var guncelleme = Builders<PlatformYoneticisi>.Update
            .Set(y => y.AdSoyad, istek.AdSoyad.Trim())
            .Set(y => y.AktifMi, istek.AktifMi);
        if (!string.IsNullOrEmpty(istek.YeniSifre))
            guncelleme = guncelleme.Set(y => y.SifreHash, BCrypt.Net.BCrypt.HashPassword(istek.YeniSifre));

        var guncel = await _platform.Yoneticiler.FindOneAndUpdateAsync<PlatformYoneticisi>(y => y.Id == id, guncelleme,
            new FindOneAndUpdateOptions<PlatformYoneticisi> { ReturnDocument = ReturnDocument.After }, iptal);

        if (oturumlarKapansin)
            await _platform.YenilemeTokenlari.DeleteManyAsync(t => t.KullaniciId == id, iptal);

        return YanitaCevir(guncel);
    }

    [HttpDelete("{id:oid}")]
    public async Task<IActionResult> Sil(string id, CancellationToken iptal)
    {
        if (id == User.FindFirstValue(TokenServisi.KimlikTalebi))
            return Problem(detail: "Kendi hesabınızı silemezsiniz.", statusCode: StatusCodes.Status409Conflict);
        if (!await BaskaAktifYoneticiVarMi(id, iptal))
            return SonYonetici();

        var sonuc = await _platform.Yoneticiler.DeleteOneAsync(y => y.Id == id, iptal);
        if (sonuc.DeletedCount == 0) return Bulunamadi();

        await _platform.YenilemeTokenlari.DeleteManyAsync(t => t.KullaniciId == id, iptal);
        return NoContent();
    }

    private Task<bool> BaskaAktifYoneticiVarMi(string haricId, CancellationToken iptal) =>
        _platform.Yoneticiler.Find(y => y.Id != haricId && y.AktifMi).AnyAsync(iptal);

    private static VuluYoneticisiYaniti YanitaCevir(PlatformYoneticisi y) => new(y.Id, y.KullaniciAdi, y.AdSoyad, y.AktifMi);

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Vulu yöneticisi bulunamadı.", statusCode: StatusCodes.Status404NotFound);

    private ObjectResult SonYonetici() =>
        Problem(detail: "En az bir aktif vulu yöneticisi kalmalı.", statusCode: StatusCodes.Status409Conflict);
}
