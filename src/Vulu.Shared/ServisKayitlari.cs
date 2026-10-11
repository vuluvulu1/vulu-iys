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
    string Sorun,
    [StringLength(60, ErrorMessage = "Marka en fazla 60 karakter olabilir.")]
    string? Marka = null,
    [StringLength(60, ErrorMessage = "Model en fazla 60 karakter olabilir.")]
    string? Model = null,
    // Seri numarası ya da IMEI; aynı cihazın önceki kayıtları bununla bulunur.
    [StringLength(60, ErrorMessage = "Seri no en fazla 60 karakter olabilir.")]
    string? SeriNo = null,
    bool GarantiKapsaminda = false,
    // Cihazla birlikte teslim alınanlar (şarj aleti, çanta...).
    [StringLength(300, ErrorMessage = "Aksesuarlar en fazla 300 karakter olabilir.")]
    string? Aksesuarlar = null,
    // Cihazın teslim alındığı andaki görünür durumu (çizik, kırık...).
    [StringLength(500, ErrorMessage = "Fiziksel durum en fazla 500 karakter olabilir.")]
    string? FizikselDurum = null);

public record ServisDurumuIstegi(ServisDurumu Durum);

public record YapilanIslemIstegi(
    [StringLength(1000, ErrorMessage = "Yapılan işlem en fazla 1000 karakter olabilir.")]
    string? Metin);

public record NotIstegi(
    [Required(ErrorMessage = "Not boş olamaz.")]
    [StringLength(500, MinimumLength = 2, ErrorMessage = "Not 2-500 karakter olmalı.")]
    string Metin);

// Müşteriye gönderilecek hazır mesaj. Uygulama mesajı kendisi göndermez; personelin bilgisayarındaki WhatsApp
// ya da e-posta programını bu metinle açar. WhatsAppNumarasi ülke koduyla, başında + olmadan gelir.
public record BildirimYaniti(string Metin, string TakipAdresi, string? WhatsAppNumarasi, string? Eposta);

// Müşterinin hangi yolla bilgilendirildiğini işlem geçmişine yazar.
public record BildirimIstegi(BildirimKanali Kanal);

public record IslemYaniti(string Id, DateTime Tarih, string KullaniciAdi, IslemTuru Tur, string Aciklama);

// Tamire başlamadan önce müşteriye bildirilen tahmini tutar (KDV dahil).
public record TeklifIstegi(
    [Range(typeof(decimal), FaturaKurallari.EnAzTutar, FaturaKurallari.EnFazlaTutar,
        ParseLimitsInInvariantCulture = true, ErrorMessage = FaturaKurallari.TutarMesaji)]
    decimal Tutar,
    [StringLength(500, ErrorMessage = "Açıklama en fazla 500 karakter olabilir.")]
    string? Aciklama);

// Müşterinin teklife verdiği yanıt; not alanına onayın nasıl alındığı yazılır (telefonla, yüz yüze...).
public record TeklifYanitIstegi(
    bool Onaylandi,
    [StringLength(300, ErrorMessage = "Not en fazla 300 karakter olabilir.")]
    string? Not);

public record TeklifYaniti(
    decimal Tutar, string? Aciklama, TeklifDurumu Durum, DateTime Tarih, DateTime? YanitTarihi, string? YanitNotu);

public record ParcaKullanimIstegi(
    [Required(ErrorMessage = "Parça seçilmeli.")]
    [RegularExpression(KimlikKurallari.Desen, ErrorMessage = "Geçersiz parça.")]
    string ParcaId,
    [Range(1, 1000, ErrorMessage = "Adet 1 ile 1000 arasında olmalı.")]
    int Adet);

// Ad ve birim fiyat parçanın kullanıldığı andaki değerleridir; stok kartı sonradan değişse de kayıt değişmez.
public record KullanilanParcaYaniti(string Id, string ParcaId, string Ad, int Adet, decimal BirimFiyat);

public record FaturaIstegi(
    [Range(typeof(decimal), FaturaKurallari.EnAzTutar, FaturaKurallari.EnFazlaTutar,
        ParseLimitsInInvariantCulture = true, ErrorMessage = FaturaKurallari.TutarMesaji)]
    decimal Tutar,
    bool OdendiMi);

public record FaturaYaniti(string No, decimal Tutar, bool OdendiMi, DateTime OlusturmaTarihi, DateTime? OdemeTarihi);

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
    FaturaYaniti? Fatura,
    string No,
    string? Marka,
    string? Model,
    string? SeriNo,
    bool GarantiKapsaminda,
    string? Aksesuarlar,
    string? FizikselDurum,
    string? YapilanIslem,
    DateTime? TeslimTarihi,
    TeklifYaniti? Teklif,
    IReadOnlyList<KullanilanParcaYaniti> Parcalar,
    // Müşterinin cihaz durumunu sorgulama sayfasında kullandığı kod.
    string TakipKodu)
{
    public decimal ParcaToplami => Parcalar.Sum(p => p.Adet * p.BirimFiyat);
}

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
