using System.ComponentModel.DataAnnotations;

namespace Vulu.Shared;

// Oturum açan personelin işletmesi; yan menüde ve başlıkta gösterilir.
public record IsletmeOzeti(string Id, string Kod, string Ad);

public record IsletmeYaniti(
    string Id,
    string Kod,
    string Ad,
    string? Adres,
    string? Telefon,
    string? Eposta,
    string? VergiDairesi,
    string? VergiNo,
    decimal KdvOrani,
    bool AktifMi,
    DateTime OlusturmaTarihi,
    // Logo varsa base64 kodlu içeriği ve türü (image/png ya da image/jpeg).
    string? LogoVerisi,
    string? LogoTuru,
    // Aynı anda aktif olabilecek personel hesabı sayısı.
    int PersonelSiniri,
    // Aktif personel sayısı; yalnızca vulu yöneticisinin işletme listesinde doldurulur.
    long? PersonelSayisi);

// Vulu yöneticisinin işletme kaydı açarken verdiği bilgiler: işletme ve onun ilk yöneticisi.
public record IsletmeOlusturIstegi(
    [Required(ErrorMessage = "İşletme kodu gerekli.")]
    [RegularExpression(IsletmeKurallari.KodDeseni, ErrorMessage = IsletmeKurallari.KodMesaji)]
    string Kod,
    [Required(ErrorMessage = "İşletme adı gerekli.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "İşletme adı 2-120 karakter olmalı.")]
    string Ad,
    [Required(ErrorMessage = "Yönetici kullanıcı adı gerekli.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-32 karakter olmalı.")]
    [RegularExpression(KullaniciKurallari.AdDeseni, ErrorMessage = KullaniciKurallari.AdDeseniMesaji)]
    string YoneticiKullaniciAdi,
    [Required(ErrorMessage = "Yönetici şifresi gerekli.")]
    [StringLength(128, MinimumLength = KullaniciKurallari.EnAzSifreUzunlugu, ErrorMessage = KullaniciKurallari.SifreMesaji)]
    string YoneticiSifre,
    [Required(ErrorMessage = "Yönetici ad soyadı gerekli.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
    string YoneticiAdSoyad,
    [Range(IsletmeKurallari.EnAzPersonelSiniri, IsletmeKurallari.EnFazlaPersonelSiniri, ErrorMessage = IsletmeKurallari.PersonelSiniriMesaji)]
    int PersonelSiniri = IsletmeKurallari.VarsayilanPersonelSiniri);

// Vulu yöneticisinin sonradan değiştirebildikleri: ad, açık/askıda durumu ve personel sınırı.
public record IsletmeDurumIstegi(
    [Required(ErrorMessage = "İşletme adı gerekli.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "İşletme adı 2-120 karakter olmalı.")]
    string Ad,
    bool AktifMi,
    [Range(IsletmeKurallari.EnAzPersonelSiniri, IsletmeKurallari.EnFazlaPersonelSiniri, ErrorMessage = IsletmeKurallari.PersonelSiniriMesaji)]
    int PersonelSiniri);

// Vulu yöneticisinin şifre sıfırlamak için gördüğü, bir işletmenin yönetici hesapları.
public record IsletmeYoneticisiOzeti(string Id, string KullaniciAdi, string AdSoyad, bool AktifMi);

public record SifreSifirlamaIstegi(
    [Required(ErrorMessage = "Yeni şifre gerekli.")]
    [StringLength(128, MinimumLength = KullaniciKurallari.EnAzSifreUzunlugu, ErrorMessage = KullaniciKurallari.SifreMesaji)]
    string YeniSifre);

// İşletme yöneticisinin kendi işletmesi için düzenlediği bilgiler.
public record IsletmeProfilIstegi(
    [Required(ErrorMessage = "İşletme adı gerekli.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "İşletme adı 2-120 karakter olmalı.")]
    string Ad,
    [StringLength(500, ErrorMessage = "Adres en fazla 500 karakter olabilir.")]
    string? Adres,
    [RegularExpression(TelefonKurallari.Desen, ErrorMessage = TelefonKurallari.Mesaj)]
    string? Telefon,
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(200, ErrorMessage = "E-posta en fazla 200 karakter olabilir.")]
    string? Eposta,
    [StringLength(100, ErrorMessage = "Vergi dairesi en fazla 100 karakter olabilir.")]
    string? VergiDairesi,
    [RegularExpression(@"^\d{10,11}$", ErrorMessage = "Vergi numarası 10 ya da 11 haneli olmalı.")]
    string? VergiNo,
    [Range(0, 100, ErrorMessage = "KDV oranı 0 ile 100 arasında olmalı.")]
    decimal KdvOrani,
    // Yeni logo yüklenecekse dolu gelir; boşsa mevcut logo korunur.
    string? LogoVerisi,
    string? LogoTuru,
    bool LogoyuKaldir);

// Vulu yöneticisi hesapları (işletme kaydı açan kişiler).
public record VuluYoneticisiYaniti(string Id, string KullaniciAdi, string AdSoyad, bool AktifMi);

public record VuluYoneticisiOlusturIstegi(
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-32 karakter olmalı.")]
    [RegularExpression(KullaniciKurallari.AdDeseni, ErrorMessage = KullaniciKurallari.AdDeseniMesaji)]
    string KullaniciAdi,
    [Required(ErrorMessage = "Şifre gerekli.")]
    [StringLength(128, MinimumLength = KullaniciKurallari.EnAzSifreUzunlugu, ErrorMessage = KullaniciKurallari.SifreMesaji)]
    string Sifre,
    [Required(ErrorMessage = "Ad soyad gerekli.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
    string AdSoyad);

public record VuluYoneticisiGuncelleIstegi(
    [Required(ErrorMessage = "Ad soyad gerekli.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
    string AdSoyad,
    bool AktifMi,
    [StringLength(128, MinimumLength = KullaniciKurallari.EnAzSifreUzunlugu, ErrorMessage = KullaniciKurallari.SifreMesaji)]
    string? YeniSifre);

public static class IsletmeKurallari
{
    // Giriş ekranında yazılan kısa ad; adreslerde ve veritabanı adında kullanıldığı için dar bir alfabeyle sınırlıdır.
    public const string KodDeseni = "^[a-z0-9][a-z0-9-]{1,28}[a-z0-9]$";
    public const string KodMesaji = "İşletme kodu 3-30 karakter olmalı; yalnızca küçük harf, rakam ve tire içerebilir.";

    // Bu kodla giriş yapanlar bir işletmenin personeli değil, vulu yöneticisidir.
    public const string PlatformKodu = "vulu";

    public const int EnFazlaLogoBayt = 256 * 1024;
    public static readonly string[] LogoTurleri = ["image/png", "image/jpeg"];

    public const int EnAzPersonelSiniri = 1;
    public const int EnFazlaPersonelSiniri = 1000;
    public const int VarsayilanPersonelSiniri = 5;
    public const string PersonelSiniriMesaji = "Personel sınırı 1 ile 1000 arasında olmalı.";
}
