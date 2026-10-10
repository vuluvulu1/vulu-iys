using System.Globalization;
using System.Text.RegularExpressions;
using Vulu.Shared;

namespace Vulu.Desktop;

// Arayüzde gösterilen tarih, para ve durum metinleri.
public static partial class Bicim
{
    private static readonly CultureInfo Turkce = CultureInfo.GetCultureInfo("tr-TR");

    // API tarihleri UTC verir; ekranda bilgisayarın saat dilimine çevrilir.
    public static string Tarih(DateTime utc) => utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Turkce);

    public static string Para(decimal tutar) => tutar.ToString("N2", Turkce) + " ₺";

    public static string ParaGirdisi(decimal tutar) => tutar.ToString("0.00", Turkce);

    // "1250", "1250,50", "1.250,50" ve "1250.50" yazımlarını kabul eder. Yalnızca nokta içeren ve noktadan sonra
    // tam üç hane gelen yazım ("1.250") Türkçe alışkanlığa göre binlik ayracı sayılır.
    public static bool ParaOku(string? metin, out decimal tutar)
    {
        tutar = 0;
        var girdi = (metin ?? "").Replace(" ", "").Replace("₺", "");
        if (girdi.Length == 0) return false;

        string sade;
        if (girdi.Contains(','))
            sade = girdi.Replace(".", "").Replace(',', '.');
        else if (BinlikAyracli().IsMatch(girdi))
            sade = girdi.Replace(".", "");
        else
            sade = girdi;

        return YalinSayi().IsMatch(sade)
            && decimal.TryParse(sade, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out tutar);
    }

    public static string Durum(ServisDurumu durum) => durum switch
    {
        ServisDurumu.Beklemede => "Beklemede",
        ServisDurumu.DevamEdiyor => "Devam ediyor",
        ServisDurumu.Tamamlandi => "Tamamlandı",
        ServisDurumu.IptalEdildi => "İptal edildi",
        _ => durum.ToString()
    };

    public static string DurumSinifi(ServisDurumu durum) => durum switch
    {
        ServisDurumu.Beklemede => "etiket bekliyor",
        ServisDurumu.DevamEdiyor => "etiket suruyor",
        ServisDurumu.Tamamlandi => "etiket tamam",
        _ => "etiket iptal"
    };

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial Regex BinlikAyracli();

    [GeneratedRegex(@"^\d+(\.\d{1,2})?$")]
    private static partial Regex YalinSayi();
}
