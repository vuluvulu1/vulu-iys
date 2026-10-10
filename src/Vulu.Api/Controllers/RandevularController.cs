using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/randevular")]
public class RandevularController : ControllerBase
{
    private const int EnFazlaSayfaBoyutu = 100;

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
        if (baslangic is not null) filtre &= f.Gte(r => r.RandevuTarihi, UtcYap(baslangic.Value));
        if (bitis is not null) filtre &= f.Lt(r => r.RandevuTarihi, UtcYap(bitis.Value));
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
        if (UtcYap(istek.RandevuTarihi) < DateTime.UtcNow - GecmisToleransi)
            return Problem(detail: "Randevu tarihi geçmişte olamaz.", statusCode: StatusCodes.Status400BadRequest);

        var randevu = new Randevu();
        if (await Uygula(randevu, istek, iptal) is { } hata) return hata;

        await _db.Randevular.InsertOneAsync(randevu, cancellationToken: iptal);
        return CreatedAtAction(nameof(Getir), new { id = randevu.Id }, randevu.YanitaCevir());
    }

    [HttpPut("{id:oid}")]
    public async Task<ActionResult<RandevuYaniti>> Guncelle(string id, RandevuIstegi istek, CancellationToken iptal)
    {
        var randevu = await Bul(id, iptal);
        if (randevu is null) return Bulunamadi();
        if (await Uygula(randevu, istek, iptal) is { } hata) return hata;

        await _db.Randevular.ReplaceOneAsync(r => r.Id == id, randevu, cancellationToken: iptal);
        return randevu.YanitaCevir();
    }

    [HttpPut("{id:oid}/durum")]
    public async Task<ActionResult<RandevuYaniti>> DurumuDegistir(string id, RandevuDurumuIstegi istek, CancellationToken iptal)
    {
        var randevu = await _db.Randevular.FindOneAndUpdateAsync<Randevu>(r => r.Id == id,
            Builders<Randevu>.Update.Set(r => r.Durum, istek.Durum),
            new FindOneAndUpdateOptions<Randevu> { ReturnDocument = ReturnDocument.After }, iptal);

        return randevu is null ? Bulunamadi() : randevu.YanitaCevir();
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

        var teknisyenId = Metin.BosIseNull(istek.TeknisyenId);
        Teknisyen? teknisyen = null;
        if (teknisyenId is not null)
        {
            teknisyen = await _db.Teknisyenler
                .Find(t => t.Id == teknisyenId && t.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
            if (teknisyen is null) return Gecersiz("Seçilen teknisyen bulunamadı.");
        }

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
        randevu.RandevuTarihi = UtcYap(istek.RandevuTarihi);
        randevu.Aciklama = Metin.BosIseNull(istek.Aciklama);
        return null;
    }

    // Saat dilimi belirtilmemiş tarihler UTC sayılır.
    private static DateTime UtcYap(DateTime tarih) => tarih.Kind switch
    {
        DateTimeKind.Utc => tarih,
        DateTimeKind.Local => tarih.ToUniversalTime(),
        _ => DateTime.SpecifyKind(tarih, DateTimeKind.Utc)
    };

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
        r.ServisKaydiId, r.RandevuTarihi, r.Aciklama, r.Durum);
}
