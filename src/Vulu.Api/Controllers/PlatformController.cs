using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

// Vulu yöneticisinin işletme kayıtlarını yönettiği uçlar. İşletme personeli (yöneticisi dahil) erişemez.
[ApiController]
[Route("api/platform/isletmeler")]
[Authorize(Roles = TokenServisi.PlatformRolu)]
public class PlatformController : ControllerBase
{
    private readonly PlatformBaglami _platform;
    private readonly IMemoryCache _onbellek;
    private readonly ILogger<PlatformController> _log;

    public PlatformController(PlatformBaglami platform, IMemoryCache onbellek, ILogger<PlatformController> log)
    {
        _platform = platform;
        _onbellek = onbellek;
        _log = log;
    }

    [HttpGet]
    public async Task<ActionResult<List<IsletmeYaniti>>> Listele(CancellationToken iptal)
    {
        // Liste için logo içerikleri taşınmaz.
        var isletmeler = await _platform.Isletmeler
            .Find(FilterDefinition<Isletme>.Empty, new FindOptions { Collation = MongoBaglami.TurkceSiralama })
            .Project<Isletme>(Builders<Isletme>.Projection.Exclude(i => i.Logo))
            .SortBy(i => i.Ad)
            .ToListAsync(iptal);
        return isletmeler.Select(i => i.YanitaCevir()).ToList();
    }

    // İşletme kaydını, işletmenin veritabanını ve ilk yöneticisinin hesabını oluşturur.
    [HttpPost]
    public async Task<ActionResult<IsletmeYaniti>> Olustur(IsletmeOlusturIstegi istek, CancellationToken iptal)
    {
        var kod = istek.Kod.Trim().ToLowerInvariant();
        if (kod == IsletmeKurallari.PlatformKodu)
            return Problem(detail: "Bu işletme kodu kullanılamaz.", statusCode: StatusCodes.Status409Conflict);

        var isletme = new Isletme
        {
            Kod = kod,
            Ad = istek.Ad.Trim(),
            VeritabaniAdi = _platform.YeniVeritabaniAdi(kod)
        };

        try
        {
            await _platform.Isletmeler.InsertOneAsync(isletme, cancellationToken: iptal);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Problem(detail: "Bu işletme kodu zaten kullanılıyor.", statusCode: StatusCodes.Status409Conflict);
        }

        try
        {
            await _platform.IsletmeyiHazirlaAsync(isletme, _log, iptal);
            await _platform.IsletmeBaglami(isletme).Kullanicilar.InsertOneAsync(new Kullanici
            {
                KullaniciAdi = KullaniciAdlari.Normalle(istek.YoneticiKullaniciAdi),
                SifreHash = BCrypt.Net.BCrypt.HashPassword(istek.YoneticiSifre),
                Rol = KullaniciRol.Admin,
                AdSoyad = istek.YoneticiAdSoyad.Trim()
            }, cancellationToken: iptal);
        }
        catch
        {
            // Yöneticisi olmayan bir işletme kimsenin giremeyeceği bir kayıt olurdu; kayıt geri alınır.
            await _platform.Isletmeler.DeleteOneAsync(i => i.Id == isletme.Id, CancellationToken.None);
            throw;
        }

        _log.LogInformation("İşletme açıldı: {Kod} ({Ad})", isletme.Kod, isletme.Ad);
        return CreatedAtAction(nameof(Listele), isletme.YanitaCevir());
    }

    // Ad düzeltmesi ve askıya alma / yeniden açma. İşletmenin verisi silinmez.
    [HttpPut("{id:oid}")]
    public async Task<ActionResult<IsletmeYaniti>> Guncelle(string id, IsletmeDurumIstegi istek, CancellationToken iptal)
    {
        var isletme = await _platform.Isletmeler.FindOneAndUpdateAsync<Isletme>(i => i.Id == id,
            Builders<Isletme>.Update.Set(i => i.Ad, istek.Ad.Trim()).Set(i => i.AktifMi, istek.AktifMi),
            new FindOneAndUpdateOptions<Isletme> { ReturnDocument = ReturnDocument.After }, iptal);
        if (isletme is null)
            return Problem(detail: "İşletme bulunamadı.", statusCode: StatusCodes.Status404NotFound);

        // Askıya alınan işletmenin açık oturumları yenilenemesin; erişim tokenları da bir sonraki istekte reddedilir.
        IsletmeCozucu.OnbellektenCikar(_onbellek, id);
        if (!istek.AktifMi)
            await _platform.YenilemeTokenlari.DeleteManyAsync(t => t.IsletmeId == id, iptal);

        return isletme.YanitaCevir();
    }
}
