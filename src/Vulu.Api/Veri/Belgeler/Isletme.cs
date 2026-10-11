using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Vulu.Api.Veri.Belgeler;

// Hizmeti kullanan bir işletme. Platform veritabanında durur; işletmenin kendi verisi (personel, müşteriler,
// servis kayıtları, randevular) VeritabaniAdi ile gösterilen ayrı veritabanındadır.
public class Isletme
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    // Giriş ekranında yazılan kısa ad; açıldıktan sonra değişmez.
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    public string? Adres { get; set; }
    public string? Telefon { get; set; }
    public string? Eposta { get; set; }
    public string? VergiDairesi { get; set; }
    public string? VergiNo { get; set; }

    // Fatura tutarı KDV dahil girilir; belge bu orana göre matrah ve KDV'yi ayırır.
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal KdvOrani { get; set; } = 20;

    // Servis kabul formunun altına yazılan koşullar; boşsa varsayılan metin kullanılır.
    public string? ServisKosullari { get; set; }

    public byte[]? Logo { get; set; }
    public string? LogoTuru { get; set; }

    // Askıya alınan işletmenin personeli giriş yapamaz ve açık oturumları reddedilir.
    public bool AktifMi { get; set; } = true;
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    public string VeritabaniAdi { get; set; } = string.Empty;
}

// Vulu yöneticisi: işletme kaydı açar ve askıya alır. Hiçbir işletmenin personeli değildir.
public class PlatformYoneticisi
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string KullaniciAdi { get; set; } = string.Empty;
    public string SifreHash { get; set; } = string.Empty;
    public string AdSoyad { get; set; } = string.Empty;
    public bool AktifMi { get; set; } = true;
}
