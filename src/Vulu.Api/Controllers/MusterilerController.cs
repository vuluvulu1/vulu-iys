using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kurallar;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/musteriler")]
public class MusterilerController : ControllerBase
{
    private const int EnFazlaSayfaBoyutu = 100;

    private readonly MongoBaglami _db;

    public MusterilerController(MongoBaglami db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<Sayfa<MusteriYaniti>>> Listele(
        [FromQuery] string? arama, [FromQuery] int sayfa = 1, [FromQuery] int boyut = 25,
        CancellationToken iptal = default)
    {
        sayfa = Math.Max(1, sayfa);
        boyut = Math.Clamp(boyut, 1, EnFazlaSayfaBoyutu);

        var filtre = Builders<Musteri>.Filter.Eq(m => m.SilinmeTarihi, null);
        if (!string.IsNullOrWhiteSpace(arama))
            filtre &= Builders<Musteri>.Filter.Regex(m => m.AramaMetni, AramaMetni.Desen(arama));

        var toplam = await _db.Musteriler.CountDocumentsAsync(filtre, cancellationToken: iptal);
        var musteriler = await _db.Musteriler
            .Find(filtre, new FindOptions { Collation = MongoBaglami.TurkceSiralama })
            .SortBy(m => m.AdSoyad)
            .Skip((sayfa - 1) * boyut)
            .Limit(boyut)
            .ToListAsync(iptal);

        return new Sayfa<MusteriYaniti>(musteriler.Select(m => m.YanitaCevir()).ToList(), toplam, sayfa, boyut);
    }

    [HttpGet("{id:oid}")]
    public async Task<ActionResult<MusteriYaniti>> Getir(string id, CancellationToken iptal)
    {
        var musteri = await _db.Musteriler.Find(m => m.Id == id && m.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
        return musteri is null ? Bulunamadi() : musteri.YanitaCevir();
    }

    [HttpPost]
    public async Task<ActionResult<MusteriYaniti>> Ekle(MusteriIstegi istek, CancellationToken iptal)
    {
        var musteri = new Musteri();
        Uygula(musteri, istek);

        await _db.Musteriler.InsertOneAsync(musteri, cancellationToken: iptal);
        return CreatedAtAction(nameof(Getir), new { id = musteri.Id }, musteri.YanitaCevir());
    }

    [HttpPut("{id:oid}")]
    public async Task<ActionResult<MusteriYaniti>> Guncelle(string id, MusteriIstegi istek, CancellationToken iptal)
    {
        var musteri = await _db.Musteriler.Find(m => m.Id == id && m.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
        if (musteri is null) return Bulunamadi();

        Uygula(musteri, istek);
        await _db.Musteriler.ReplaceOneAsync(m => m.Id == id, musteri, cancellationToken: iptal);

        // Servis kayıtları ve randevular müşterinin ad ve telefonunun kopyasını taşır; onlar da güncellenir.
        // Servis kaydının sürümü de artar ki o sırada kaydı düzenleyen biri eski kopyayı geri yazamasın.
        var ozet = musteri.OzetiniCikar();
        await _db.ServisKayitlari.UpdateManyAsync(s => s.MusteriId == id,
            Builders<ServisKaydi>.Update.Set(s => s.Musteri, ozet).Inc(s => s.Surum, 1), cancellationToken: iptal);
        await _db.Randevular.UpdateManyAsync(r => r.MusteriId == id,
            Builders<Randevu>.Update.Set(r => r.Musteri, ozet).Inc(r => r.Surum, 1), cancellationToken: iptal);

        return musteri.YanitaCevir();
    }

    [HttpDelete("{id:oid}")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<IActionResult> Sil(string id, CancellationToken iptal)
    {
        var acikServisVar = await _db.ServisKayitlari.Find(s => s.MusteriId == id && s.SilinmeTarihi == null
            && ServisKaydiKurallari.AcikDurumlar.Contains(s.Durum)).AnyAsync(iptal);
        if (acikServisVar)
            return Problem(detail: "Müşterinin devam eden servis kaydı var; önce kaydı tamamlayın veya iptal edin.",
                statusCode: StatusCodes.Status409Conflict);

        var planliRandevuVar = await _db.Randevular
            .Find(r => r.MusteriId == id && r.Durum == RandevuDurumu.Planlandi).AnyAsync(iptal);
        if (planliRandevuVar)
            return Problem(detail: "Müşterinin planlanmış randevusu var; önce randevuyu tamamlayın veya iptal edin.",
                statusCode: StatusCodes.Status409Conflict);

        // Belge silinmez, işaretlenir: geçmiş servis kayıtları ve faturalar müşteriye bağlı kalır.
        var sonuc = await _db.Musteriler.UpdateOneAsync(m => m.Id == id && m.SilinmeTarihi == null,
            Builders<Musteri>.Update.Set(m => m.SilinmeTarihi, DateTime.UtcNow), cancellationToken: iptal);

        return sonuc.ModifiedCount == 0 ? Bulunamadi() : NoContent();
    }

    private static void Uygula(Musteri musteri, MusteriIstegi istek)
    {
        musteri.AdSoyad = istek.AdSoyad.Trim();
        musteri.Telefon = istek.Telefon.Trim();
        musteri.Email = Metin.BosIseNull(istek.Email);
        musteri.Adres = Metin.BosIseNull(istek.Adres);
        musteri.AramaMetni = AramaMetni.Olustur(musteri.AdSoyad, musteri.Telefon, musteri.Email);
    }

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Müşteri bulunamadı.", statusCode: StatusCodes.Status404NotFound);
}

public static class MusteriDonusumleri
{
    public static MusteriYaniti YanitaCevir(this Musteri m) =>
        new(m.Id, m.AdSoyad, m.Telefon, m.Email, m.Adres, m.OlusturmaTarihi);
}
