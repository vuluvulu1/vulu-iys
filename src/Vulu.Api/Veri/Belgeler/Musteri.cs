using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Vulu.Api.Veri.Belgeler;

public class Musteri
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string AdSoyad { get; set; } = string.Empty;
    public string? Telefon { get; set; }
    public string? Email { get; set; }
    public string? Adres { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    // Ad, telefon ve e-postanın arama için küçültülmüş hali; bkz. Altyapi.AramaMetni.
    public string AramaMetni { get; set; } = string.Empty;

    // Dolu ise kayıt silinmiş sayılır; geçmiş servis kayıtları bozulmasın diye belge tutulur.
    public DateTime? SilinmeTarihi { get; set; }

    public MusteriOzeti OzetiniCikar() => new()
    {
        AdSoyad = AdSoyad,
        Telefon = Telefon,
        AramaMetni = Altyapi.AramaMetni.Olustur(AdSoyad, Telefon)
    };
}

// Servis kaydı ve randevuya gömülen kopya; listeler müşteri koleksiyonuna gitmeden gösterilir ve aranır.
// Müşteri güncellenince kopyalar da yenilenir (bkz. MusterilerController.Guncelle).
public class MusteriOzeti
{
    public string AdSoyad { get; set; } = string.Empty;
    public string? Telefon { get; set; }
    public string AramaMetni { get; set; } = string.Empty;
}
