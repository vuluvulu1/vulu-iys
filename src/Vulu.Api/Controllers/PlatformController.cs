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
        // Liste için logo içerikleri ve ödeme geçmişi taşınmaz.
        var isletmeler = await _platform.Isletmeler
            .Find(FilterDefinition<Isletme>.Empty, new FindOptions { Collation = MongoBaglami.TurkceSiralama })
            .Project<Isletme>(Builders<Isletme>.Projection.Exclude(i => i.Logo).Exclude(i => i.Odemeler))
            .SortBy(i => i.Ad)
            .ToListAsync(iptal);

        // Paket kullanımını göstermek için her işletmenin aktif personeli sayılır.
        var yanitlar = new List<IsletmeYaniti>(isletmeler.Count);
        foreach (var isletme in isletmeler)
            yanitlar.Add(isletme.YanitaCevir(await AktifPersonelSayisi(isletme, iptal)));
        return yanitlar;
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
            PersonelSiniri = istek.PersonelSiniri,
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
        return CreatedAtAction(nameof(Listele), isletme.YanitaCevir(personelSayisi: 1));
    }

    // Ad düzeltmesi, askıya alma / yeniden açma ve paket. İşletmenin verisi silinmez.
    [HttpPut("{id:oid}")]
    public async Task<ActionResult<IsletmeYaniti>> Guncelle(string id, IsletmeDurumIstegi istek, CancellationToken iptal)
    {
        var guncelleme = Builders<Isletme>.Update
            .Set(i => i.Ad, istek.Ad.Trim())
            .Set(i => i.AktifMi, istek.AktifMi)
            .Set(i => i.PersonelSiniri, istek.PersonelSiniri);
        guncelleme = istek.AbonelikBitisi is { } bitis
            ? guncelleme.Set(i => i.AbonelikBitisi, GunBasi(bitis))
            : guncelleme.Unset(i => i.AbonelikBitisi);

        var isletme = await _platform.Isletmeler.FindOneAndUpdateAsync<Isletme>(i => i.Id == id, guncelleme,
            new FindOneAndUpdateOptions<Isletme> { ReturnDocument = ReturnDocument.After }, iptal);
        if (isletme is null) return Bulunamadi();

        // Askıya alınan işletmenin açık oturumları yenilenemesin; erişim tokenları da bir sonraki istekte reddedilir.
        IsletmeCozucu.OnbellektenCikar(_onbellek, id);
        if (!istek.AktifMi)
            await _platform.YenilemeTokenlari.DeleteManyAsync(t => t.IsletmeId == id, iptal);

        return isletme.YanitaCevir(await AktifPersonelSayisi(isletme, iptal));
    }

    // İşletmeyi ve bütün verisini geri dönüşsüz siler. Yanlışlıkla silmeye karşı iki şart aranır:
    // işletme önce askıya alınmış olmalı ve istek işletmenin kodunu yeniden vermelidir.
    [HttpDelete("{id:oid}")]
    public async Task<IActionResult> Sil(string id, [FromQuery] string? kod, CancellationToken iptal)
    {
        var isletme = await _platform.Isletmeler.Find(i => i.Id == id).FirstOrDefaultAsync(iptal);
        if (isletme is null) return Bulunamadi();

        if (isletme.AktifMi)
            return Problem(detail: "Açık bir işletme silinemez; önce askıya alın.", statusCode: StatusCodes.Status409Conflict);
        if (!string.Equals(kod?.Trim(), isletme.Kod, StringComparison.Ordinal))
            return Problem(detail: "Onay için işletme kodu yanlış yazıldı.", statusCode: StatusCodes.Status400BadRequest);

        await _platform.YenilemeTokenlari.DeleteManyAsync(t => t.IsletmeId == id, iptal);
        await _platform.IsletmeVeritabaniniSilAsync(isletme, iptal);
        await _platform.Isletmeler.DeleteOneAsync(i => i.Id == id, iptal);
        IsletmeCozucu.OnbellektenCikar(_onbellek, id);

        _log.LogWarning("İşletme kalıcı olarak silindi: {Kod} ({Ad}), veritabanı {Veritabani}",
            isletme.Kod, isletme.Ad, isletme.VeritabaniAdi);
        return NoContent();
    }

    // --- İşletme yöneticisinin unutulan şifresi ---

    [HttpGet("{id:oid}/yoneticiler")]
    public async Task<ActionResult<List<IsletmeYoneticisiOzeti>>> Yoneticiler(string id, CancellationToken iptal)
    {
        var isletme = await _platform.Isletmeler.Find(i => i.Id == id).FirstOrDefaultAsync(iptal);
        if (isletme is null) return Bulunamadi();

        var yoneticiler = await _platform.IsletmeBaglami(isletme).Kullanicilar
            .Find(k => k.Rol == KullaniciRol.Admin).SortBy(k => k.KullaniciAdi).ToListAsync(iptal);
        return yoneticiler.Select(k => new IsletmeYoneticisiOzeti(k.Id, k.KullaniciAdi, k.AdSoyad, k.AktifMi)).ToList();
    }

    // Yalnızca işletmenin yönetici hesaplarının şifresi sıfırlanabilir; diğer personelin şifresini işletmenin
    // kendi yöneticisi verir. Hesap devre dışıysa yeniden etkinleştirilir (kilitlenen işletmeye giriş yolu açılır).
    [HttpPut("{id:oid}/yoneticiler/{kullaniciId:oid}/sifre")]
    public async Task<IActionResult> YoneticiSifresiniSifirla(string id, string kullaniciId, SifreSifirlamaIstegi istek,
        CancellationToken iptal)
    {
        var isletme = await _platform.Isletmeler.Find(i => i.Id == id).FirstOrDefaultAsync(iptal);
        if (isletme is null) return Bulunamadi();

        var sonuc = await _platform.IsletmeBaglami(isletme).Kullanicilar.UpdateOneAsync(
            k => k.Id == kullaniciId && k.Rol == KullaniciRol.Admin,
            Builders<Kullanici>.Update
                .Set(k => k.SifreHash, BCrypt.Net.BCrypt.HashPassword(istek.YeniSifre))
                .Set(k => k.AktifMi, true),
            cancellationToken: iptal);
        if (sonuc.MatchedCount == 0)
            return Problem(detail: "İşletmede böyle bir yönetici hesabı yok.", statusCode: StatusCodes.Status404NotFound);

        // Eski şifreyle açılmış oturumlar kapanır.
        await _platform.YenilemeTokenlari.DeleteManyAsync(t => t.KullaniciId == kullaniciId, iptal);
        _log.LogInformation("İşletme yöneticisinin şifresi sıfırlandı: {Kod}", isletme.Kod);
        return NoContent();
    }

    // --- Abonelik ödemeleri ---

    [HttpGet("{id:oid}/odemeler")]
    public async Task<ActionResult<List<OdemeYaniti>>> Odemeler(string id, CancellationToken iptal)
    {
        var isletme = await _platform.Isletmeler.Find(i => i.Id == id).FirstOrDefaultAsync(iptal);
        if (isletme is null) return Bulunamadi();

        return isletme.Odemeler.OrderByDescending(o => o.Tarih)
            .Select(o => new OdemeYaniti(o.Tarih, o.Tutar, o.Aciklama)).ToList();
    }

    [HttpPost("{id:oid}/odemeler")]
    public async Task<ActionResult<IsletmeYaniti>> OdemeEkle(string id, OdemeIstegi istek, CancellationToken iptal)
    {
        if (decimal.Round(istek.Tutar, 2) != istek.Tutar)
            return Problem(detail: "Tutar en fazla iki ondalık hane içermeli.", statusCode: StatusCodes.Status400BadRequest);

        var guncelleme = Builders<Isletme>.Update.Push(i => i.Odemeler, new Odeme
        {
            Tarih = Zaman.UtcYap(istek.Tarih),
            Tutar = istek.Tutar,
            Aciklama = Metin.BosIseNull(istek.Aciklama)
        });
        if (istek.YeniAbonelikBitisi is { } bitis)
            guncelleme = guncelleme.Set(i => i.AbonelikBitisi, GunBasi(bitis));

        var isletme = await _platform.Isletmeler.FindOneAndUpdateAsync<Isletme>(i => i.Id == id, guncelleme,
            new FindOneAndUpdateOptions<Isletme> { ReturnDocument = ReturnDocument.After }, iptal);
        if (isletme is null) return Bulunamadi();

        IsletmeCozucu.OnbellektenCikar(_onbellek, id);
        return isletme.YanitaCevir(await AktifPersonelSayisi(isletme, iptal));
    }

    private Task<long> AktifPersonelSayisi(Isletme isletme, CancellationToken iptal) =>
        _platform.IsletmeBaglami(isletme).Kullanicilar.CountDocumentsAsync(k => k.AktifMi, cancellationToken: iptal);

    // Abonelik bitişi saat değil gündür; saat dilimi kaymasın diye UTC gün başı olarak saklanır.
    private static DateTime GunBasi(DateTime tarih) => DateTime.SpecifyKind(tarih.Date, DateTimeKind.Utc);

    private ObjectResult Bulunamadi() =>
        Problem(detail: "İşletme bulunamadı.", statusCode: StatusCodes.Status404NotFound);
}
