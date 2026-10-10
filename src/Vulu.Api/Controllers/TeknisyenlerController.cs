using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/teknisyenler")]
public class TeknisyenlerController : ControllerBase
{
    private readonly MongoBaglami _db;

    public TeknisyenlerController(MongoBaglami db) => _db = db;

    // Teknisyen sayısı az olduğu ve seçim listelerinde tamamı gerektiği için sayfalanmaz.
    [HttpGet]
    public async Task<ActionResult<List<TeknisyenYaniti>>> Listele([FromQuery] string? arama, CancellationToken iptal)
    {
        var filtre = Builders<Teknisyen>.Filter.Eq(t => t.SilinmeTarihi, null);
        if (!string.IsNullOrWhiteSpace(arama))
            filtre &= Builders<Teknisyen>.Filter.Regex(t => t.AramaMetni, AramaMetni.Desen(arama));

        var teknisyenler = await _db.Teknisyenler
            .Find(filtre, new FindOptions { Collation = MongoBaglami.TurkceSiralama })
            .SortBy(t => t.AdSoyad)
            .ToListAsync(iptal);

        return await YanitlaraCevir(teknisyenler, iptal);
    }

    [HttpGet("{id:oid}")]
    public async Task<ActionResult<TeknisyenYaniti>> Getir(string id, CancellationToken iptal)
    {
        var teknisyen = await _db.Teknisyenler.Find(t => t.Id == id && t.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
        if (teknisyen is null) return Bulunamadi();

        return (await YanitlaraCevir([teknisyen], iptal))[0];
    }

    [HttpPost]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<ActionResult<TeknisyenYaniti>> Ekle(TeknisyenIstegi istek, CancellationToken iptal)
    {
        if (await HesapYok(istek.KullaniciId, iptal)) return HesapBulunamadi();

        var teknisyen = new Teknisyen();
        Uygula(teknisyen, istek);

        try
        {
            await _db.Teknisyenler.InsertOneAsync(teknisyen, cancellationToken: iptal);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return HesapBaskasinaBagli();
        }

        var yanit = (await YanitlaraCevir([teknisyen], iptal))[0];
        return CreatedAtAction(nameof(Getir), new { id = teknisyen.Id }, yanit);
    }

    [HttpPut("{id:oid}")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<ActionResult<TeknisyenYaniti>> Guncelle(string id, TeknisyenIstegi istek, CancellationToken iptal)
    {
        var teknisyen = await _db.Teknisyenler.Find(t => t.Id == id && t.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
        if (teknisyen is null) return Bulunamadi();
        if (await HesapYok(istek.KullaniciId, iptal)) return HesapBulunamadi();

        Uygula(teknisyen, istek);

        try
        {
            await _db.Teknisyenler.ReplaceOneAsync(t => t.Id == id, teknisyen, cancellationToken: iptal);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return HesapBaskasinaBagli();
        }

        // Servis kayıtları ve randevular teknisyenin adının kopyasını taşır; onlar da güncellenir.
        await _db.ServisKayitlari.UpdateManyAsync(s => s.TeknisyenId == id,
            Builders<ServisKaydi>.Update.Set(s => s.TeknisyenAdi, teknisyen.AdSoyad), cancellationToken: iptal);
        await _db.Randevular.UpdateManyAsync(r => r.TeknisyenId == id,
            Builders<Randevu>.Update.Set(r => r.TeknisyenAdi, teknisyen.AdSoyad), cancellationToken: iptal);

        return (await YanitlaraCevir([teknisyen], iptal))[0];
    }

    [HttpDelete("{id:oid}")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<IActionResult> Sil(string id, CancellationToken iptal)
    {
        var acikServisVar = await _db.ServisKayitlari.Find(s => s.TeknisyenId == id && s.SilinmeTarihi == null
            && (s.Durum == ServisDurumu.Beklemede || s.Durum == ServisDurumu.DevamEdiyor)).AnyAsync(iptal);
        if (acikServisVar)
            return Problem(detail: "Teknisyene atanmış devam eden servis kaydı var; önce kaydı başka birine atayın.",
                statusCode: StatusCodes.Status409Conflict);

        var planliRandevuVar = await _db.Randevular
            .Find(r => r.TeknisyenId == id && r.Durum == RandevuDurumu.Planlandi).AnyAsync(iptal);
        if (planliRandevuVar)
            return Problem(detail: "Teknisyene atanmış planlı randevu var; önce randevuyu başka birine atayın.",
                statusCode: StatusCodes.Status409Conflict);

        // Belge silinmez, işaretlenir; kullanıcı hesabı bağlantısı kaldırılır ki hesap başka teknisyene bağlanabilsin.
        var sonuc = await _db.Teknisyenler.UpdateOneAsync(t => t.Id == id && t.SilinmeTarihi == null,
            Builders<Teknisyen>.Update
                .Set(t => t.SilinmeTarihi, DateTime.UtcNow)
                .Unset(t => t.KullaniciId),
            cancellationToken: iptal);

        return sonuc.ModifiedCount == 0 ? Bulunamadi() : NoContent();
    }

    private static void Uygula(Teknisyen teknisyen, TeknisyenIstegi istek)
    {
        teknisyen.AdSoyad = istek.AdSoyad.Trim();
        teknisyen.Telefon = istek.Telefon.Trim();
        teknisyen.Uzmanlik = Metin.BosIseNull(istek.Uzmanlik);
        teknisyen.KullaniciId = Metin.BosIseNull(istek.KullaniciId);
        teknisyen.AramaMetni = AramaMetni.Olustur(teknisyen.AdSoyad, teknisyen.Telefon, teknisyen.Uzmanlik);
    }

    private async Task<bool> HesapYok(string? kullaniciId, CancellationToken iptal) =>
        !string.IsNullOrWhiteSpace(kullaniciId)
        && !await _db.Kullanicilar.Find(k => k.Id == kullaniciId).AnyAsync(iptal);

    private async Task<List<TeknisyenYaniti>> YanitlaraCevir(IReadOnlyList<Teknisyen> teknisyenler, CancellationToken iptal)
    {
        var hesapKimlikleri = teknisyenler.Where(t => t.KullaniciId is not null).Select(t => t.KullaniciId!).ToList();
        var hesapAdlari = hesapKimlikleri.Count == 0
            ? []
            : (await _db.Kullanicilar.Find(Builders<Kullanici>.Filter.In(k => k.Id, hesapKimlikleri)).ToListAsync(iptal))
                .ToDictionary(k => k.Id, k => k.KullaniciAdi);

        return teknisyenler.Select(t => new TeknisyenYaniti(t.Id, t.AdSoyad, t.Telefon, t.Uzmanlik, t.KullaniciId,
            t.KullaniciId is not null ? hesapAdlari.GetValueOrDefault(t.KullaniciId) : null)).ToList();
    }

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Teknisyen bulunamadı.", statusCode: StatusCodes.Status404NotFound);

    private ObjectResult HesapBulunamadi() =>
        Problem(detail: "Seçilen kullanıcı hesabı bulunamadı.", statusCode: StatusCodes.Status400BadRequest);

    private ObjectResult HesapBaskasinaBagli() =>
        Problem(detail: "Bu kullanıcı hesabı zaten başka bir teknisyene bağlı.", statusCode: StatusCodes.Status409Conflict);
}
