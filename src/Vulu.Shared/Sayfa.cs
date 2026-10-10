namespace Vulu.Shared;

// Sayfalanmış liste yanıtı. SayfaNo 1'den başlar.
public record Sayfa<T>(IReadOnlyList<T> Ogeler, long Toplam, int SayfaNo, int Boyut);
