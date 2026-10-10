using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Vulu.Api.Veri.Belgeler;

// Platform veritabanında durur: yenileme isteği geldiğinde tokenın hangi işletmeye ait olduğu buradan öğrenilir.
public class YenilemeTokeni
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string KullaniciId { get; set; } = null!;

    // Boşsa token bir vulu yöneticisine aittir.
    [BsonRepresentation(BsonType.ObjectId)]
    public string? IsletmeId { get; set; }

    // Tokenin kendisi saklanmaz; veritabanı sızsa bile oturum açılamaz.
    public string TokenHash { get; set; } = string.Empty;

    public DateTime SonGecerlilik { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
}
