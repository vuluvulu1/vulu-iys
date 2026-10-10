using System.ComponentModel.DataAnnotations;

namespace Vulu.Shared;

// Yeni kayıt her zaman "Beklemede" açılır; durum ayrı bir istekle değiştirilir.
public record ServisKaydiIstegi(
    [Required(ErrorMessage = "Müşteri seçilmeli.")]
    [RegularExpression(KimlikKurallari.Desen, ErrorMessage = "Geçersiz müşteri.")]
    string MusteriId,
    [RegularExpression(KimlikKurallari.Desen, ErrorMessage = "Geçersiz teknisyen.")]
    string? TeknisyenId,
    [Required(ErrorMessage = "Cihaz bilgisi gerekli.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Cihaz bilgisi 2-200 karakter olmalı.")]
    string CihazBilgisi,
    [Required(ErrorMessage = "Sorun açıklaması gerekli.")]
    [StringLength(1000, MinimumLength = 2, ErrorMessage = "Sorun açıklaması 2-1000 karakter olmalı.")]
    string Sorun);

public record ServisDurumuIstegi(ServisDurumu Durum);

public record FaturaIstegi(
    [Range(typeof(decimal), FaturaKurallari.EnAzTutar, FaturaKurallari.EnFazlaTutar,
        ParseLimitsInInvariantCulture = true, ErrorMessage = FaturaKurallari.TutarMesaji)]
    decimal Tutar,
    bool OdendiMi);

public record FaturaYaniti(decimal Tutar, bool OdendiMi, DateTime OlusturmaTarihi, DateTime? OdemeTarihi);

public record ServisKaydiYaniti(
    string Id,
    string MusteriId,
    string MusteriAdi,
    string? MusteriTelefonu,
    string? TeknisyenId,
    string? TeknisyenAdi,
    string CihazBilgisi,
    string Sorun,
    ServisDurumu Durum,
    DateTime OlusturmaTarihi,
    DateTime? TamamlanmaTarihi,
    FaturaYaniti? Fatura);

// Liste süzgeci: kayıtları fatura durumuna göre daraltır.
public enum FaturaSuzgeci
{
    Hepsi,
    Faturali,
    Odenmemis,
    Odenmis
}

public static class FaturaKurallari
{
    public const string EnAzTutar = "0.01";
    public const string EnFazlaTutar = "10000000";
    public const string TutarMesaji = "Tutar 0,01 ile 10.000.000 arasında olmalı.";
}
