using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/kullanicilar")]
[Authorize(Roles = nameof(KullaniciRol.Admin))]
public class KullanicilarController : ControllerBase
{
    private readonly MongoBaglami _db;

    public KullanicilarController(MongoBaglami db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<KullaniciOzeti>>> Listele(CancellationToken iptal)
    {
        var kullanicilar = await _db.Kullanicilar.Find(FilterDefinition<Kullanici>.Empty)
            .SortBy(k => k.KullaniciAdi)
            .ToListAsync(iptal);
        return kullanicilar.Select(k => k.OzeteCevir()).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<KullaniciOzeti>> Olustur(KullaniciOlusturIstegi istek, CancellationToken iptal)
    {
        var kullanici = new Kullanici
        {
            KullaniciAdi = KullaniciAdlari.Normalle(istek.KullaniciAdi),
            SifreHash = BCrypt.Net.BCrypt.HashPassword(istek.Sifre),
            Rol = istek.Rol
        };

        try
        {
            await _db.Kullanicilar.InsertOneAsync(kullanici, cancellationToken: iptal);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return KullaniciAdiKullaniliyor();
        }

        return CreatedAtAction(nameof(Listele), kullanici.OzeteCevir());
    }

    [HttpPut("{id:oid}")]
    public async Task<ActionResult<KullaniciOzeti>> Guncelle(string id, KullaniciGuncelleIstegi istek, CancellationToken iptal)
    {
        var kullanici = await _db.Kullanicilar.Find(k => k.Id == id).FirstOrDefaultAsync(iptal);
        if (kullanici is null) return Bulunamadi();

        var adminligiBirakiyor = kullanici.Rol == KullaniciRol.Admin && kullanici.AktifMi
            && (istek.Rol != KullaniciRol.Admin || !istek.AktifMi);
        if (adminligiBirakiyor && !await BaskaAktifAdminVarMi(id, iptal))
            return Problem(detail: "Sistemde en az bir aktif yönetici kalmalı.", statusCode: StatusCodes.Status409Conflict);

        var oturumlarKapansin = !istek.AktifMi || istek.Rol != kullanici.Rol || !string.IsNullOrEmpty(istek.YeniSifre);

        kullanici.KullaniciAdi = KullaniciAdlari.Normalle(istek.KullaniciAdi);
        kullanici.Rol = istek.Rol;
        kullanici.AktifMi = istek.AktifMi;
        if (!string.IsNullOrEmpty(istek.YeniSifre))
            kullanici.SifreHash = BCrypt.Net.BCrypt.HashPassword(istek.YeniSifre);

        try
        {
            await _db.Kullanicilar.ReplaceOneAsync(k => k.Id == id, kullanici, cancellationToken: iptal);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return KullaniciAdiKullaniliyor();
        }

        // Yetkisi değişen hesap eski yenileme tokenıyla oturumunu sürdüremesin.
        if (oturumlarKapansin)
            await _db.YenilemeTokenlari.DeleteManyAsync(t => t.KullaniciId == id, iptal);

        return kullanici.OzeteCevir();
    }

    [HttpDelete("{id:oid}")]
    public async Task<IActionResult> Sil(string id, CancellationToken iptal)
    {
        if (id == User.FindFirstValue(TokenServisi.KimlikTalebi))
            return Problem(detail: "Kendi hesabınızı silemezsiniz.", statusCode: StatusCodes.Status409Conflict);

        var teknisyeneBagli = await _db.Teknisyenler
            .Find(t => t.KullaniciId == id && t.SilinmeTarihi == null).AnyAsync(iptal);
        if (teknisyeneBagli)
            return Problem(detail: "Bu hesap bir teknisyene bağlı, önce bağlantıyı kaldırın.", statusCode: StatusCodes.Status409Conflict);

        var sonuc = await _db.Kullanicilar.DeleteOneAsync(k => k.Id == id, iptal);
        if (sonuc.DeletedCount == 0) return Bulunamadi();

        await _db.YenilemeTokenlari.DeleteManyAsync(t => t.KullaniciId == id, iptal);
        return NoContent();
    }

    private Task<bool> BaskaAktifAdminVarMi(string haricId, CancellationToken iptal) =>
        _db.Kullanicilar.Find(k => k.Id != haricId && k.Rol == KullaniciRol.Admin && k.AktifMi).AnyAsync(iptal);

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Kullanıcı bulunamadı.", statusCode: StatusCodes.Status404NotFound);

    private ObjectResult KullaniciAdiKullaniliyor() =>
        Problem(detail: "Bu kullanıcı adı zaten kullanılıyor.", statusCode: StatusCodes.Status409Conflict);
}

public static class KullaniciDonusumleri
{
    public static KullaniciOzeti OzeteCevir(this Kullanici k) => new(k.Id, k.KullaniciAdi, k.Rol, k.AktifMi);
}
