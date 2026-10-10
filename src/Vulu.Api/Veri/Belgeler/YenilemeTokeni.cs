using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Vulu.Api.Veri.Belgeler;

public class YenilemeTokeni
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string KullaniciId { get; set; } = null!;

    // Tokenin kendisi saklanmaz; veritabanı sızsa bile oturum açılamaz.
    public string TokenHash { get; set; } = string.Empty;

    public DateTime SonGecerlilik { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
}
