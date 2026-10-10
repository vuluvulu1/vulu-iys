using System.ComponentModel.DataAnnotations;

namespace Vulu.Shared;

public record GirisIstegi(
    [Required(ErrorMessage = "İşletme kodu gerekli.")] string IsletmeKodu,
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")] string KullaniciAdi,
    [Required(ErrorMessage = "Şifre gerekli.")] string Sifre);

public record YenilemeIstegi(
    [Required(ErrorMessage = "Yenileme tokeni gerekli.")] string YenilemeTokeni);

// Isletme boşsa oturum bir işletme personeline değil vulu yöneticisine aittir.
public record OturumYaniti(
    string ErisimTokeni,
    DateTime ErisimBitis,
    string YenilemeTokeni,
    KullaniciOzeti Kullanici,
    IsletmeOzeti? Isletme);

// Personel kaydı: sisteme giriş yapan hesap ile işlerin atandığı kişi aynı kayıttır.
public record KullaniciOzeti(
    string Id,
    string KullaniciAdi,
    KullaniciRol Rol,
    bool AktifMi,
    string AdSoyad,
    string? Telefon,
    string? Uzmanlik);

// Servis kaydı ve randevu formlarındaki "teknisyen" seçimi için; yönetici olmayanlar da görebilir.
public record PersonelOzeti(string Id, string AdSoyad, string? Uzmanlik);

public record KullaniciOlusturIstegi(
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-32 karakter olmalı.")]
    [RegularExpression(KullaniciKurallari.AdDeseni, ErrorMessage = KullaniciKurallari.AdDeseniMesaji)]
    string KullaniciAdi,
    [Required(ErrorMessage = "Şifre gerekli.")]
    [StringLength(128, MinimumLength = KullaniciKurallari.EnAzSifreUzunlugu, ErrorMessage = KullaniciKurallari.SifreMesaji)]
    string Sifre,
    KullaniciRol Rol,
    [Required(ErrorMessage = "Ad soyad gerekli.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
    string AdSoyad,
    [RegularExpression(TelefonKurallari.Desen, ErrorMessage = TelefonKurallari.Mesaj)]
    string? Telefon = null,
    [StringLength(100, ErrorMessage = "Uzmanlık en fazla 100 karakter olabilir.")]
    string? Uzmanlik = null);

public record KullaniciGuncelleIstegi(
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-32 karakter olmalı.")]
    [RegularExpression(KullaniciKurallari.AdDeseni, ErrorMessage = KullaniciKurallari.AdDeseniMesaji)]
    string KullaniciAdi,
    KullaniciRol Rol,
    bool AktifMi,
    [StringLength(128, MinimumLength = KullaniciKurallari.EnAzSifreUzunlugu, ErrorMessage = KullaniciKurallari.SifreMesaji)]
    string? YeniSifre,
    [Required(ErrorMessage = "Ad soyad gerekli.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
    string AdSoyad,
    [RegularExpression(TelefonKurallari.Desen, ErrorMessage = TelefonKurallari.Mesaj)]
    string? Telefon = null,
    [StringLength(100, ErrorMessage = "Uzmanlık en fazla 100 karakter olabilir.")]
    string? Uzmanlik = null);

public static class KullaniciKurallari
{
    public const int EnAzSifreUzunlugu = 8;
    public const string SifreMesaji = "Şifre en az 8 karakter olmalı.";
    public const string AdDeseni = "^[a-zA-Z0-9._-]+$";
    public const string AdDeseniMesaji = "Kullanıcı adı yalnızca harf, rakam, nokta, alt çizgi ve tire içerebilir.";
}

public static class KimlikKurallari
{
    // Kayıt kimlikleri 24 haneli onaltılık dizelerdir (MongoDB ObjectId).
    public const string Desen = "^[0-9a-fA-F]{24}$";
}
