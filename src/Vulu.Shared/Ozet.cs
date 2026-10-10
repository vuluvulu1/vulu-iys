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
    IReadOnlyList<ServisKaydiYaniti> AcikKayitlar);
