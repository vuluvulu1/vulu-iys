using System.Globalization;

namespace Vulu.Api.Altyapi;

// Tarihler UTC saklanır. Sunucunun kendi saat dilimi barındırma ortamına göre değiştiği için,
// kullanıcıya gösterilen metinlerde ve belgelerde Türkiye saati açıkça kullanılır.
public static class Zaman
{
    private static readonly TimeZoneInfo Turkiye = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static readonly CultureInfo Turkce = CultureInfo.GetCultureInfo("tr-TR");

    public static DateTime TurkiyeSaati(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Turkiye);

    public static string Tarih(DateTime utc) => TurkiyeSaati(utc).ToString("dd.MM.yyyy", Turkce);

    public static string TarihSaat(DateTime utc) => TurkiyeSaati(utc).ToString("dd.MM.yyyy HH:mm", Turkce);

    public static string Para(decimal tutar) => tutar.ToString("N2", Turkce) + " ₺";

    // İstemciden gelen tarihler bu aralıkta olmalıdır. Aralığın dışındaki değerler (0001 ya da 9999 yılı gibi)
    // anlamsızdır ve üzerlerine süre eklenince taşarlar.
    public static bool MakulMu(DateTime tarih) => tarih.Year is >= 2000 and <= 2100;

    public const string MakulDegilMesaji = "Tarih 2000 ile 2100 yılları arasında olmalı.";

    // İstemciden gelen tarihleri UTC'ye çevirir; saat dilimi belirtilmemiş olanlar UTC sayılır.
    public static DateTime UtcYap(DateTime tarih) => tarih.Kind switch
    {
        DateTimeKind.Utc => tarih,
        DateTimeKind.Local => tarih.ToUniversalTime(),
        _ => DateTime.SpecifyKind(tarih, DateTimeKind.Utc)
    };
}
