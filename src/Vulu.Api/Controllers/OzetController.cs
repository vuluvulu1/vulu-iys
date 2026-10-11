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
        if (!Zaman.MakulMu(gunBaslangici) || !Zaman.MakulMu(ayBaslangici))
            return Problem(detail: $"gunBaslangici ve ayBaslangici gerekli. {Zaman.MakulDegilMesaji}",
                statusCode: StatusCodes.Status400BadRequest);

        var gun = Zaman.UtcYap(gunBaslangici);
        var ay = Zaman.UtcYap(ayBaslangici);
        var simdi = DateTime.UtcNow;

        var kayitlar = _db.ServisKayitlari;
        var randevular = _db.Randevular;

        // Sorgular birbirinden bağımsızdır; sırayla beklemek yerine hepsi birlikte gönderilir. API ile veritabanı
        // arasındaki her gidiş dönüş zaman aldığı için bu, yanıt süresini sorgu sayısı kadar kısaltır.
        var bekleyen = kayitlar.CountDocumentsAsync(
            s => s.SilinmeTarihi == null && s.Durum == ServisDurumu.Beklemede, cancellationToken: iptal);
        var devamEden = kayitlar.CountDocumentsAsync(
            s => s.SilinmeTarihi == null && s.Durum == ServisDurumu.DevamEdiyor, cancellationToken: iptal);

        var onayBekleyen = kayitlar.CountDocumentsAsync(
            s => s.SilinmeTarihi == null && s.Durum == ServisDurumu.OnayBekliyor, cancellationToken: iptal);
        var teslimBekleyen = kayitlar.CountDocumentsAsync(
            s => s.SilinmeTarihi == null && s.Durum == ServisDurumu.Tamamlandi, cancellationToken: iptal);
        var azalanParca = _db.Parcalar.CountDocumentsAsync(
            Builders<Parca>.Filter.Eq(p => p.SilinmeTarihi, null) & ParcalarController.AzalanFiltresi, cancellationToken: iptal);

        var bugunku = randevular.CountDocumentsAsync(
            r => r.Durum == RandevuDurumu.Planlandi && r.RandevuTarihi >= gun && r.RandevuTarihi < gun.AddDays(1),
            cancellationToken: iptal);

        // Tutarlar az sayıda belgeden toplandığı ve Decimal128 hassasiyeti korunmak istendiği için bellekte toplanır.
        var odenmemisler = kayitlar
            .Find(s => s.SilinmeTarihi == null && s.Fatura!.OdendiMi == false)
            .Project(s => s.Fatura!.Tutar)
            .ToListAsync(iptal);
        var buAyOdenenler = kayitlar
            .Find(s => s.SilinmeTarihi == null && s.Fatura!.OdendiMi == true && s.Fatura!.OdemeTarihi >= ay)
            .Project(s => s.Fatura!.Tutar)
            .ToListAsync(iptal);

        var yaklasan = randevular
            .Find(r => r.Durum == RandevuDurumu.Planlandi && r.RandevuTarihi >= simdi)
            .SortBy(r => r.RandevuTarihi)
            .Limit(ListeBoyutu)
            .ToListAsync(iptal);

        var acik = kayitlar
            .Find(s => s.SilinmeTarihi == null && ServisKaydiKurallari.AcikDurumlar.Contains(s.Durum))
            .SortBy(s => s.OlusturmaTarihi)
            .Limit(ListeBoyutu)
            .ToListAsync(iptal);

        await Task.WhenAll(bekleyen, devamEden, onayBekleyen, teslimBekleyen, azalanParca,
            bugunku, odenmemisler, buAyOdenenler, yaklasan, acik);

        return new OzetYaniti(
            bekleyen.Result, devamEden.Result, bugunku.Result,
            odenmemisler.Result.Count, odenmemisler.Result.Sum(), buAyOdenenler.Result.Sum(),
            yaklasan.Result.Select(r => r.YanitaCevir()).ToList(),
            acik.Result.Select(s => s.YanitaCevir()).ToList(),
            onayBekleyen.Result, teslimBekleyen.Result, azalanParca.Result);
    }
}
