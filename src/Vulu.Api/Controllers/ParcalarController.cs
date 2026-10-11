using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

// Yedek parça stoğu. Herkes görür ve servis kaydında kullanır (bkz. ServisKayitlariController.ParcaEkle);
// stok kartlarını ve stok girişlerini yalnızca işletme yöneticisi yönetir.
[ApiController]
[Route("api/parcalar")]
public class ParcalarController : ControllerBase
{
    private const int EnFazlaSayfaBoyutu = 100;
    private const int EnFazlaHareket = 200;

    // Stoğu kritik seviyeye inmiş ya da altına düşmüş parçalar (iki alan karşılaştırılır).
    public static readonly FilterDefinition<Parca> AzalanFiltresi =
        new BsonDocument("$expr", new BsonDocument("$lte", new BsonArray { "$stokAdedi", "$kritikSeviye" }));

    private readonly MongoBaglami _db;
    private readonly IslemGunlugu _gunluk;

    public ParcalarController(MongoBaglami db, IslemGunlugu gunluk)
    {
        _db = db;
        _gunluk = gunluk;
    }

    [HttpGet]
    public async Task<ActionResult<Sayfa<ParcaYaniti>>> Listele(
        [FromQuery, StringLength(AramaMetni.EnFazlaUzunluk, ErrorMessage = AramaMetni.UzunlukMesaji)] string? arama, [FromQuery] bool azalan = false,
        [FromQuery] int sayfa = 1, [FromQuery] int boyut = 25, CancellationToken iptal = default)
    {
        var sayfalama = Sayfalama.Olustur(sayfa, boyut, EnFazlaSayfaBoyutu);

        var f = Builders<Parca>.Filter;
        var filtre = f.Eq(p => p.SilinmeTarihi, null);
        if (!string.IsNullOrWhiteSpace(arama))
            filtre &= f.Regex(p => p.AramaMetni, AramaMetni.Desen(arama));

        if (azalan)
            filtre &= AzalanFiltresi;

        var toplam = await _db.Parcalar.CountDocumentsAsync(filtre, cancellationToken: iptal);
        var parcalar = await _db.Parcalar
            .Find(filtre, new FindOptions { Collation = MongoBaglami.TurkceSiralama })
            .SortBy(p => p.Ad)
            .Skip(sayfalama.Atla)
            .Limit(sayfalama.Boyut)
            .ToListAsync(iptal);

        return new Sayfa<ParcaYaniti>(parcalar.Select(p => p.YanitaCevir()).ToList(), toplam, sayfalama.Sayfa, sayfalama.Boyut);
    }

    [HttpGet("disa-aktar")]
    public async Task<IActionResult> DisaAktar(CancellationToken iptal)
    {
        var parcalar = await _db.Parcalar
            .Find(p => p.SilinmeTarihi == null, new FindOptions { Collation = MongoBaglami.TurkceSiralama })
            .SortBy(p => p.Ad)
            .Limit(DisaAktarim.EnFazlaSatir)
            .ToListAsync(iptal);

        return File(ExcelYazici.Olustur(DisaAktarim.Parcalar(parcalar)), ExcelYazici.IcerikTuru, DisaAktarim.DosyaAdi("Stok"));
    }

    [HttpGet("{id:oid}")]
    public async Task<ActionResult<ParcaYaniti>> Getir(string id, CancellationToken iptal)
    {
        var parca = await Bul(id, iptal);
        return parca is null ? Bulunamadi() : parca.YanitaCevir();
    }

    [HttpPost]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<ActionResult<ParcaYaniti>> Ekle(ParcaIstegi istek, CancellationToken iptal)
    {
        if (FiyatHatasi(istek) is { } hata) return hata;

        var parca = new Parca
        {
            Ad = istek.Ad.Trim(),
            Kod = Metin.BosIseNull(istek.Kod),
            BirimFiyat = istek.BirimFiyat,
            KritikSeviye = istek.KritikSeviye
        };
        parca.AramaMetni = AramaMetni.Olustur(parca.Ad, parca.Kod);

        await _db.Parcalar.InsertOneAsync(parca, cancellationToken: iptal);
        return CreatedAtAction(nameof(Getir), new { id = parca.Id }, parca.YanitaCevir());
    }

