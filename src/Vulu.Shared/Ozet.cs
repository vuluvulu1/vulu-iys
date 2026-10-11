namespace Vulu.Shared;

// Ana sayfadaki özet. "Bugün" ve "bu ay" kullanıcının saat dilimine göre değiştiği için sınırlarını istemci gönderir.
public record OzetYaniti(
    long BekleyenKayit,
    long DevamEdenKayit,
    long BugunkuRandevu,
    long OdenmemisFatura,
    decimal OdenmemisTutar,
    decimal BuAyTahsilat,
    IReadOnlyList<RandevuYaniti> YaklasanRandevular,
    IReadOnlyList<ServisKaydiYaniti> AcikKayitlar,
    long OnayBekleyenKayit,
    // İşi bitmiş, müşterinin almasını bekleyen cihazlar.
    long TeslimBekleyenKayit,
    // Stoğu kritik seviyeye inmiş parça sayısı.
    long AzalanParca);

// Seçilen tarih aralığının raporu. Aralığın sınırlarını istemci kendi saat dilimine göre gönderir.
public record RaporYaniti(
    int AcilanKayit,
    int TamamlananKayit,
    int IptalEdilenKayit,
    int GarantiKapsamindaKayit,
    // Kabulden tamamlanmaya geçen ortalama süre; aralıkta tamamlanan iş yoksa boş.
    double? OrtalamaTamirSaati,
    int KesilenFatura,
    decimal KesilenFaturaTutari,
    decimal TahsilEdilen,
    IReadOnlyList<TeknisyenRaporu> Teknisyenler,
    IReadOnlyList<AylikRapor> Aylar,
    IReadOnlyList<ParcaRaporu> Parcalar);

public record TeknisyenRaporu(string Ad, int AcilanKayit, int TamamlananKayit, double? OrtalamaTamirSaati, decimal FaturaTutari);

public record AylikRapor(int Yil, int Ay, int AcilanKayit, int TamamlananKayit, decimal TahsilEdilen);

public record ParcaRaporu(string Ad, int Adet, decimal Tutar);
