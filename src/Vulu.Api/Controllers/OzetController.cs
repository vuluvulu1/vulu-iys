using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kurallar;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/ozet")]
public class OzetController : ControllerBase
{
    private const int ListeBoyutu = 5;

    private readonly MongoBaglami _db;

    public OzetController(MongoBaglami db) => _db = db;

    // gunBaslangici: kullanıcının bugününün ilk anı; ayBaslangici: içinde bulunduğu ayın ilk anı.
    [HttpGet]
    public async Task<ActionResult<OzetYaniti>> Getir(
        [FromQuery] DateTime gunBaslangici, [FromQuery] DateTime ayBaslangici, CancellationToken iptal)
    {
        if (gunBaslangici == default || ayBaslangici == default)
            return Problem(detail: "gunBaslangici ve ayBaslangici gerekli.", statusCode: StatusCodes.Status400BadRequest);

        var gun = Zaman.UtcYap(gunBaslangici);
        var ay = Zaman.UtcYap(ayBaslangici);
        var simdi = DateTime.UtcNow;

        var kayitlar = _db.ServisKayitlari;
        var randevular = _db.Randevular;

        var bekleyen = await kayitlar.CountDocumentsAsync(
            s => s.SilinmeTarihi == null && s.Durum == ServisDurumu.Beklemede, cancellationToken: iptal);
        var devamEden = await kayitlar.CountDocumentsAsync(
            s => s.SilinmeTarihi == null && s.Durum == ServisDurumu.DevamEdiyor, cancellationToken: iptal);

        var onayBekleyen = await kayitlar.CountDocumentsAsync(
            s => s.SilinmeTarihi == null && s.Durum == ServisDurumu.OnayBekliyor, cancellationToken: iptal);
        var teslimBekleyen = await kayitlar.CountDocumentsAsync(
            s => s.SilinmeTarihi == null && s.Durum == ServisDurumu.Tamamlandi, cancellationToken: iptal);
        var azalanParca = await _db.Parcalar.CountDocumentsAsync(
            Builders<Parca>.Filter.Eq(p => p.SilinmeTarihi, null) & ParcalarController.AzalanFiltresi, cancellationToken: iptal);

        var bugunku = await randevular.CountDocumentsAsync(
            r => r.Durum == RandevuDurumu.Planlandi && r.RandevuTarihi >= gun && r.RandevuTarihi < gun.AddDays(1),
            cancellationToken: iptal);

        // Tutarlar az sayıda belgeden toplandığı ve Decimal128 hassasiyeti korunmak istendiği için bellekte toplanır.
        var odenmemisler = await kayitlar
            .Find(s => s.SilinmeTarihi == null && s.Fatura!.OdendiMi == false)
            .Project(s => s.Fatura!.Tutar)
            .ToListAsync(iptal);
        var buAyOdenenler = await kayitlar
            .Find(s => s.SilinmeTarihi == null && s.Fatura!.OdendiMi == true && s.Fatura!.OdemeTarihi >= ay)
            .Project(s => s.Fatura!.Tutar)
            .ToListAsync(iptal);

        var yaklasan = await randevular
            .Find(r => r.Durum == RandevuDurumu.Planlandi && r.RandevuTarihi >= simdi)
            .SortBy(r => r.RandevuTarihi)
            .Limit(ListeBoyutu)
            .ToListAsync(iptal);

        var acik = await kayitlar
            .Find(s => s.SilinmeTarihi == null && ServisKaydiKurallari.AcikDurumlar.Contains(s.Durum))
            .SortBy(s => s.OlusturmaTarihi)
            .Limit(ListeBoyutu)
            .ToListAsync(iptal);

        return new OzetYaniti(
            bekleyen, devamEden, bugunku,
            odenmemisler.Count, odenmemisler.Sum(), buAyOdenenler.Sum(),
            yaklasan.Select(r => r.YanitaCevir()).ToList(),
            acik.Select(s => s.YanitaCevir()).ToList(),
            onayBekleyen, teslimBekleyen, azalanParca);
    }
}
