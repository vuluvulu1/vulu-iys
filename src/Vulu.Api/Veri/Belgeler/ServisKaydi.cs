using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Vulu.Shared;

namespace Vulu.Api.Veri.Belgeler;

public class ServisKaydi
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

    public string CihazBilgisi { get; set; } = string.Empty;
    public string Sorun { get; set; } = string.Empty;
    public ServisDurumu Durum { get; set; } = ServisDurumu.Beklemede;

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? TamamlanmaTarihi { get; set; }

    // Fatura servis kaydıyla bire bir olduğu için ayrı koleksiyon yerine buraya gömülür.
    public Fatura? Fatura { get; set; }

    // Cihaz ve sorunun arama için küçültülmüş hali; müşteri adı Musteri.AramaMetni üzerinden aranır.
    public string AramaMetni { get; set; } = string.Empty;

    // Her yazmada bir artar. Kayıt, okunduğu sürümle eşleşmiyorsa yazılmaz; böylece iki kişinin aynı anda yaptığı
    // değişiklikler (örn. biri fatura keserken diğerinin durumu değiştirmesi) birbirini sessizce ezmez.
    public int Surum { get; set; }

    public DateTime? SilinmeTarihi { get; set; }
}

public class Fatura
{
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Tutar { get; set; }

    public bool OdendiMi { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? OdemeTarihi { get; set; }
}
