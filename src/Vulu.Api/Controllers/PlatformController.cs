using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
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
    private readonly Oturumlar _oturumlar;
    private readonly ILogger<PlatformController> _log;

    public PlatformController(PlatformBaglami platform, Oturumlar oturumlar, ILogger<PlatformController> log)
    {
        _platform = platform;
        _oturumlar = oturumlar;
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

        // Listede her işletmenin aktif personel sayısı da gösterilir.
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
            Adres = istek.Adres.Trim(),
            Telefon = istek.Telefon.Trim(),
            Eposta = istek.Eposta.Trim(),
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
                SifreHash = Sifreler.Hashle(istek.YoneticiSifre),
                OturumDamgasi = Sifreler.YeniOturumDamgasi(),
                Rol = KullaniciRol.Admin,
                AdSoyad = istek.YoneticiAdSoyad.Trim()
            }, cancellationToken: iptal);
        }
        catch
        {
            // Yöneticisi olmayan bir işletme kimsenin giremeyeceği bir kayıt olurdu; kayıt ve yarım kalan
            // veritabanı geri alınır.
            await _platform.Isletmeler.DeleteOneAsync(i => i.Id == isletme.Id, CancellationToken.None);
            await _platform.IsletmeVeritabaniniSilAsync(isletme, CancellationToken.None);
            throw;
        }

        _log.LogInformation("İşletme açıldı: {Kod} ({Ad})", isletme.Kod, isletme.Ad);
        return CreatedAtAction(nameof(Listele), isletme.YanitaCevir(personelSayisi: 1));
    }

    // Ad ve iletişim bilgisi düzeltmesi, askıya alma / yeniden açma. İşletmenin verisi silinmez.
    [HttpPut("{id:oid}")]
    public async Task<ActionResult<IsletmeYaniti>> Guncelle(string id, IsletmeDurumIstegi istek, CancellationToken iptal)
    {
        var guncelleme = Builders<Isletme>.Update
            .Set(i => i.Ad, istek.Ad.Trim())
            .Set(i => i.AktifMi, istek.AktifMi)
            .Set(i => i.Adres, istek.Adres.Trim())
            .Set(i => i.Telefon, istek.Telefon.Trim())
            .Set(i => i.Eposta, istek.Eposta.Trim());

        var isletme = await _platform.Isletmeler.FindOneAndUpdateAsync<Isletme>(i => i.Id == id, guncelleme,
            new FindOneAndUpdateOptions<Isletme> { ReturnDocument = ReturnDocument.After }, iptal);
        if (isletme is null) return Bulunamadi();

        // Askıya alınan işletmenin açık oturumları yenilenemesin; erişim tokenları da bir sonraki istekte reddedilir.
        _oturumlar.IsletmeDegisti(id);
        if (!istek.AktifMi)
        {
            await _oturumlar.IsletmeninkileriKapatAsync(id, iptal);
            _log.LogWarning("İşletme askıya alındı: {Kod}", isletme.Kod);
        }

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

        await _oturumlar.IsletmeninkileriKapatAsync(id, iptal);
        await _platform.IsletmeVeritabaniniSilAsync(isletme, iptal);
        await _platform.Isletmeler.DeleteOneAsync(i => i.Id == id, iptal);
        _oturumlar.IsletmeDegisti(id);

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
                .Set(k => k.SifreHash, Sifreler.Hashle(istek.YeniSifre))
                .Set(k => k.OturumDamgasi, Sifreler.YeniOturumDamgasi())
                .Set(k => k.AktifMi, true),
            cancellationToken: iptal);
        if (sonuc.MatchedCount == 0)
            return Problem(detail: "İşletmede böyle bir yönetici hesabı yok.", statusCode: StatusCodes.Status404NotFound);

        // Eski şifreyle açılmış oturumlar kapanır.
        await _oturumlar.KapatAsync(id, kullaniciId, iptal);
        _log.LogWarning("İşletme yöneticisinin şifresi vulu yöneticisi tarafından sıfırlandı: {Kod}, hesap {Kullanici}, sıfırlayan {Yonetici}",
            isletme.Kod, kullaniciId, User.Identity?.Name);
        return NoContent();
    }

    private Task<long> AktifPersonelSayisi(Isletme isletme, CancellationToken iptal) =>
        _platform.IsletmeBaglami(isletme).Kullanicilar.CountDocumentsAsync(k => k.AktifMi, cancellationToken: iptal);

    private ObjectResult Bulunamadi() =>
        Problem(detail: "İşletme bulunamadı.", statusCode: StatusCodes.Status404NotFound);
}
