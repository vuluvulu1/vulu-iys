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
[Route("api/servis-kayitlari")]
public class ServisKayitlariController : ControllerBase
{
    private const int EnFazlaSayfaBoyutu = 100;

    private readonly MongoBaglami _db;

    public ServisKayitlariController(MongoBaglami db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<Sayfa<ServisKaydiYaniti>>> Listele(
        [FromQuery] string? arama, [FromQuery] ServisDurumu? durum,
        [FromQuery] FaturaSuzgeci fatura = FaturaSuzgeci.Hepsi,
        [FromQuery] string? musteriId = null, [FromQuery] string? teknisyenId = null,
        [FromQuery] int sayfa = 1, [FromQuery] int boyut = 25,
        CancellationToken iptal = default)
    {
        if (!KimlikGecerli(musteriId) || !KimlikGecerli(teknisyenId))
            return Problem(detail: "Geçersiz süzgeç.", statusCode: StatusCodes.Status400BadRequest);

        sayfa = Math.Max(1, sayfa);
        boyut = Math.Clamp(boyut, 1, EnFazlaSayfaBoyutu);

        var f = Builders<ServisKaydi>.Filter;
        var filtre = f.Eq(s => s.SilinmeTarihi, null);

        if (durum is not null) filtre &= f.Eq(s => s.Durum, durum.Value);
        if (musteriId is not null) filtre &= f.Eq(s => s.MusteriId, musteriId);
        if (teknisyenId is not null) filtre &= f.Eq(s => s.TeknisyenId, teknisyenId);

        filtre &= fatura switch
        {
            FaturaSuzgeci.Faturali => f.Ne(s => s.Fatura, null),
            FaturaSuzgeci.Odenmemis => f.Eq(s => s.Fatura!.OdendiMi, false),
            FaturaSuzgeci.Odenmis => f.Eq(s => s.Fatura!.OdendiMi, true),
            _ => f.Empty
        };

        if (!string.IsNullOrWhiteSpace(arama))
        {
            var desen = AramaMetni.Desen(arama);
            filtre &= f.Or(f.Regex(s => s.AramaMetni, desen), f.Regex(s => s.Musteri.AramaMetni, desen));
        }

        var toplam = await _db.ServisKayitlari.CountDocumentsAsync(filtre, cancellationToken: iptal);
        var kayitlar = await _db.ServisKayitlari.Find(filtre)
            .SortByDescending(s => s.OlusturmaTarihi)
            .Skip((sayfa - 1) * boyut)
            .Limit(boyut)
            .ToListAsync(iptal);

        return new Sayfa<ServisKaydiYaniti>(kayitlar.Select(k => k.YanitaCevir()).ToList(), toplam, sayfa, boyut);
    }

    [HttpGet("{id:oid}")]
    public async Task<ActionResult<ServisKaydiYaniti>> Getir(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        return kayit is null ? Bulunamadi() : kayit.YanitaCevir();
    }

    [HttpPost]
    public async Task<ActionResult<ServisKaydiYaniti>> Ekle(ServisKaydiIstegi istek, CancellationToken iptal)
    {
        var kayit = new ServisKaydi();
        if (await Uygula(kayit, istek, iptal) is { } hata) return hata;

        await _db.ServisKayitlari.InsertOneAsync(kayit, cancellationToken: iptal);
        return CreatedAtAction(nameof(Getir), new { id = kayit.Id }, kayit.YanitaCevir());
    }

    [HttpPut("{id:oid}")]
    public async Task<ActionResult<ServisKaydiYaniti>> Guncelle(string id, ServisKaydiIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();
        if (await Uygula(kayit, istek, iptal) is { } hata) return hata;

        return await Kaydet(kayit, iptal);
    }

    [HttpPut("{id:oid}/durum")]
    public async Task<ActionResult<ServisKaydiYaniti>> DurumuDegistir(string id, ServisDurumuIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        if (ServisKaydiKurallari.DurumuDegistir(kayit, istek.Durum, DateTime.UtcNow) is { } hata)
            return KuralIhlali(hata);

        return await Kaydet(kayit, iptal);
    }

    // Fatura yoksa keser, varsa tutarını ve ödeme durumunu günceller.
    [HttpPut("{id:oid}/fatura")]
    public async Task<ActionResult<ServisKaydiYaniti>> FaturayiKaydet(string id, FaturaIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        if (ServisKaydiKurallari.FaturayiKaydet(kayit, istek.Tutar, istek.OdendiMi, DateTime.UtcNow) is { } hata)
            return KuralIhlali(hata);

        return await Kaydet(kayit, iptal);
    }

    [HttpDelete("{id:oid}/fatura")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<ActionResult<ServisKaydiYaniti>> FaturayiSil(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();
        if (kayit.Fatura is null)
            return Problem(detail: "Bu kaydın faturası yok.", statusCode: StatusCodes.Status404NotFound);

        kayit.Fatura = null;
        return await Kaydet(kayit, iptal);
    }

    [HttpDelete("{id:oid}")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<IActionResult> Sil(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        // Belge silinmez, işaretlenir.
        if (ServisKaydiKurallari.Sil(kayit, DateTime.UtcNow) is { } hata)
            return KuralIhlali(hata);

        var sonuc = await Kaydet(kayit, iptal);
        return sonuc.Result is ObjectResult cakisma ? cakisma : NoContent();
    }

    private Task<ServisKaydi?> Bul(string id, CancellationToken iptal) =>
        _db.ServisKayitlari.Find(s => s.Id == id && s.SilinmeTarihi == null).FirstOrDefaultAsync(iptal)!;

    // İstekteki alanları kayda işler; müşteri ve teknisyenin güncel ad bilgisini kopyalar.
    private async Task<ObjectResult?> Uygula(ServisKaydi kayit, ServisKaydiIstegi istek, CancellationToken iptal)
    {
        var musteri = await _db.Musteriler
            .Find(m => m.Id == istek.MusteriId && m.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
        if (musteri is null)
            return Problem(detail: "Seçilen müşteri bulunamadı.", statusCode: StatusCodes.Status400BadRequest);

        var teknisyenId = Metin.BosIseNull(istek.TeknisyenId);
        Teknisyen? teknisyen = null;
        if (teknisyenId is not null)
        {
            teknisyen = await _db.Teknisyenler
                .Find(t => t.Id == teknisyenId && t.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
            if (teknisyen is null)
                return Problem(detail: "Seçilen teknisyen bulunamadı.", statusCode: StatusCodes.Status400BadRequest);
        }

        kayit.MusteriId = musteri.Id;
        kayit.Musteri = musteri.OzetiniCikar();
        kayit.TeknisyenId = teknisyen?.Id;
        kayit.TeknisyenAdi = teknisyen?.AdSoyad;
        kayit.CihazBilgisi = istek.CihazBilgisi.Trim();
        kayit.Sorun = istek.Sorun.Trim();
        kayit.AramaMetni = AramaMetni.Olustur(kayit.CihazBilgisi, kayit.Sorun);
        return null;
    }

    // Kaydı yalnızca okunduğu sürümdeyse yazar; arada başkası değiştirdiyse 409 döner.
    private async Task<ActionResult<ServisKaydiYaniti>> Kaydet(ServisKaydi kayit, CancellationToken iptal)
    {
        var okunanSurum = kayit.Surum;
        kayit.Surum++;

        var sonuc = await _db.ServisKayitlari.ReplaceOneAsync(
            s => s.Id == kayit.Id && s.Surum == okunanSurum, kayit, cancellationToken: iptal);

        if (sonuc.MatchedCount == 0)
            return Problem(detail: "Kayıt bu sırada başka biri tarafından değiştirildi. Yenileyip yeniden deneyin.",
                statusCode: StatusCodes.Status409Conflict);

        return kayit.YanitaCevir();
    }

    private static bool KimlikGecerli(string? kimlik) => kimlik is null || ObjectId.TryParse(kimlik, out _);

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Servis kaydı bulunamadı.", statusCode: StatusCodes.Status404NotFound);

    private ObjectResult KuralIhlali(string mesaj) =>
        Problem(detail: mesaj, statusCode: StatusCodes.Status409Conflict);
}

public static class ServisKaydiDonusumleri
{
    public static ServisKaydiYaniti YanitaCevir(this ServisKaydi k) => new(
        k.Id, k.MusteriId, k.Musteri.AdSoyad, k.Musteri.Telefon, k.TeknisyenId, k.TeknisyenAdi,
        k.CihazBilgisi, k.Sorun, k.Durum, k.OlusturmaTarihi, k.TamamlanmaTarihi,
        k.Fatura is null
            ? null
            : new FaturaYaniti(k.Fatura.Tutar, k.Fatura.OdendiMi, k.Fatura.OlusturmaTarihi, k.Fatura.OdemeTarihi));
}
