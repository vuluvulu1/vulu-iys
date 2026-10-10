using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

// Personel yönetimi. Her personel bir kullanıcı hesabıdır; işler bu hesaplara atanır.
[ApiController]
[Route("api/kullanicilar")]
[Authorize(Roles = nameof(KullaniciRol.Admin))]
public class KullanicilarController : ControllerBase
{
    private readonly MongoBaglami _db;
    private readonly PlatformBaglami _platform;

    public KullanicilarController(MongoBaglami db, PlatformBaglami platform)
    {
        _db = db;
        _platform = platform;
    }

    [HttpGet]
    public async Task<ActionResult<List<KullaniciOzeti>>> Listele(CancellationToken iptal)
    {
        var kullanicilar = await _db.Kullanicilar
            .Find(FilterDefinition<Kullanici>.Empty, new FindOptions { Collation = MongoBaglami.TurkceSiralama })
            .SortBy(k => k.AdSoyad)
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
            Rol = istek.Rol,
            AdSoyad = istek.AdSoyad.Trim(),
            Telefon = Metin.BosIseNull(istek.Telefon),
            Uzmanlik = Metin.BosIseNull(istek.Uzmanlik)
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
        var adDegisti = kullanici.AdSoyad != istek.AdSoyad.Trim();

        kullanici.KullaniciAdi = KullaniciAdlari.Normalle(istek.KullaniciAdi);
        kullanici.Rol = istek.Rol;
        kullanici.AktifMi = istek.AktifMi;
        kullanici.AdSoyad = istek.AdSoyad.Trim();
        kullanici.Telefon = Metin.BosIseNull(istek.Telefon);
        kullanici.Uzmanlik = Metin.BosIseNull(istek.Uzmanlik);
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
            await _platform.YenilemeTokenlari.DeleteManyAsync(t => t.KullaniciId == id, iptal);

        // Servis kayıtları ve randevular atanan kişinin adının kopyasını taşır; ad değiştiyse onlar da güncellenir.
        if (adDegisti)
        {
            await _db.ServisKayitlari.UpdateManyAsync(s => s.TeknisyenId == id,
                Builders<ServisKaydi>.Update.Set(s => s.TeknisyenAdi, kullanici.AdSoyad).Inc(s => s.Surum, 1),
                cancellationToken: iptal);
            await _db.Randevular.UpdateManyAsync(r => r.TeknisyenId == id,
                Builders<Randevu>.Update.Set(r => r.TeknisyenAdi, kullanici.AdSoyad).Inc(r => r.Surum, 1),
                cancellationToken: iptal);
        }

        return kullanici.OzeteCevir();
    }

    [HttpDelete("{id:oid}")]
    public async Task<IActionResult> Sil(string id, CancellationToken iptal)
    {
        if (id == User.FindFirstValue(TokenServisi.KimlikTalebi))
            return Problem(detail: "Kendi hesabınızı silemezsiniz.", statusCode: StatusCodes.Status409Conflict);

        // Üzerinde iş geçmişi olan hesap silinirse eski kayıtlar sahipsiz kalır; böyle hesaplar devre dışı bırakılır.
        var gecmisiVar = await _db.ServisKayitlari.Find(s => s.TeknisyenId == id).AnyAsync(iptal)
            || await _db.Randevular.Find(r => r.TeknisyenId == id).AnyAsync(iptal);
        if (gecmisiVar)
            return Problem(detail: "Bu personele atanmış servis kaydı ya da randevu var; hesabı silmek yerine devre dışı bırakın.",
                statusCode: StatusCodes.Status409Conflict);

        var sonuc = await _db.Kullanicilar.DeleteOneAsync(k => k.Id == id, iptal);
        if (sonuc.DeletedCount == 0) return Bulunamadi();

        await _platform.YenilemeTokenlari.DeleteManyAsync(t => t.KullaniciId == id, iptal);
        return NoContent();
    }

    private Task<bool> BaskaAktifAdminVarMi(string haricId, CancellationToken iptal) =>
        _db.Kullanicilar.Find(k => k.Id != haricId && k.Rol == KullaniciRol.Admin && k.AktifMi).AnyAsync(iptal);

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Personel bulunamadı.", statusCode: StatusCodes.Status404NotFound);

    private ObjectResult KullaniciAdiKullaniliyor() =>
        Problem(detail: "Bu kullanıcı adı zaten kullanılıyor.", statusCode: StatusCodes.Status409Conflict);
}

public static class KullaniciDonusumleri
{
    public static KullaniciOzeti OzeteCevir(this Kullanici k) =>
        new(k.Id, k.KullaniciAdi, k.Rol, k.AktifMi, k.AdSoyad, k.Telefon, k.Uzmanlik);
}
