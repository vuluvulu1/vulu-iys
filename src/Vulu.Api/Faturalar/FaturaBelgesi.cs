using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Faturalar;

// Servis kaydındaki faturadan A4 PDF üretir. Belge saklanmaz; her istekte kayıttaki ve işletme profilindeki
// güncel bilgiden yeniden çizilir. Başlıkta işletmenin kendi adı, bilgileri ve logosu yer alır.
public static class FaturaBelgesi
{
    private const string Koyu = "#2b1a10";
    private const string Vurgu = "#8a5c36";
    private const string Soluk = "#7a6654";
    private const string Cizgi = "#d9c7b3";
    private const string Zemin = "#f6efe6";

    private const string BelgeNotu = "Bu belge bilgilendirme amaçlıdır; e-Fatura veya e-Arşiv Fatura yerine geçmez.";

    // Fatura numarasının başındaki kısaltma işletme kodundan türetilir (örn. "duzce-servis" -> "DUZC").
    public static string NumaraOnEki(Isletme isletme)
    {
        var harfler = new string(isletme.Kod.Where(char.IsLetterOrDigit).Take(4).ToArray()).ToUpperInvariant();
        return harfler.Length > 0 ? harfler : "F";
    }

    public static byte[] Olustur(ServisKaydi kayit, Musteri? musteri, Isletme isletme)
    {
        var fatura = kayit.Fatura ?? throw new InvalidOperationException("Kaydın faturası yok.");

        // Tutar KDV dahildir; matrah ve KDV ondan ayrılır.
        var matrah = decimal.Round(fatura.Tutar / (1 + isletme.KdvOrani / 100), 2, MidpointRounding.AwayFromZero);
        var kdv = fatura.Tutar - matrah;

        return Document.Create(belge => belge.Page(sayfa =>
        {
            sayfa.Size(PageSizes.A4);
            sayfa.Margin(40);
            sayfa.DefaultTextStyle(stil => stil.FontSize(10).FontColor(Koyu));

            sayfa.Header().Row(satir =>
            {
                if (isletme.Logo is { Length: > 0 } logo)
                    satir.ConstantItem(66).PaddingRight(12).MaxHeight(54).Image(logo).FitArea();

                satir.RelativeItem().Column(sutun =>
                {
                    sutun.Item().Text(isletme.Ad).FontSize(16).Bold();
                    foreach (var bilgi in IsletmeSatirlari(isletme))
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
                    ToplamSatiri(toplamlar, $"KDV (%{isletme.KdvOrani:0.##})", Zaman.Para(kdv));
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

            sayfa.Footer().BorderTop(1).BorderColor(Cizgi).PaddingTop(6).Text(BelgeNotu).FontSize(8).FontColor(Soluk);
        })).GeneratePdf();
    }

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
}
