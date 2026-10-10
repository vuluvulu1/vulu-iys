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

    // Yerel tarihi, API sorgu dizgisinde kullanılacak UTC anına çevirir.
    public static string SorguTarihi(DateTime yerel) =>
        Uri.EscapeDataString(DateTime.SpecifyKind(yerel, DateTimeKind.Local).ToUniversalTime().ToString("o"));

    // Haftalar Pazartesi başlar.
    public static DateTime HaftaBasi(DateTime gun) => gun.Date.AddDays(-(((int)gun.DayOfWeek + 6) % 7));

    public static string HaftaAraligi(DateTime haftaBasi)
    {
        var son = haftaBasi.AddDays(6);
        return haftaBasi.Month == son.Month
            ? $"{haftaBasi.Day} – {son.ToString("d MMMM yyyy", Turkce)}"
            : $"{haftaBasi.ToString("d MMMM", Turkce)} – {son.ToString("d MMMM yyyy", Turkce)}";
    }

    public static string GunBasligi(DateTime gun) => gun.ToString("ddd d MMM", Turkce);

    public static string SaatAraligi(DateTime utc, int sureDakika)
    {
        var baslangic = utc.ToLocalTime();
        return $"{baslangic:HH:mm}–{baslangic.AddMinutes(sureDakika):HH:mm}";
    }

    public static string Sure(int dakika) => dakika switch
    {
        < 60 => $"{dakika} dakika",
        _ when dakika % 60 == 0 => $"{dakika / 60} saat",
        _ => $"{dakika / 60} saat {dakika % 60} dakika"
    };

    public static string TakvimSinifi(RandevuDurumu durum) => durum switch
    {
        RandevuDurumu.Planlandi => "planli",
        RandevuDurumu.Tamamlandi => "tamam",
        _ => "iptal"
    };

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

    public static string Durum(RandevuDurumu durum) => durum switch
    {
        RandevuDurumu.Planlandi => "Planlandı",
        RandevuDurumu.Tamamlandi => "Tamamlandı",
        RandevuDurumu.IptalEdildi => "İptal edildi",
        _ => durum.ToString()
    };

    public static string DurumSinifi(RandevuDurumu durum) => durum switch
    {
        RandevuDurumu.Planlandi => "etiket suruyor",
        RandevuDurumu.Tamamlandi => "etiket tamam",
        _ => "etiket iptal"
    };

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial Regex BinlikAyracli();

    [GeneratedRegex(@"^\d+(\.\d{1,2})?$")]
    private static partial Regex YalinSayi();
}
