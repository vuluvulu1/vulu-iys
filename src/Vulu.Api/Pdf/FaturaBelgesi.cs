using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri.Belgeler;
using static Vulu.Api.Pdf.BelgeOrtak;

namespace Vulu.Api.Pdf;

// Servis kaydındaki faturadan A4 PDF üretir. Belge saklanmaz; her istekte kayıttaki ve işletme profilindeki
// güncel bilgiden yeniden çizilir. Başlıkta işletmenin kendi adı, bilgileri ve logosu yer alır.
public static class FaturaBelgesi
{
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
        var kalemler = Kalemler(kayit, fatura.Tutar);

        return Document.Create(belge => belge.Page(sayfa =>
        {
            SayfayiKur(sayfa);

            sayfa.Header().Element(alan => Baslik(alan, isletme, "FATURA",
                $"No: {fatura.No}", $"Tarih: {Zaman.Tarih(fatura.OlusturmaTarihi)}", $"Servis kaydı: {kayit.No}"));

            sayfa.Content().PaddingTop(24).Column(sutun =>
            {
                sutun.Spacing(18);

                sutun.Item().Element(alan => Kutu(alan, "MÜŞTERİ", kayit.Musteri.AdSoyad,
                    kayit.Musteri.Telefon, musteri?.Email, musteri?.Adres));

                sutun.Item().Table(tablo =>
                {
                    tablo.ColumnsDefinition(sutunlar =>
                    {
                        sutunlar.RelativeColumn();
                        sutunlar.ConstantColumn(50);
                        sutunlar.ConstantColumn(90);
                        sutunlar.ConstantColumn(100);
                    });

                    tablo.Header(baslik =>
                    {
                        baslik.Cell().Element(BaslikHucresi).Text("Açıklama");
                        baslik.Cell().Element(BaslikHucresi).AlignRight().Text("Adet");
                        baslik.Cell().Element(BaslikHucresi).AlignRight().Text("Birim fiyat");
                        baslik.Cell().Element(BaslikHucresi).AlignRight().Text("Tutar");
                    });

                    foreach (var kalem in kalemler)
                    {
                        tablo.Cell().Element(Hucre).Column(aciklama =>
                        {
                            aciklama.Item().Text(kalem.Ad).Bold();
                            foreach (var ayrinti in kalem.Ayrintilar)
                                aciklama.Item().Text(ayrinti).FontSize(9).FontColor(Soluk);
                        });
                        tablo.Cell().Element(Hucre).AlignRight().Text(kalem.Adet?.ToString() ?? "");
                        tablo.Cell().Element(Hucre).AlignRight().Text(kalem.BirimFiyat is { } birim ? Zaman.Para(birim) : "");
                        tablo.Cell().Element(Hucre).AlignRight().Text(Zaman.Para(kalem.Tutar));
                    }
                });

                sutun.Item().AlignRight().Width(240).Column(toplamlar =>
                {
                    ToplamSatiri(toplamlar, "Ara toplam (KDV hariç)", Zaman.Para(matrah));
                    ToplamSatiri(toplamlar, $"KDV (%{isletme.KdvOrani:0.##})", Zaman.Para(kdv));
                    toplamlar.Item().PaddingTop(4).BorderTop(1).BorderColor(Koyu).PaddingTop(6).Row(satir =>
                    {
                        satir.RelativeItem().Text("Genel toplam").FontSize(12).Bold();
                        satir.RelativeItem().AlignRight().Text(Zaman.Para(fatura.Tutar)).FontSize(12).Bold();
                    });
                });

                sutun.Item().Text(metin =>
                {
                    metin.Span("Fiyatlara KDV dahildir. Ödeme durumu: ").FontColor(Soluk);
                    if (fatura.OdemeTarihi is { } odeme)
                        metin.Span($"Ödendi ({Zaman.Tarih(odeme)})").Bold();
                    else
                        metin.Span("Ödenmedi").Bold();
                });
            });

            sayfa.Footer().Element(alan => AltBilgi(alan, BelgeNotu));
        })).GeneratePdf();
    }

    private record Kalem(string Ad, IReadOnlyList<string> Ayrintilar, int? Adet, decimal? BirimFiyat, decimal Tutar);

    // Fatura tek bir tutar olarak girilir. Kullanılan parçalar ayrı satırlarda gösterilir, tutarın kalanı
    // işçiliktir. Tutar parçaların toplamından azsa (indirim yapılmışsa) döküm tutmaz; tek satır yazılır.
    private static List<Kalem> Kalemler(ServisKaydi kayit, decimal tutar)
    {
        var ayrintilar = new List<string>();
        if (!string.IsNullOrWhiteSpace(kayit.YapilanIslem)) ayrintilar.Add(kayit.YapilanIslem);
        else ayrintilar.Add(kayit.Sorun);
        if (kayit.TeknisyenAdi is not null) ayrintilar.Add($"Teknisyen: {kayit.TeknisyenAdi}");
        if (kayit.TamamlanmaTarihi is { } tamamlanma) ayrintilar.Add($"Tamamlanma: {Zaman.Tarih(tamamlanma)}");

        var hizmetAdi = $"Teknik servis – {ServisFormu.CihazAdi(kayit)}";
        var parcaToplami = kayit.Parcalar.Sum(p => p.Adet * p.BirimFiyat);

        if (kayit.Parcalar.Count == 0 || parcaToplami > tutar)
        {
            if (kayit.Parcalar.Count > 0)
                ayrintilar.Add("Kullanılan parçalar: " + string.Join(", ", kayit.Parcalar.Select(p => $"{p.Adet} × {p.Ad}")));
            return [new Kalem(hizmetAdi, ayrintilar, null, null, tutar)];
        }

        var kalemler = new List<Kalem>();
        if (tutar > parcaToplami)
            kalemler.Add(new Kalem(hizmetAdi + " (işçilik)", ayrintilar, null, null, tutar - parcaToplami));
        kalemler.AddRange(kayit.Parcalar.Select(p => new Kalem(p.Ad, [], p.Adet, p.BirimFiyat, p.Adet * p.BirimFiyat)));
        return kalemler;
    }

    private static void ToplamSatiri(ColumnDescriptor sutun, string etiket, string deger) =>
        sutun.Item().PaddingVertical(2).Row(satir =>
        {
            satir.RelativeItem().Text(etiket).FontColor(Soluk);
            satir.RelativeItem().AlignRight().Text(deger);
        });
}
