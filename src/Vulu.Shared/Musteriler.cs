using System.ComponentModel.DataAnnotations;

namespace Vulu.Shared;

public record MusteriIstegi(
    [Required(ErrorMessage = "Ad soyad gerekli.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ad soyad 2-100 karakter olmalı.")]
    string AdSoyad,
    [Required(ErrorMessage = "Telefon gerekli.")]
    [RegularExpression(TelefonKurallari.Desen, ErrorMessage = TelefonKurallari.Mesaj)]
    string Telefon,
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(200, ErrorMessage = "E-posta en fazla 200 karakter olabilir.")]
    string? Email,
    [StringLength(500, ErrorMessage = "Adres en fazla 500 karakter olabilir.")]
    string? Adres);

public record MusteriYaniti(
    string Id,
    string AdSoyad,
    string? Telefon,
    string? Email,
    string? Adres,
    DateTime OlusturmaTarihi);

public static class TelefonKurallari
{
    public const string Desen = @"^0\d{10}$";
    public const string Mesaj = "Telefon 0 ile başlayan 11 haneli bir numara olmalı (örn. 05551234567).";
}
