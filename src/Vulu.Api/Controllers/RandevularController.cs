using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kurallar;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/randevular")]
public class RandevularController : ControllerBase
{
    private const int EnFazlaSayfaBoyutu = 200;

    // Saat farkları ve formu doldururken geçen süre için küçük bir pay.
    private static readonly TimeSpan GecmisToleransi = TimeSpan.FromMinutes(10);

    private readonly MongoBaglami _db;

    public RandevularController(MongoBaglami db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<Sayfa<RandevuYaniti>>> Listele(
        [FromQuery] string? arama, [FromQuery] RandevuDurumu? durum,
        [FromQuery] string? musteriId = null, [FromQuery] string? teknisyenId = null,
        [FromQuery] DateTime? baslangic = null, [FromQuery] DateTime? bitis = null,
        [FromQuery] int sayfa = 1, [FromQuery] int boyut = 25,
        CancellationToken iptal = default)
    {
        if (!KimlikGecerli(musteriId) || !KimlikGecerli(teknisyenId))
            return Problem(detail: "Geçersiz süzgeç.", statusCode: StatusCodes.Status400BadRequest);

        sayfa = Math.Max(1, sayfa);
        boyut = Math.Clamp(boyut, 1, EnFazlaSayfaBoyutu);

        var f = Builders<Randevu>.Filter;
        var filtre = f.Empty;

        if (durum is not null) filtre &= f.Eq(r => r.Durum, durum.Value);
        if (musteriId is not null) filtre &= f.Eq(r => r.MusteriId, musteriId);
        if (teknisyenId is not null) filtre &= f.Eq(r => r.TeknisyenId, teknisyenId);
        if (baslangic is not null) filtre &= f.Gte(r => r.RandevuTarihi, Zaman.UtcYap(baslangic.Value));
        if (bitis is not null) filtre &= f.Lt(r => r.RandevuTarihi, Zaman.UtcYap(bitis.Value));
        if (!string.IsNullOrWhiteSpace(arama))
            filtre &= f.Regex(r => r.Musteri.AramaMetni, AramaMetni.Desen(arama));

        var toplam = await _db.Randevular.CountDocumentsAsync(filtre, cancellationToken: iptal);
        var randevular = await _db.Randevular.Find(filtre)
            .SortBy(r => r.RandevuTarihi)
            .Skip((sayfa - 1) * boyut)
            .Limit(boyut)
            .ToListAsync(iptal);

        return new Sayfa<RandevuYaniti>(randevular.Select(r => r.YanitaCevir()).ToList(), toplam, sayfa, boyut);
    }

    [HttpGet("{id:oid}")]
    public async Task<ActionResult<RandevuYaniti>> Getir(string id, CancellationToken iptal)
    {
        var randevu = await Bul(id, iptal);
        return randevu is null ? Bulunamadi() : randevu.YanitaCevir();
    }

    [HttpPost]
    public async Task<ActionResult<RandevuYaniti>> Ekle(RandevuIstegi istek, CancellationToken iptal)
    {
        if (Zaman.UtcYap(istek.RandevuTarihi) < DateTime.UtcNow - GecmisToleransi)
            return Problem(detail: "Randevu tarihi geçmişte olamaz.", statusCode: StatusCodes.Status400BadRequest);

        var randevu = new Randevu();
        if (await Uygula(randevu, istek, iptal) is { } hata) return hata;
        if (await CakismayiDenetle(randevu, iptal) is { } cakisma) return cakisma;

        await _db.Randevular.InsertOneAsync(randevu, cancellationToken: iptal);
        return CreatedAtAction(nameof(Getir), new { id = randevu.Id }, randevu.YanitaCevir());
    }

    [HttpPut("{id:oid}")]
    public async Task<ActionResult<RandevuYaniti>> Guncelle(string id, RandevuIstegi istek, CancellationToken iptal)
    {
        var randevu = await Bul(id, iptal);
        if (randevu is null) return Bulunamadi();
        if (await Uygula(randevu, istek, iptal) is { } hata) return hata;
        if (await CakismayiDenetle(randevu, iptal) is { } cakisma) return cakisma;

        return await Kaydet(randevu, iptal);
    }

    [HttpPut("{id:oid}/durum")]
    public async Task<ActionResult<RandevuYaniti>> DurumuDegistir(string id, RandevuDurumuIstegi istek, CancellationToken iptal)
    {
        var randevu = await Bul(id, iptal);
        if (randevu is null) return Bulunamadi();

        randevu.Durum = istek.Durum;

        // İptal edilmiş bir randevu yeniden planlanırken o saat artık dolu olabilir.
        if (await CakismayiDenetle(randevu, iptal) is { } cakisma) return cakisma;

        return await Kaydet(randevu, iptal);
    }

    // Randevunun olağan sonu iptal ya da tamamlanmadır; silme yanlış girilen kayıtlar içindir.
    [HttpDelete("{id:oid}")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<IActionResult> Sil(string id, CancellationToken iptal)
    {
        var sonuc = await _db.Randevular.DeleteOneAsync(r => r.Id == id, iptal);
        return sonuc.DeletedCount == 0 ? Bulunamadi() : NoContent();
    }

    private Task<Randevu?> Bul(string id, CancellationToken iptal) =>
        _db.Randevular.Find(r => r.Id == id).FirstOrDefaultAsync(iptal)!;

    // İstekteki alanları randevuya işler; müşteri ve teknisyenin güncel ad bilgisini kopyalar.
    private async Task<ObjectResult?> Uygula(Randevu randevu, RandevuIstegi istek, CancellationToken iptal)
    {
        var musteri = await _db.Musteriler
            .Find(m => m.Id == istek.MusteriId && m.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
        if (musteri is null) return Gecersiz("Seçilen müşteri bulunamadı.");

        var (teknisyen, atamaHatasi) = await PersonelAtamasi.Coz(_db, istek.TeknisyenId, randevu.TeknisyenId, iptal);
        if (atamaHatasi is not null) return Gecersiz(atamaHatasi);

        var servisKaydiId = Metin.BosIseNull(istek.ServisKaydiId);
        if (servisKaydiId is not null)
        {
            var kayitVar = await _db.ServisKayitlari
                .Find(s => s.Id == servisKaydiId && s.MusteriId == musteri.Id && s.SilinmeTarihi == null).AnyAsync(iptal);
            if (!kayitVar) return Gecersiz("Seçilen servis kaydı bu müşteriye ait değil ya da bulunamadı.");
        }

        randevu.MusteriId = musteri.Id;
        randevu.Musteri = musteri.OzetiniCikar();
        randevu.TeknisyenId = teknisyen?.Id;
        randevu.TeknisyenAdi = teknisyen?.AdSoyad;
        randevu.ServisKaydiId = servisKaydiId;
        randevu.RandevuTarihi = Zaman.UtcYap(istek.RandevuTarihi);
        randevu.SureDakika = istek.SureDakika;
        randevu.BitisTarihi = randevu.RandevuTarihi.AddMinutes(randevu.SureDakika);
        randevu.Aciklama = Metin.BosIseNull(istek.Aciklama);
        return null;
    }

    // Bir teknisyenin planlı randevuları saat olarak üst üste binemez. Teknisyeni olmayan ya da planlı olmayan
    // randevular denetlenmez. Kontrol ile yazma arasında başka bir randevu araya girebilir; bu dar aralık
    // çok belgeli işlem (transaction) olmadan kapatılamaz.
    private async Task<ObjectResult?> CakismayiDenetle(Randevu randevu, CancellationToken iptal)
    {
        if (randevu.TeknisyenId is null || randevu.Durum != RandevuDurumu.Planlandi) return null;

        var f = Builders<Randevu>.Filter;
        var filtre = f.Eq(r => r.TeknisyenId, randevu.TeknisyenId)
            & f.Eq(r => r.Durum, RandevuDurumu.Planlandi)
            & f.Lt(r => r.RandevuTarihi, randevu.BitisTarihi)
            & f.Gt(r => r.BitisTarihi, randevu.RandevuTarihi);
        if (randevu.Id is not null) filtre &= f.Ne(r => r.Id, randevu.Id);

        var cakisan = await _db.Randevular.Find(filtre).SortBy(r => r.RandevuTarihi).FirstOrDefaultAsync(iptal);
        if (cakisan is null) return null;

        return Problem(
            detail: $"{randevu.TeknisyenAdi} için bu saatte başka bir randevu var: "
                + $"{Zaman.TarihSaat(cakisan.RandevuTarihi)}, {cakisan.Musteri.AdSoyad} ({cakisan.SureDakika} dk).",
            statusCode: StatusCodes.Status409Conflict);
    }

    // Randevuyu yalnızca okunduğu sürümdeyse yazar; arada başkası değiştirdiyse 409 döner.
    private async Task<ActionResult<RandevuYaniti>> Kaydet(Randevu randevu, CancellationToken iptal)
    {
        var okunanSurum = randevu.Surum;
        randevu.Surum++;

        var sonuc = await _db.Randevular.ReplaceOneAsync(
            r => r.Id == randevu.Id && r.Surum == okunanSurum, randevu, cancellationToken: iptal);

        if (sonuc.MatchedCount == 0)
            return Problem(detail: "Randevu bu sırada başka biri tarafından değiştirildi. Yenileyip yeniden deneyin.",
                statusCode: StatusCodes.Status409Conflict);

        return randevu.YanitaCevir();
    }

    private static bool KimlikGecerli(string? kimlik) => kimlik is null || ObjectId.TryParse(kimlik, out _);

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Randevu bulunamadı.", statusCode: StatusCodes.Status404NotFound);

    private ObjectResult Gecersiz(string mesaj) =>
        Problem(detail: mesaj, statusCode: StatusCodes.Status400BadRequest);
}

public static class RandevuDonusumleri
{
    public static RandevuYaniti YanitaCevir(this Randevu r) => new(
        r.Id, r.MusteriId, r.Musteri.AdSoyad, r.Musteri.Telefon, r.TeknisyenId, r.TeknisyenAdi,
        r.ServisKaydiId, r.RandevuTarihi, r.SureDakika, r.Aciklama, r.Durum);
}
