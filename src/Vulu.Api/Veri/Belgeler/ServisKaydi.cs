using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Vulu.Shared;

namespace Vulu.Api.Veri.Belgeler;

public class ServisKaydi
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    // İşletme içinde yıl bazında sıralı kayıt numarası (örn. SK-2026-00042); formlarda ve aramada kullanılır.
    public string No { get; set; } = string.Empty;

    // Müşterinin cihaz durumunu sorgularken kullandığı, tahmin edilemeyen kod. Kayıt numarası sıralı olduğu için
    // bu amaçla kullanılamaz.
    public string TakipKodu { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string MusteriId { get; set; } = null!;
    public MusteriOzeti Musteri { get; set; } = new();

    [BsonRepresentation(BsonType.ObjectId)]
    public string? TeknisyenId { get; set; }
    public string? TeknisyenAdi { get; set; }

    public string CihazBilgisi { get; set; } = string.Empty;
    public string Sorun { get; set; } = string.Empty;
    public ServisDurumu Durum { get; set; } = ServisDurumu.Beklemede;

    public string? Marka { get; set; }
    public string? Model { get; set; }

    // Formlarda ve listelerde gösterilen ad: cihaz, marka ve model birlikte.
    [BsonIgnore]
    public string CihazAdi => CihazAdlari.Birlestir(CihazBilgisi, Marka, Model);

    // Seri numarası ya da IMEI; büyük harfle saklanır ki aynı cihazın kayıtları birebir eşleşsin.
    public string? SeriNo { get; set; }
    public bool GarantiKapsaminda { get; set; }
    public string? Aksesuarlar { get; set; }
    public string? FizikselDurum { get; set; }
    public string? YapilanIslem { get; set; }

    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? TamamlanmaTarihi { get; set; }
    public DateTime? TeslimTarihi { get; set; }

    public Teklif? Teklif { get; set; }
    public List<KullanilanParca> Parcalar { get; set; } = [];

    // Fatura servis kaydıyla bire bir olduğu için ayrı koleksiyon yerine buraya gömülür.
    public Fatura? Fatura { get; set; }

    // Kayıt no, cihaz bilgileri ve sorunun arama için küçültülmüş hali; müşteri adı Musteri.AramaMetni üzerinden aranır.
    public string AramaMetni { get; set; } = string.Empty;

    // Her yazmada bir artar. Kayıt, okunduğu sürümle eşleşmiyorsa yazılmaz; böylece iki kişinin aynı anda yaptığı
    // değişiklikler (örn. biri fatura keserken diğerinin durumu değiştirmesi) birbirini sessizce ezmez.
    public int Surum { get; set; }

    public DateTime? SilinmeTarihi { get; set; }
}

// Tamire başlamadan önce müşteriye bildirilen tahmini tutar ve müşterinin yanıtı.
public class Teklif
{
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Tutar { get; set; }

    public string? Aciklama { get; set; }
    public TeklifDurumu Durum { get; set; } = TeklifDurumu.Bekliyor;
    public DateTime Tarih { get; set; } = DateTime.UtcNow;
    public DateTime? YanitTarihi { get; set; }
    public string? YanitNotu { get; set; }
}

// Kayıtta kullanılan parça satırı. Ad ve fiyat kullanıldığı andaki değerlerdir; stok kartı sonradan değişse de
// eski kayıtların tutarı değişmez.
public class KullanilanParca
{
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string ParcaId { get; set; } = null!;

    public string Ad { get; set; } = string.Empty;
    public int Adet { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal BirimFiyat { get; set; }
}

public class Fatura
{
    // Yıl içinde sıralı belge numarası (örn. VL-2026-000042); fatura ilk kesildiğinde verilir ve değişmez.
    public string No { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Tutar { get; set; }

    public bool OdendiMi { get; set; }
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;
    public DateTime? OdemeTarihi { get; set; }
}
