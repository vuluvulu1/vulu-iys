using System.ComponentModel.DataAnnotations;

namespace Vulu.Shared;

public record TeknisyenIstegi(
    [Required(ErrorMessage = "Ad soyad gerekli.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
    string AdSoyad,
    [Required(ErrorMessage = "Telefon gerekli.")]
    [RegularExpression(TelefonKurallari.Desen, ErrorMessage = TelefonKurallari.Mesaj)]
    string Telefon,
    [StringLength(100, ErrorMessage = "Uzmanlık en fazla 100 karakter olabilir.")]
    string? Uzmanlik,
    // Teknisyenin uygulamaya giriş yaptığı hesap; hesabı yoksa boş bırakılır.
    [RegularExpression(KimlikKurallari.Desen, ErrorMessage = "Geçersiz kullanıcı hesabı.")]
    string? KullaniciId);

public record TeknisyenYaniti(
    string Id,
    string AdSoyad,
    string? Telefon,
    string? Uzmanlik,
    string? KullaniciId,
    string? KullaniciAdi);

public static class KimlikKurallari
{
    // Kayıt kimlikleri 24 haneli onaltılık dizelerdir (MongoDB ObjectId).
    public const string Desen = "^[0-9a-fA-F]{24}$";
}
