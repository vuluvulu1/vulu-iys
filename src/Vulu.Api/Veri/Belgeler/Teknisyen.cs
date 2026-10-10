using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Vulu.Api.Veri.Belgeler;

public class Teknisyen
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string AdSoyad { get; set; } = string.Empty;
    public string? Telefon { get; set; }
    public string? Uzmanlik { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? KullaniciId { get; set; }

    public DateTime? SilinmeTarihi { get; set; }
}
