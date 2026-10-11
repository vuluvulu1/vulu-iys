using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

// İşletme yöneticisinin dönem raporu: iş sayıları, tamir süresi, gelir, teknisyen ve parça dökümü.
[ApiController]
[Route("api/raporlar")]
[Authorize(Roles = nameof(KullaniciRol.Admin))]
public class RaporlarController : ControllerBase
{
    private const int EnFazlaGun = 366 * 2;
    private const int ParcaListesiBoyutu = 10;
    private const string Atanmamis = "Atanmamış";

    private readonly MongoBaglami _db;

    public RaporlarController(MongoBaglami db) => _db = db;

    // baslangic dahil, bitis hariç.
    [HttpGet]
    public async Task<ActionResult<RaporYaniti>> Getir(
        [FromQuery] DateTime baslangic, [FromQuery] DateTime bitis, CancellationToken iptal)
    {
        if (!Zaman.MakulMu(baslangic) || !Zaman.MakulMu(bitis))
            return Problem(detail: $"baslangic ve bitis gerekli. {Zaman.MakulDegilMesaji}",
                statusCode: StatusCodes.Status400BadRequest);

        var ilk = Zaman.UtcYap(baslangic);
        var son = Zaman.UtcYap(bitis);
        if (son <= ilk || (son - ilk).TotalDays > EnFazlaGun)
            return Problem(detail: "Tarih aralığı geçersiz (en fazla iki yıl).", statusCode: StatusCodes.Status400BadRequest);

        // Aralığa herhangi bir tarihiyle giren kayıtlar okunur ve sayımlar bellekte yapılır: bir işletmenin iki
        // yıllık kaydı küçüktür ve tutarların Decimal128 hassasiyeti korunur.
        var kayitlar = await _db.ServisKayitlari
            .Find(s => s.SilinmeTarihi == null
                && ((s.OlusturmaTarihi >= ilk && s.OlusturmaTarihi < son)
                    || (s.TamamlanmaTarihi >= ilk && s.TamamlanmaTarihi < son)
                    || (s.Fatura!.OlusturmaTarihi >= ilk && s.Fatura!.OlusturmaTarihi < son)
                    || (s.Fatura!.OdemeTarihi >= ilk && s.Fatura!.OdemeTarihi < son)))
            .ToListAsync(iptal);

        bool Icinde(DateTime? tarih) => tarih >= ilk && tarih < son;

        var acilanlar = kayitlar.Where(k => Icinde(k.OlusturmaTarihi)).ToList();
        var tamamlananlar = kayitlar.Where(k => Icinde(k.TamamlanmaTarihi)).ToList();
        var faturalananlar = kayitlar.Where(k => Icinde(k.Fatura?.OlusturmaTarihi)).ToList();
        var odenenler = kayitlar.Where(k => k.Fatura is { OdendiMi: true } && Icinde(k.Fatura.OdemeTarihi)).ToList();

        var teknisyenler = kayitlar
            .GroupBy(k => k.TeknisyenAdi ?? Atanmamis)
            .Select(grup => new TeknisyenRaporu(
                grup.Key,
                grup.Count(k => Icinde(k.OlusturmaTarihi)),
                grup.Count(k => Icinde(k.TamamlanmaTarihi)),
                OrtalamaSaat(grup.Where(k => Icinde(k.TamamlanmaTarihi))),
                grup.Where(k => Icinde(k.Fatura?.OlusturmaTarihi)).Sum(k => k.Fatura!.Tutar)))
            .Where(t => t.AcilanKayit > 0 || t.TamamlananKayit > 0 || t.FaturaTutari > 0)
            .OrderByDescending(t => t.TamamlananKayit).ThenBy(t => t.Ad)
            .ToList();

        // Aylar Türkiye saatine göre ayrılır; sunucunun saat dilimi sonucu değiştirmez.
        static (int Yil, int Ay) AyAnahtari(DateTime utc)
        {
            var yerel = Zaman.TurkiyeSaati(utc);
            return (yerel.Year, yerel.Month);
        }

        var aylar = acilanlar.Select(k => AyAnahtari(k.OlusturmaTarihi))
            .Concat(tamamlananlar.Select(k => AyAnahtari(k.TamamlanmaTarihi!.Value)))
            .Concat(odenenler.Select(k => AyAnahtari(k.Fatura!.OdemeTarihi!.Value)))
            .Distinct()
            .OrderBy(a => a)
            .Select(a => new AylikRapor(a.Yil, a.Ay,
                acilanlar.Count(k => AyAnahtari(k.OlusturmaTarihi) == a),
                tamamlananlar.Count(k => AyAnahtari(k.TamamlanmaTarihi!.Value) == a),
                odenenler.Where(k => AyAnahtari(k.Fatura!.OdemeTarihi!.Value) == a).Sum(k => k.Fatura!.Tutar)))
            .ToList();

        var parcalar = tamamlananlar
            .SelectMany(k => k.Parcalar)
            .GroupBy(p => p.Ad)
            .Select(grup => new ParcaRaporu(grup.Key, grup.Sum(p => p.Adet), grup.Sum(p => p.Adet * p.BirimFiyat)))
            .OrderByDescending(p => p.Adet).ThenBy(p => p.Ad)
            .Take(ParcaListesiBoyutu)
            .ToList();

        return new RaporYaniti(
            acilanlar.Count,
            tamamlananlar.Count,
            acilanlar.Count(k => k.Durum == ServisDurumu.IptalEdildi),
            acilanlar.Count(k => k.GarantiKapsaminda),
            OrtalamaSaat(tamamlananlar),
            faturalananlar.Count,
            faturalananlar.Sum(k => k.Fatura!.Tutar),
            odenenler.Sum(k => k.Fatura!.Tutar),
            teknisyenler, aylar, parcalar);
    }

    private static double? OrtalamaSaat(IEnumerable<ServisKaydi> tamamlananlar)
    {
        var sureler = tamamlananlar
            .Where(k => k.TamamlanmaTarihi is not null)
            .Select(k => (k.TamamlanmaTarihi!.Value - k.OlusturmaTarihi).TotalHours)
            .ToList();
        return sureler.Count == 0 ? null : Math.Round(sureler.Average(), 2);
    }
}
