using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Vulu.Api.Veri.Belgeler;

// Stok kartı: işletmenin elinde tuttuğu yedek parça.
public class Parca
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string Ad { get; set; } = string.Empty;
    public string? Kod { get; set; }

    // Müşteriye yansıtılan, KDV dahil satış fiyatı.
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal BirimFiyat { get; set; }

    // Yalnızca atomik artırma/azaltma ile değişir (bkz. StokIslemleri); kart düzenlenirken dokunulmaz.
    public int StokAdedi { get; set; }
    public int KritikSeviye { get; set; }

    public string AramaMetni { get; set; } = string.Empty;
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    // Geçmiş servis kayıtları parçaya bağlı kaldığı için belge silinmez, işaretlenir.
    public DateTime? SilinmeTarihi { get; set; }
}

// Stoktaki her değişikliğin izi: giriş, düzeltme ya da bir servis kaydında kullanım. Yalnızca eklenir.
public class StokHareketi
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonRepresentation(BsonType.ObjectId)]
    public string ParcaId { get; set; } = null!;

    public DateTime Tarih { get; set; } = DateTime.UtcNow;

    // Artı: stoğa giren, eksi: stoktan çıkan adet.
    public int Adet { get; set; }
    public string? Aciklama { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? KullaniciId { get; set; }
    public string KullaniciAdi { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? ServisKaydiId { get; set; }
    public string? ServisKaydiNo { get; set; }
}
