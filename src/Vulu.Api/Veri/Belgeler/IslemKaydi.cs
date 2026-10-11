using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Vulu.Shared;

namespace Vulu.Api.Veri.Belgeler;

// Servis kaydının işlem geçmişindeki bir satır: kim, ne zaman, ne yaptı. Yalnızca eklenir; düzenlenmez ve
// silinmez. Kaydın içine gömülmez, çünkü kayıt her listelemede okunur ve geçmiş sınırsız büyüyebilir.
public class IslemKaydi
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string ServisKaydiId { get; set; } = null!;

    public DateTime Tarih { get; set; } = DateTime.UtcNow;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? KullaniciId { get; set; }

    // İşlemi yapanın o andaki adı; hesap sonradan silinse ya da adı değişse de geçmiş okunabilir kalır.
    public string KullaniciAdi { get; set; } = string.Empty;

    public IslemTuru Tur { get; set; }
    public string Aciklama { get; set; } = string.Empty;
}