    [HttpPut("{id:oid}")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<ActionResult<ParcaYaniti>> Guncelle(string id, ParcaIstegi istek, CancellationToken iptal)
    {
        if (FiyatHatasi(istek) is { } hata) return hata;

        var ad = istek.Ad.Trim();
        var kod = Metin.BosIseNull(istek.Kod);

        // Yalnızca kart alanları yazılır; stok adedi bu sırada kullanılan parçalarla değişmiş olabilir.
        var parca = await _db.Parcalar.FindOneAndUpdateAsync<Parca>(
            p => p.Id == id && p.SilinmeTarihi == null,
            Builders<Parca>.Update
                .Set(p => p.Ad, ad)
                .Set(p => p.Kod, kod)
                .Set(p => p.BirimFiyat, istek.BirimFiyat)
                .Set(p => p.KritikSeviye, istek.KritikSeviye)
                .Set(p => p.AramaMetni, AramaMetni.Olustur(ad, kod)),
            new FindOneAndUpdateOptions<Parca> { ReturnDocument = ReturnDocument.After }, iptal);

        return parca is null ? Bulunamadi() : parca.YanitaCevir();
    }

    // Stok girişi (artı adet) ya da sayım düzeltmesi (eksi adet).
    [HttpPost("{id:oid}/stok")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<ActionResult<ParcaYaniti>> StokDegistir(string id, StokHareketiIstegi istek, CancellationToken iptal)
    {
        if (istek.Adet == 0)
            return Problem(detail: "Adet sıfır olamaz.", statusCode: StatusCodes.Status400BadRequest);

        // Eksi hareket stoğu sıfırın altına düşüremez; koşul ve azaltma tek işlemdir.
        var parca = await _db.Parcalar.FindOneAndUpdateAsync<Parca>(
            p => p.Id == id && p.SilinmeTarihi == null && p.StokAdedi >= -istek.Adet,
            Builders<Parca>.Update.Inc(p => p.StokAdedi, istek.Adet),
            new FindOneAndUpdateOptions<Parca> { ReturnDocument = ReturnDocument.After }, iptal);

        if (parca is null)
        {
            return await Bul(id, iptal) is { } mevcut
                ? Problem(detail: $"Stokta yalnızca {mevcut.StokAdedi} adet var.", statusCode: StatusCodes.Status409Conflict)
                : Bulunamadi();
        }

        await _gunluk.StokHareketiYazAsync(id, istek.Adet,
            Metin.BosIseNull(istek.Aciklama) ?? (istek.Adet > 0 ? "Stok girişi" : "Stok düzeltmesi"), iptal: iptal);
        return parca.YanitaCevir();
    }

    [HttpGet("{id:oid}/hareketler")]
    public async Task<ActionResult<IReadOnlyList<StokHareketiYaniti>>> Hareketler(string id, CancellationToken iptal)
    {
        if (await Bul(id, iptal) is null) return Bulunamadi();

        var hareketler = await _db.StokHareketleri.Find(h => h.ParcaId == id)
            .SortByDescending(h => h.Tarih)
            .Limit(EnFazlaHareket)
            .ToListAsync(iptal);

        return hareketler
            .Select(h => new StokHareketiYaniti(h.Id, h.Tarih, h.Adet, h.Aciklama, h.KullaniciAdi, h.ServisKaydiNo))
            .ToList();
    }

    [HttpDelete("{id:oid}")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<IActionResult> Sil(string id, CancellationToken iptal)
    {
        // Belge silinmez, işaretlenir: geçmiş servis kayıtları ve stok hareketleri parçaya bağlı kalır.
        var sonuc = await _db.Parcalar.UpdateOneAsync(p => p.Id == id && p.SilinmeTarihi == null,
            Builders<Parca>.Update.Set(p => p.SilinmeTarihi, DateTime.UtcNow), cancellationToken: iptal);

        return sonuc.ModifiedCount == 0 ? Bulunamadi() : NoContent();
    }

    private Task<Parca?> Bul(string id, CancellationToken iptal) =>
        _db.Parcalar.Find(p => p.Id == id && p.SilinmeTarihi == null).FirstOrDefaultAsync(iptal)!;

    private ObjectResult? FiyatHatasi(ParcaIstegi istek) =>
        decimal.Round(istek.BirimFiyat, 2) != istek.BirimFiyat
            ? Problem(detail: "Birim fiyat en fazla iki ondalık hane içerebilir.", statusCode: StatusCodes.Status400BadRequest)
            : null;

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Parça bulunamadı.", statusCode: StatusCodes.Status404NotFound);
}

public static class ParcaDonusumleri
{
    public static ParcaYaniti YanitaCevir(this Parca p) =>
        new(p.Id, p.Ad, p.Kod, p.BirimFiyat, p.StokAdedi, p.KritikSeviye);
}
