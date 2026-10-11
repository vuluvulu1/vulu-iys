namespace Vulu.Api.Kimlik;

public class JwtAyarlari
{
    public const string Bolum = "Jwt";
    public const int EnAzAnahtarUzunlugu = 32;

    public string Anahtar { get; set; } = string.Empty;
    public string Yayinci { get; set; } = "vulu-iys";
    public string Hedef { get; set; } = "vulu-iys-istemci";
    public int ErisimDakika { get; set; } = 15;
    public int YenilemeGun { get; set; } = 7;
}

// Veritabanında hiç kullanıcı yokken oluşturulacak ilk yönetici hesabı.
public class IlkAdminAyarlari
{
    public const string Bolum = "IlkAdmin";

    public string? KullaniciAdi { get; set; }
    public string? Sifre { get; set; }
}
