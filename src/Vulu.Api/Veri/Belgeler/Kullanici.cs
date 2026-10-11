using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Vulu.Shared;

namespace Vulu.Api.Veri.Belgeler;

// Personel kaydı: sisteme giriş yapan hesap ile servis kaydı ve randevuların atandığı kişi aynı belgedir.
public class Kullanici
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    public string KullaniciAdi { get; set; } = string.Empty;
    public string SifreHash { get; set; } = string.Empty;
    public KullaniciRol Rol { get; set; } = KullaniciRol.Teknisyen;
    public bool AktifMi { get; set; } = true;

    // Erişim tokenına da yazılır; şifre, rol ya da aktiflik değişince yenilenir ve eski tokenlar geçersiz kalır
    // (bkz. Altyapi.OturumDogrulayici).
    public string OturumDamgasi { get; set; } = string.Empty;
    public DateTime OlusturmaTarihi { get; set; } = DateTime.UtcNow;

    public string AdSoyad { get; set; } = string.Empty;
    public string? Telefon { get; set; }
    public string? Uzmanlik { get; set; }
}
