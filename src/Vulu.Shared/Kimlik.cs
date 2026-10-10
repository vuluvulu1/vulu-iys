using System.ComponentModel.DataAnnotations;

namespace Vulu.Shared;

public record GirisIstegi(
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")] string KullaniciAdi,
    [Required(ErrorMessage = "Şifre gerekli.")] string Sifre);

public record YenilemeIstegi(
    [Required(ErrorMessage = "Yenileme tokeni gerekli.")] string YenilemeTokeni);

public record OturumYaniti(
    string ErisimTokeni,
    DateTime ErisimBitis,
    string YenilemeTokeni,
    KullaniciOzeti Kullanici);

public record KullaniciOzeti(string Id, string KullaniciAdi, KullaniciRol Rol, bool AktifMi);

public record KullaniciOlusturIstegi(
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-32 karakter olmalı.")]
    [RegularExpression(KullaniciKurallari.AdDeseni, ErrorMessage = KullaniciKurallari.AdDeseniMesaji)]
    string KullaniciAdi,
    [Required(ErrorMessage = "Şifre gerekli.")]
    [StringLength(128, MinimumLength = KullaniciKurallari.EnAzSifreUzunlugu, ErrorMessage = KullaniciKurallari.SifreMesaji)]
    string Sifre,
    KullaniciRol Rol);

public record KullaniciGuncelleIstegi(
    [Required(ErrorMessage = "Kullanıcı adı gerekli.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Kullanıcı adı 3-32 karakter olmalı.")]
    [RegularExpression(KullaniciKurallari.AdDeseni, ErrorMessage = KullaniciKurallari.AdDeseniMesaji)]
    string KullaniciAdi,
    KullaniciRol Rol,
    bool AktifMi,
    [StringLength(128, MinimumLength = KullaniciKurallari.EnAzSifreUzunlugu, ErrorMessage = KullaniciKurallari.SifreMesaji)]
    string? YeniSifre);

public static class KullaniciKurallari
{
    public const int EnAzSifreUzunlugu = 8;
    public const string SifreMesaji = "Şifre en az 8 karakter olmalı.";
    public const string AdDeseni = "^[a-zA-Z0-9._-]+$";
    public const string AdDeseniMesaji = "Kullanıcı adı yalnızca harf, rakam, nokta, alt çizgi ve tire içerebilir.";
}
