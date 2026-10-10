using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Faturalar;

// Servis kaydındaki faturadan A4 PDF üretir. Belge saklanmaz; her istekte kayıttaki güncel bilgiden yeniden çizilir.
public static class FaturaBelgesi
{
    private const string Koyu = "#2b1a10";
    private const string Vurgu = "#8a5c36";
    private const string Soluk = "#7a6654";
    private const string Cizgi = "#d9c7b3";
    private const string Zemin = "#f6efe6";

    private static readonly string Logo = LogoyuOku();

    public static byte[] Olustur(ServisKaydi kayit, Musteri? musteri, FirmaAyarlari firma)
    {
        var fatura = kayit.Fatura ?? throw new InvalidOperationException("Kaydın faturası yok.");

        // Tutar KDV dahildir; matrah ve KDV ondan ayrılır.
        var matrah = decimal.Round(fatura.Tutar / (1 + firma.KdvOrani / 100), 2, MidpointRounding.AwayFromZero);
        var kdv = fatura.Tutar - matrah;

        return Document.Create(belge => belge.Page(sayfa =>
        {
            sayfa.Size(PageSizes.A4);
            sayfa.Margin(40);
            sayfa.DefaultTextStyle(stil => stil.FontSize(10).FontColor(Koyu));

            sayfa.Header().Row(satir =>
            {
                satir.ConstantItem(54).Height(54).Svg(Logo);

                satir.RelativeItem().PaddingLeft(12).Column(sutun =>
                {
                    sutun.Item().Text(firma.Ad).FontSize(16).Bold();
                    foreach (var bilgi in FirmaSatirlari(firma))
                        sutun.Item().Text(bilgi).FontSize(9).FontColor(Soluk);
                });

                satir.ConstantItem(180).AlignRight().Column(sutun =>
                {
                    sutun.Item().AlignRight().Text("FATURA").FontSize(22).Bold().FontColor(Vurgu);
                    sutun.Item().AlignRight().Text($"No: {fatura.No}");
                    sutun.Item().AlignRight().Text($"Tarih: {Zaman.Tarih(fatura.OlusturmaTarihi)}");
                });
            });

            sayfa.Content().PaddingTop(24).Column(sutun =>
            {
                sutun.Spacing(18);

                sutun.Item().Background(Zemin).Padding(12).Column(kutu =>
                {
                    kutu.Item().Text("MÜŞTERİ").FontSize(8).Bold().FontColor(Soluk);
                    kutu.Item().PaddingTop(2).Text(kayit.Musteri.AdSoyad).FontSize(12).Bold();
                    foreach (var bilgi in new[] { kayit.Musteri.Telefon, musteri?.Email, musteri?.Adres })
                        if (!string.IsNullOrWhiteSpace(bilgi))
                            kutu.Item().Text(bilgi);
                });

                sutun.Item().Table(tablo =>
                {
                    tablo.ColumnsDefinition(sutunlar =>
                    {
                        sutunlar.RelativeColumn();
                        sutunlar.ConstantColumn(120);
                    });

                    tablo.Header(baslik =>
                    {
                        baslik.Cell().Element(BaslikHucresi).Text("Hizmet");
                        baslik.Cell().Element(BaslikHucresi).AlignRight().Text("Tutar");
                    });

                    tablo.Cell().Element(Hucre).Column(hizmet =>
                    {
                        hizmet.Item().Text($"Teknik servis – {kayit.CihazBilgisi}").Bold();
                        hizmet.Item().Text(kayit.Sorun).FontSize(9).FontColor(Soluk);
                        if (kayit.TeknisyenAdi is not null)
                            hizmet.Item().Text($"Teknisyen: {kayit.TeknisyenAdi}").FontSize(9).FontColor(Soluk);
                        if (kayit.TamamlanmaTarihi is { } tamamlanma)
                            hizmet.Item().Text($"Tamamlanma: {Zaman.Tarih(tamamlanma)}").FontSize(9).FontColor(Soluk);
                    });
                    tablo.Cell().Element(Hucre).AlignRight().Text(Zaman.Para(matrah));
                });

                sutun.Item().AlignRight().Width(240).Column(toplamlar =>
                {
                    ToplamSatiri(toplamlar, "Ara toplam", Zaman.Para(matrah));
                    ToplamSatiri(toplamlar, $"KDV (%{firma.KdvOrani:0.##})", Zaman.Para(kdv));
                    toplamlar.Item().PaddingTop(4).BorderTop(1).BorderColor(Koyu).PaddingTop(6).Row(satir =>
                    {
                        satir.RelativeItem().Text("Genel toplam").FontSize(12).Bold();
                        satir.RelativeItem().AlignRight().Text(Zaman.Para(fatura.Tutar)).FontSize(12).Bold();
                    });
                });

                sutun.Item().Text(metin =>
                {
                    metin.Span("Ödeme durumu: ").FontColor(Soluk);
                    if (fatura.OdemeTarihi is { } odeme)
                        metin.Span($"Ödendi ({Zaman.Tarih(odeme)})").Bold();
                    else
                        metin.Span("Ödenmedi").Bold();
                });
            });

            sayfa.Footer().Column(sutun =>
            {
                if (!string.IsNullOrWhiteSpace(firma.BelgeNotu))
                    sutun.Item().BorderTop(1).BorderColor(Cizgi).PaddingTop(6)
                        .Text(firma.BelgeNotu).FontSize(8).FontColor(Soluk);
            });
        })).GeneratePdf();
    }

    private static IEnumerable<string> FirmaSatirlari(FirmaAyarlari firma)
    {
        if (!string.IsNullOrWhiteSpace(firma.Adres)) yield return firma.Adres;

        var iletisim = string.Join("  ·  ", new[] { firma.Telefon, firma.Eposta }.Where(b => !string.IsNullOrWhiteSpace(b)));
        if (iletisim.Length > 0) yield return iletisim;

        if (!string.IsNullOrWhiteSpace(firma.VergiNo))
            yield return string.IsNullOrWhiteSpace(firma.VergiDairesi)
                ? $"Vergi no: {firma.VergiNo}"
                : $"{firma.VergiDairesi} V.D. – Vergi no: {firma.VergiNo}";
    }

    private static void ToplamSatiri(ColumnDescriptor sutun, string etiket, string deger) =>
        sutun.Item().PaddingVertical(2).Row(satir =>
        {
            satir.RelativeItem().Text(etiket).FontColor(Soluk);
            satir.RelativeItem().AlignRight().Text(deger);
        });

    private static IContainer BaslikHucresi(IContainer hucre) =>
        hucre.Background(Koyu).PaddingVertical(6).PaddingHorizontal(10)
            .DefaultTextStyle(stil => stil.FontColor(Colors.White).FontSize(9).Bold());

    private static IContainer Hucre(IContainer hucre) =>
        hucre.BorderBottom(1).BorderColor(Cizgi).PaddingVertical(10).PaddingHorizontal(10);

    private static string LogoyuOku()
    {
        using var akis = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vulu.Api.logo.svg")
            ?? throw new InvalidOperationException("Gömülü logo bulunamadı.");
        using var okuyucu = new StreamReader(akis);
        return okuyucu.ReadToEnd();
    }
}
