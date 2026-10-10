using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Vulu.Shared;

namespace Vulu.Api.Veri.Belgeler;

public class Randevu
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string MusteriId { get; set; } = null!;
    public MusteriOzeti Musteri { get; set; } = new();

    [BsonRepresentation(BsonType.ObjectId)]
    public string? TeknisyenId { get; set; }
    public string? TeknisyenAdi { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? ServisKaydiId { get; set; }

    public DateTime RandevuTarihi { get; set; }
    public string? Aciklama { get; set; }
    public RandevuDurumu Durum { get; set; } = RandevuDurumu.Planlandi;
}
