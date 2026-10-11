using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Altyapi;

// Listelerin Excel'e aktarılırken hangi sütunlarla yazılacağı. Aynı sayfa birden fazla yerde kullanılır
// (servis kayıtları hem kendi listesinden hem müşteri veri dökümünden dışa aktarılır).
public static class DisaAktarim
{
    // Tek dosyaya yazılan en fazla satır; daha fazlası için liste süzülerek aktarılır.
    public const int EnFazlaSatir = 20_000;

    public static string DosyaAdi(string ad) => $"{ad}-{Zaman.TurkiyeSaati(DateTime.UtcNow):yyyy-MM-dd}.xlsx";

    public static ExcelSayfasi Musteriler(IEnumerable<Musteri> musteriler) => new(
        "Müşteriler",
        ["Ad soyad", "Telefon", "E-posta", "Adres", "Kayıt tarihi"],
        musteriler.Select(m => new object?[] { m.AdSoyad, m.Telefon, m.Email, m.Adres, m.OlusturmaTarihi }).ToList());

    public static ExcelSayfasi ServisKayitlari(IEnumerable<ServisKaydi> kayitlar) => new(
        "Servis kayıtları",
        [
            "Kayıt no", "Açılış", "Müşteri", "Telefon", "Cihaz", "Marka", "Model", "Seri no / IMEI", "Garanti",
            "Sorun", "Yapılan işlem", "Teknisyen", "Durum", "Tamamlanma", "Teslim", "Teklif", "Parça toplamı",
            "Fatura no", "Fatura tutarı", "Ödendi"
        ],
        kayitlar.Select(k => new object?[]
        {
            k.No, k.OlusturmaTarihi, k.Musteri.AdSoyad, k.Musteri.Telefon, k.CihazBilgisi, k.Marka, k.Model, k.SeriNo,
            k.GarantiKapsaminda, k.Sorun, k.YapilanIslem, k.TeknisyenAdi, k.Durum.Ad(), k.TamamlanmaTarihi, k.TeslimTarihi,
            k.Teklif?.Tutar, k.Parcalar.Count == 0 ? null : k.Parcalar.Sum(p => p.Adet * p.BirimFiyat),
            k.Fatura?.No, k.Fatura?.Tutar, k.Fatura?.OdendiMi
        }).ToList());

    public static ExcelSayfasi Parcalar(IEnumerable<Parca> parcalar) => new(
        "Stok",
        ["Parça", "Stok kodu", "Birim fiyat", "Stok adedi", "Kritik seviye"],
        parcalar.Select(p => new object?[] { p.Ad, p.Kod, p.BirimFiyat, p.StokAdedi, p.KritikSeviye }).ToList());

    public static ExcelSayfasi[] Rapor(RaporYaniti rapor, DateTime baslangic, DateTime bitis) =>
    [
        new("Özet", ["Gösterge", "Değer"],
        [
            ["Dönem başlangıcı", baslangic],
            ["Dönem bitişi (hariç)", bitis],
            ["Açılan servis kaydı", rapor.AcilanKayit],
            ["Tamamlanan iş", rapor.TamamlananKayit],
            ["İptal edilen kayıt", rapor.IptalEdilenKayit],
            ["Garanti kapsamındaki iş", rapor.GarantiKapsamindaKayit],
            ["Ortalama tamir süresi (saat)", rapor.OrtalamaTamirSaati],
            ["Kesilen fatura", rapor.KesilenFatura],
            ["Kesilen fatura tutarı", rapor.KesilenFaturaTutari],
            ["Tahsil edilen", rapor.TahsilEdilen]
        ]),
        new("Teknisyenler", ["Teknisyen", "Açılan", "Tamamlanan", "Ortalama süre (saat)", "Fatura tutarı"],
            rapor.Teknisyenler.Select(t => new object?[] { t.Ad, t.AcilanKayit, t.TamamlananKayit, t.OrtalamaTamirSaati, t.FaturaTutari }).ToList()),
        new("Aylar", ["Yıl", "Ay", "Açılan", "Tamamlanan", "Tahsil edilen"],
            rapor.Aylar.Select(a => new object?[] { a.Yil, a.Ay, a.AcilanKayit, a.TamamlananKayit, a.TahsilEdilen }).ToList()),
        new("Parçalar", ["Parça", "Adet", "Tutar"],
            rapor.Parcalar.Select(p => new object?[] { p.Ad, p.Adet, p.Tutar }).ToList())
    ];
}
