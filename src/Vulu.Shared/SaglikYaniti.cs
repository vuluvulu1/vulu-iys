namespace Vulu.Shared;

public record SaglikYaniti(bool VeritabaniErisilebilir, string? Hata, int SozlesmeSurumu);

public static class ApiSozlesmesi
{
    // API ile masaüstü arasındaki istek/yanıt biçimi geriye uyumsuz değiştiğinde artırılır.
    // Masaüstü, sunucudaki sürüm kendisininkinden farklıysa girişe izin vermez.
    // 2: teknisyenler kullanıcı hesaplarıyla birleşti (api/teknisyenler kalktı, api/personel geldi).
    // 3: çok işletmeli yapı (girişte işletme kodu, oturumda işletme bilgisi).
    // 4: cihaz ayrıntıları, yeni durumlar (onay bekliyor, teslim edildi), teklif, parça/stok, işlem geçmişi.
    public const int Surum = 4;
}
