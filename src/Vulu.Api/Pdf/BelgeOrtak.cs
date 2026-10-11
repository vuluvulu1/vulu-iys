using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Pdf;

// Fatura ve servis formlarının paylaştığı görünüm: renkler, işletme başlığı ve tablo hücreleri.
internal static class BelgeOrtak
{
    public const string Koyu = "#2b1a10";
    public const string Vurgu = "#8a5c36";
    public const string Soluk = "#7a6654";
    public const string Cizgi = "#d9c7b3";
    public const string Zemin = "#f6efe6";

    public static void SayfayiKur(PageDescriptor sayfa)
    {
        sayfa.Size(PageSizes.A4);
        sayfa.Margin(40);
        sayfa.DefaultTextStyle(stil => stil.FontSize(10).FontColor(Koyu));
    }

    // Solda işletmenin logosu ve bilgileri, sağda belgenin adı ve altındaki satırlar (numara, tarih...).
    public static void Baslik(IContainer alan, Isletme isletme, string belgeAdi, params string[] satirlar) =>
        alan.Row(satir =>
        {
            if (isletme.Logo is { Length: > 0 } logo)
                satir.ConstantItem(66).PaddingRight(12).MaxHeight(54).Image(logo).FitArea();

            satir.RelativeItem().Column(sutun =>
            {
                sutun.Item().Text(isletme.Ad).FontSize(16).Bold();
                foreach (var bilgi in IsletmeSatirlari(isletme))
                    sutun.Item().Text(bilgi).FontSize(9).FontColor(Soluk);
            });

            satir.ConstantItem(200).AlignRight().Column(sutun =>
            {
                sutun.Item().AlignRight().Text(belgeAdi).FontSize(belgeAdi.Length > 10 ? 15 : 22).Bold().FontColor(Vurgu);
                foreach (var bilgi in satirlar)
                    sutun.Item().AlignRight().Text(bilgi);
            });
        });

    // Başlıklı bilgi kutusu; boş satırlar atlanır.
    public static void Kutu(IContainer alan, string baslik, string? anaSatir, params string?[] satirlar) =>
        alan.Background(Zemin).Padding(12).Column(kutu =>
        {
            kutu.Item().Text(baslik).FontSize(8).Bold().FontColor(Soluk);
            if (!string.IsNullOrWhiteSpace(anaSatir))
                kutu.Item().PaddingTop(2).Text(anaSatir).FontSize(12).Bold();
            foreach (var bilgi in satirlar)
                if (!string.IsNullOrWhiteSpace(bilgi))
                    kutu.Item().Text(bilgi);
        });

    public static void AltBilgi(IContainer alan, string metin) =>
        alan.BorderTop(1).BorderColor(Cizgi).PaddingTop(6).Text(metin).FontSize(8).FontColor(Soluk);

    public static IContainer BaslikHucresi(IContainer hucre) =>
        hucre.Background(Koyu).PaddingVertical(6).PaddingHorizontal(10)
            .DefaultTextStyle(stil => stil.FontColor(Colors.White).FontSize(9).Bold());

    public static IContainer Hucre(IContainer hucre) =>
        hucre.BorderBottom(1).BorderColor(Cizgi).PaddingVertical(8).PaddingHorizontal(10);

    private static IEnumerable<string> IsletmeSatirlari(Isletme isletme)
    {
        if (!string.IsNullOrWhiteSpace(isletme.Adres)) yield return isletme.Adres;

        var iletisim = string.Join("  ·  ", new[] { isletme.Telefon, isletme.Eposta }.Where(b => !string.IsNullOrWhiteSpace(b)));
        if (iletisim.Length > 0) yield return iletisim;

        if (!string.IsNullOrWhiteSpace(isletme.VergiNo))
            yield return string.IsNullOrWhiteSpace(isletme.VergiDairesi)
                ? $"Vergi no: {isletme.VergiNo}"
                : $"{isletme.VergiDairesi} V.D. – Vergi no: {isletme.VergiNo}";
    }
}
