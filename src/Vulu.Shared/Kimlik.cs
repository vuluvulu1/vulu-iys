using System.ComponentModel.DataAnnotations;

namespace Vulu.Shared;

// Uzunluk sınırları kural değil korumadır: şifre doğrulaması pahalı bir işlemdir, çok uzun girdiyle yorulmasın.
public record GirisIstegi(
    [Required(ErrorMessage = "İşletme kodu gerekli.")]
    [StringLength(KullaniciKurallari.EnFazlaGirdiUzunlugu, ErrorMessage = "İşletme kodu çok uzun.")]
    string IsletmeKodu,
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")]
    [StringLength(KullaniciKurallari.EnFazlaGirdiUzunlugu, ErrorMessage = "Kullanıcı adı çok uzun.")]
    string KullaniciAdi,
    [Required(ErrorMessage = "Şifre gerekli.")]
    [StringLength(KullaniciKurallari.EnFazlaGirdiUzunlugu, ErrorMessage = "Şifre çok uzun.")]
    string Sifre)
{
    public override string ToString() => GizliIcerik.Ad(this);
}

public record YenilemeIstegi(
    [Required(ErrorMessage = "Yenileme tokeni gerekli.")]
    [StringLength(KullaniciKurallari.EnFazlaGirdiUzunlugu, ErrorMessage = "Yenileme tokeni geçersiz.")]
    string YenilemeTokeni)
{
    public override string ToString() => GizliIcerik.Ad(this);
}

// Oturumdaki kişinin kendi şifresini değiştirmesi; mevcut şifre, oturumu ele geçiren birinin şifreyi
// değiştirip hesabı sahiplenmesini önler.
public record SifreDegistirmeIstegi(
    [Required(ErrorMessage = "Mevcut şifre gerekli.")]
    [StringLength(KullaniciKurallari.EnFazlaGirdiUzunlugu, ErrorMessage = "Mevcut şifre çok uzun.")]
    string MevcutSifre,
    [Required(ErrorMessage = "Yeni şifre gerekli.")]
    [Sifre]
    string YeniSifre)
{
    public override string ToString() => GizliIcerik.Ad(this);
}

// Isletme boşsa oturum bir işletme personeline değil vulu yöneticisine aittir.
public record OturumYaniti(
    string ErisimTokeni,
    DateTime ErisimBitis,
    string YenilemeTokeni,
    KullaniciOzeti Kullanici,
    IsletmeOzeti? Isletme)
{
    public override string ToString() => GizliIcerik.Ad(this);
}

// Personel kaydı: sisteme giriş yapan hesap ile işlerin atandığı kişi aynı kayıttır.
public record KullaniciOzeti(
    string Id,
    string KullaniciAdi,
    KullaniciRol Rol,
    bool AktifMi,
    string AdSoyad,
    string? Telefon,
    string? Uzmanlik,
    // Stok kartlarını ve stok girişlerini yönetebilir. Yöneticiler bu işaret olmadan da yönetir.
    bool StokYetkisi = false);

// Servis kaydı ve randevu formlarındaki "teknisyen" seçimi için; yönetici olmayanlar da görebilir.
public record PersonelOzeti(string Id, string AdSoyad, string? Uzmanlik);

public record KullaniciOlusturIstegi(
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-32 karakter olmalı.")]
    [RegularExpression(KullaniciKurallari.AdDeseni, ErrorMessage = KullaniciKurallari.AdDeseniMesaji)]
    string KullaniciAdi,
    [Required(ErrorMessage = "Şifre gerekli.")]
    [Sifre]
    string Sifre,
    KullaniciRol Rol,
    [Required(ErrorMessage = "Ad soyad gerekli.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
    string AdSoyad,
    [RegularExpression(TelefonKurallari.Desen, ErrorMessage = TelefonKurallari.Mesaj)]
    string? Telefon = null,
    [StringLength(100, ErrorMessage = "Uzmanlık en fazla 100 karakter olabilir.")]
    string? Uzmanlik = null,
    bool StokYetkisi = false)
{
    public override string ToString() => GizliIcerik.Ad(this);
}

public record KullaniciGuncelleIstegi(
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-32 karakter olmalı.")]
    [RegularExpression(KullaniciKurallari.AdDeseni, ErrorMessage = KullaniciKurallari.AdDeseniMesaji)]
    string KullaniciAdi,
    KullaniciRol Rol,
    bool AktifMi,
    [Sifre]
    string? YeniSifre,
    [Required(ErrorMessage = "Ad soyad gerekli.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
    string AdSoyad,
    [RegularExpression(TelefonKurallari.Desen, ErrorMessage = TelefonKurallari.Mesaj)]
    string? Telefon = null,
    [StringLength(100, ErrorMessage = "Uzmanlık en fazla 100 karakter olabilir.")]
    string? Uzmanlik = null,
    bool StokYetkisi = false)
{
    public override string ToString() => GizliIcerik.Ad(this);
}

public static class KullaniciKurallari
{
    public const int EnAzSifreUzunlugu = 8;

    // bcrypt şifrenin yalnızca ilk 72 baytını kullanır; daha uzun şifrenin sonu sessizce yok sayılırdı.
    public const int EnFazlaSifreBayti = 72;
    public const string SifreMesaji = "Şifre en az 8 karakter olmalı ve 72 baytı geçmemeli.";

    // Giriş ekranından gelen alanların üst sınırı.
    public const int EnFazlaGirdiUzunlugu = 128;
    public const string AdDeseni = "^[a-zA-Z0-9._-]+$";
    public const string AdDeseniMesaji = "Kullanıcı adı yalnızca harf, rakam, nokta, alt çizgi ve tire içerebilir.";
}

public static class KimlikKurallari
{
    // Kayıt kimlikleri 24 haneli onaltılık dizelerdir (MongoDB ObjectId).
    public const string Desen = "^[0-9a-fA-F]{24}$";
}

// Yeni belirlenen şifrelerin kuralı. Boş değer geçerlidir; zorunluluk ayrıca [Required] ile istenir.
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class SifreAttribute : ValidationAttribute
{
    public SifreAttribute() => ErrorMessage = KullaniciKurallari.SifreMesaji;

    public override bool IsValid(object? value) =>
        value is null
        || (value is string sifre
            && sifre.Length >= KullaniciKurallari.EnAzSifreUzunlugu
            && System.Text.Encoding.UTF8.GetByteCount(sifre) <= KullaniciKurallari.EnFazlaSifreBayti);
}

// Şifre ya da token taşıyan türler metne çevrilince içeriklerini yazmaz. Kayıt türlerinin kendiliğinden üretilen
// ToString'i bütün alanları döker; böyle bir nesne günlüğe ya da bir hata mesajına düşerse şifre de düşerdi.
public static class GizliIcerik
{
    public static string Ad(object nesne) => $"{nesne.GetType().Name} {{ gizli }}";
}
