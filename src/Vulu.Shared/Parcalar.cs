using System.ComponentModel.DataAnnotations;

namespace Vulu.Shared;

// Stok kartı. Birim fiyat müşteriye yansıtılan, KDV dahil satış fiyatıdır.
public record ParcaIstegi(
    [Required(ErrorMessage = "Parça adı gerekli.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Parça adı 2-120 karakter olmalı.")]
    string Ad,
    [StringLength(60, ErrorMessage = "Stok kodu en fazla 60 karakter olabilir.")]
    string? Kod,
    [Range(typeof(decimal), "0", FaturaKurallari.EnFazlaTutar,
        ParseLimitsInInvariantCulture = true, ErrorMessage = "Birim fiyat 0 ile 10.000.000 arasında olmalı.")]
    decimal BirimFiyat,
    // Stok bu adede düşünce parça "azalan" olarak işaretlenir.
    [Range(0, 100000, ErrorMessage = "Kritik seviye 0 ile 100.000 arasında olmalı.")]
    int KritikSeviye);

public record ParcaYaniti(string Id, string Ad, string? Kod, decimal BirimFiyat, int StokAdedi, int KritikSeviye)
{
    public bool AzaldiMi => StokAdedi <= KritikSeviye;
}

// Stok girişi (artı) ya da düzeltmesi (eksi). Servis kaydında kullanılan parçalar stoktan kendiliğinden düşer.
public record StokHareketiIstegi(
    [Range(-100000, 100000, ErrorMessage = "Adet -100.000 ile 100.000 arasında olmalı.")]
    int Adet,
    [StringLength(200, ErrorMessage = "Açıklama en fazla 200 karakter olabilir.")]
    string? Aciklama);

public record StokHareketiYaniti(string Id, DateTime Tarih, int Adet, string? Aciklama, string KullaniciAdi, string? ServisKaydiNo);
