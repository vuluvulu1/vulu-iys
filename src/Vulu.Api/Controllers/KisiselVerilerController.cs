using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kimlik;
using Vulu.Api.Kurallar;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

// Müşterilerin kişisel verileri üzerindeki hakları (KVKK): verinin dökümünü alma, geri dönüşsüz silme ve saklama
// süresi dolanların listesi. Yalnızca işletme yöneticisi kullanır; her işlem API günlüğüne yazılır.
[ApiController]
[Route("api/kisisel-veriler")]
[Authorize(Roles = nameof(KullaniciRol.Admin))]
public class KisiselVerilerController : ControllerBase
{
    private const int ListeSiniri = 500;

    private readonly MongoBaglami _db;
    private readonly GecerliIsletme _isletme;
    private readonly ILogger<KisiselVerilerController> _log;

    public KisiselVerilerController(MongoBaglami db, GecerliIsletme isletme, ILogger<KisiselVerilerController> log)
    {
        _db = db;
        _isletme = isletme;
        _log = log;
    }

    // Silinmiş olarak işaretlenmiş ama verisi hâlâ duran müşteriler ile son işlemi saklama süresinden eski olanlar.
    [HttpGet]
    public async Task<ActionResult<KvkkDurumu>> Getir(CancellationToken iptal)
    {
        var saklamaYili = _isletme.Zorunlu.SaklamaYili;
        var esik = DateTime.UtcNow.AddYears(-saklamaYili);

        // Müşterinin son işlemi: kendi kaydı, en yeni servis kaydı ve en yeni randevusundan en geç olanı.
        var sonKayitlar = (await _db.ServisKayitlari.Aggregate()
                .Group(s => s.MusteriId, grup => new { Id = grup.Key, Son = grup.Max(s => s.OlusturmaTarihi) })
                .ToListAsync(iptal))
            .ToDictionary(g => g.Id, g => g.Son);
        var sonRandevular = (await _db.Randevular.Aggregate()
                .Group(r => r.MusteriId, grup => new { Id = grup.Key, Son = grup.Max(r => r.RandevuTarihi) })
                .ToListAsync(iptal))
            .ToDictionary(g => g.Id, g => g.Son);

        var musteriler = await _db.Musteriler.Find(FilterDefinition<Musteri>.Empty).ToListAsync(iptal);
        var satirlar = musteriler.Select(m =>
        {
            var son = m.OlusturmaTarihi;
            if (sonKayitlar.TryGetValue(m.Id, out var kayit) && kayit > son) son = kayit;
            if (sonRandevular.TryGetValue(m.Id, out var randevu) && randevu > son) son = randevu;
            return new KvkkMusterisi(m.Id, m.AdSoyad, m.Telefon, son, m.SilinmeTarihi is not null);
        }).ToList();

        return new KvkkDurumu(
            saklamaYili,
            satirlar.Where(s => s.SilinmisMi).OrderBy(s => s.SonIslemTarihi).Take(ListeSiniri).ToList(),
            satirlar.Where(s => !s.SilinmisMi && s.SonIslemTarihi < esik).OrderBy(s => s.SonIslemTarihi).Take(ListeSiniri).ToList());
    }

    // Müşteri hakkında tutulan her şeyin dökümü (erişim hakkı). Silinmiş olarak işaretlenmiş müşteriler için de alınabilir.
    [HttpGet("musteriler/{id:oid}/dokum")]
    public async Task<IActionResult> Dokum(string id, CancellationToken iptal)
    {
        var musteri = await _db.Musteriler.Find(m => m.Id == id).FirstOrDefaultAsync(iptal);
        if (musteri is null) return Bulunamadi();

        var kayitlar = await _db.ServisKayitlari.Find(s => s.MusteriId == id).SortBy(s => s.OlusturmaTarihi).ToListAsync(iptal);
        var randevular = await _db.Randevular.Find(r => r.MusteriId == id).SortBy(r => r.RandevuTarihi).ToListAsync(iptal);

        var dosya = ExcelYazici.Olustur(
            new ExcelSayfasi("Müşteri", ["Alan", "Değer"],
            [
                ["Ad soyad", musteri.AdSoyad],
                ["Telefon", musteri.Telefon],
                ["E-posta", musteri.Email],
                ["Adres", musteri.Adres],
                ["Kayıt tarihi", musteri.OlusturmaTarihi],
                ["Silinme tarihi", musteri.SilinmeTarihi]
            ]),
            DisaAktarim.ServisKayitlari(kayitlar),
            new ExcelSayfasi("Randevular", ["Tarih", "Süre (dk)", "Teknisyen", "Durum", "Açıklama"],
                randevular.Select(r => new object?[] { r.RandevuTarihi, r.SureDakika, r.TeknisyenAdi, r.Durum.ToString(), r.Aciklama }).ToList()));

        _log.LogInformation("Müşteri verisi dökümü alındı: müşteri {Musteri}, işletme {Kod}, isteyen {Kullanici}",
            id, _isletme.Zorunlu.Kod, User.FindFirstValue(TokenServisi.KimlikTalebi));
        return File(dosya, ExcelYazici.IcerikTuru, $"Musteri-verisi-{Zaman.Tarih(DateTime.UtcNow)}.xlsx");
    }

    // Müşterinin kişisel verisini geri dönüşsüz siler; servis kayıtları anonimleştirilerek kalır.
    [HttpDelete("musteriler/{id:oid}")]
    public async Task<IActionResult> KaliciSil(string id, CancellationToken iptal)
    {
        if (!await _db.Musteriler.Find(m => m.Id == id).AnyAsync(iptal)) return Bulunamadi();

        if (await KisiselVeriSilme.EngeliBul(_db, id, _isletme.Zorunlu.SaklamaYili, DateTime.UtcNow, iptal) is { } engel)
            return Problem(detail: engel, statusCode: StatusCodes.Status409Conflict);

        // Silme yarıda kesilmesin diye isteğin iptali dikkate alınmaz.
        await KisiselVeriSilme.SilAsync(_db, id, CancellationToken.None);

        _log.LogWarning("Müşterinin kişisel verileri kalıcı olarak silindi: müşteri {Musteri}, işletme {Kod}, silen {Kullanici}",
            id, _isletme.Zorunlu.Kod, User.FindFirstValue(TokenServisi.KimlikTalebi));
        return NoContent();
    }

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Müşteri bulunamadı.", statusCode: StatusCodes.Status404NotFound);
}
