using System.ComponentModel.DataAnnotations;

namespace Vulu.Shared;

// Yeni randevu her zaman "Planlandı" açılır; durum ayrı bir istekle değiştirilir. Tarih UTC gönderilir.
public record RandevuIstegi(
    [Required(ErrorMessage = "Müşteri seçilmeli.")]
    [RegularExpression(KimlikKurallari.Desen, ErrorMessage = "Geçersiz müşteri.")]
    string MusteriId,
    [RegularExpression(KimlikKurallari.Desen, ErrorMessage = "Geçersiz teknisyen.")]
    string? TeknisyenId,
    [RegularExpression(KimlikKurallari.Desen, ErrorMessage = "Geçersiz servis kaydı.")]
    string? ServisKaydiId,
    DateTime RandevuTarihi,
    [Range(RandevuKurallari.EnAzSure, RandevuKurallari.EnFazlaSure, ErrorMessage = "Süre 15 dakika ile 8 saat arasında olmalı.")]
    int SureDakika,
    [StringLength(500, ErrorMessage = "Açıklama en fazla 500 karakter olabilir.")]
    string? Aciklama);

public record RandevuDurumuIstegi(RandevuDurumu Durum);

public record RandevuYaniti(
    string Id,
    string MusteriId,
    string MusteriAdi,
    string? MusteriTelefonu,
    string? TeknisyenId,
    string? TeknisyenAdi,
    string? ServisKaydiId,
    DateTime RandevuTarihi,
    int SureDakika,
    string? Aciklama,
    RandevuDurumu Durum);

public static class RandevuKurallari
{
    public const int EnAzSure = 15;
    public const int EnFazlaSure = 480;
    public const int VarsayilanSure = 60;
}
