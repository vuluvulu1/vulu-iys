namespace Vulu.Api.Faturalar;

// Fatura belgesinin başlığında görünen işletme bilgileri ve vergi oranı. Gizli değildir; appsettings.json içinde durur.
public class FirmaAyarlari
{
    public const string Bolum = "Firma";

    public string Ad { get; set; } = "vulu Teknik Servis";
    public string? Adres { get; set; }
    public string? Telefon { get; set; }
    public string? Eposta { get; set; }
    public string? VergiDairesi { get; set; }
    public string? VergiNo { get; set; }

    // Fatura tutarı KDV dahil girilir; belge bu orana göre matrah ve KDV'yi ayırır.
    public decimal KdvOrani { get; set; } = 20;

    // Belgenin altına yazılan not.
    public string? BelgeNotu { get; set; }
}
