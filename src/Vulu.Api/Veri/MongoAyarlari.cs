namespace Vulu.Api.Veri;

public class MongoAyarlari
{
    public const string Bolum = "Mongo";

    public string BaglantiDizesi { get; set; } = string.Empty;

    // Platform veritabanının adı (işletme listesi, vulu yöneticileri, yenileme tokenları).
    public string VeritabaniAdi { get; set; } = string.Empty;

    // Her işletmenin kendi veritabanı bu önek + işletme koduyla adlandırılır.
    public string IsletmeVeritabaniOneki { get; set; } = "isletme_";
}
