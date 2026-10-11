using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;
using static Vulu.Api.Pdf.BelgeOrtak;

namespace Vulu.Api.Pdf;

// Cihaz teslim alınırken müşteriye verilen kabul formu ve cihaz geri verilirken imzalatılan teslim formu.
// İki nüsha basılıp karşılıklı imzalanır; cihazın hangi durumda, hangi aksesuarlarla alındığının kanıtıdır.
public static class ServisFormu
{
    // Belge adındaki "İ" harfi Türkçe kurala göre küçültülür.
    private static readonly CultureInfo Turkce = CultureInfo.GetCultureInfo("tr-TR");

    public const string VarsayilanKosullar =
        "Cihaz yalnızca bu formu ya da kimliğini gösteren kişiye teslim edilir. Onarım sırasında cihazdaki veriler " +
        "silinebilir; yedekleme müşterinin sorumluluğundadır. Bildirilen ücret ve süre tahminidir; arıza tespitinden " +
        "sonra değişirse müşteriye bilgi verilir ve onayı alınmadan işleme başlanmaz.";

    // takipAdresi: müşterinin cihaz durumunu sorgulayacağı sayfa; bilinmiyorsa forma yazılmaz.
    public static byte[] Kabul(ServisKaydi kayit, Musteri? musteri, Isletme isletme, string? takipAdresi) =>
        Olustur(kayit, musteri, isletme, "SERVİS KABUL FORMU", Zaman.TarihSaat(kayit.OlusturmaTarihi), sutun =>
        {
            sutun.Item().Element(alan => Bolum(alan, "MÜŞTERİNİN BİLDİRDİĞİ SORUN", kayit.Sorun));

            if (kayit.Teklif is { } teklif)
                sutun.Item().Element(alan => Bolum(alan, "TAHMİNİ ÜCRET (KDV DAHİL)",
                    Zaman.Para(teklif.Tutar) + (string.IsNullOrWhiteSpace(teklif.Aciklama) ? "" : $" – {teklif.Aciklama}")));

            if (takipAdresi is not null)
                sutun.Item().Element(alan => Bolum(alan, "CİHAZ DURUMU SORGULAMA",
                    $"{takipAdresi}\nTakip kodu: {kayit.TakipKodu}"));

            sutun.Item().Element(alan => Bolum(alan, "SERVİS KOŞULLARI",
                string.IsNullOrWhiteSpace(isletme.ServisKosullari) ? VarsayilanKosullar : isletme.ServisKosullari, kucuk: true));

            sutun.Item().Element(alan => Imzalar(alan, "Teslim eden (müşteri)", kayit.Musteri.AdSoyad,
                "Teslim alan (işletme)", kayit.TeknisyenAdi));
        });

    public static byte[] Teslim(ServisKaydi kayit, Musteri? musteri, Isletme isletme)
    {
        var tarih = kayit.TeslimTarihi ?? DateTime.UtcNow;

        return Olustur(kayit, musteri, isletme, "CİHAZ TESLİM FORMU", Zaman.TarihSaat(tarih), sutun =>
        {
            sutun.Item().Element(alan => Bolum(alan, "MÜŞTERİNİN BİLDİRDİĞİ SORUN", kayit.Sorun));

            sutun.Item().Element(alan => Bolum(alan, "YAPILAN İŞLEM", kayit.Durum == ServisDurumu.IptalEdildi
                ? "İşlem yapılmadan iade edildi." + (string.IsNullOrWhiteSpace(kayit.YapilanIslem) ? "" : $" {kayit.YapilanIslem}")
                : string.IsNullOrWhiteSpace(kayit.YapilanIslem) ? "—" : kayit.YapilanIslem));

            if (kayit.Parcalar.Count > 0)
                sutun.Item().Element(alan => Bolum(alan, "KULLANILAN PARÇALAR",
                    string.Join("\n", kayit.Parcalar.Select(p => $"{p.Adet} × {p.Ad}"))));

            if (kayit.Fatura is { } fatura)
                sutun.Item().Element(alan => Bolum(alan, "ÜCRET (KDV DAHİL)",
                    $"{Zaman.Para(fatura.Tutar)} – {(fatura.OdendiMi ? "ödendi" : "ödenmedi")} (fatura no: {fatura.No})"));

            sutun.Item().Element(alan => Bolum(alan, "TESLİM BEYANI",
                "Yukarıda bilgileri yazılı cihazı, belirtilen aksesuarlarıyla birlikte eksiksiz ve çalışır durumda " +
                "teslim aldım.", kucuk: true));

            sutun.Item().Element(alan => Imzalar(alan, "Teslim eden (işletme)", kayit.TeknisyenAdi,
                "Teslim alan (müşteri)", kayit.Musteri.AdSoyad));
        });
    }

    private static byte[] Olustur(ServisKaydi kayit, Musteri? musteri, Isletme isletme, string belgeAdi, string tarih,
        Action<ColumnDescriptor> govde) =>
        Document.Create(belge => belge.Page(sayfa =>
        {
            SayfayiKur(sayfa);

            sayfa.Header().Element(alan => Baslik(alan, isletme, belgeAdi, $"Kayıt no: {kayit.No}", $"Tarih: {tarih}"));

            sayfa.Content().PaddingTop(20).Column(sutun =>
            {
                sutun.Spacing(12);

                sutun.Item().Row(satir =>
                {
                    satir.Spacing(12);
                    satir.RelativeItem().Element(alan => Kutu(alan, "MÜŞTERİ", kayit.Musteri.AdSoyad,
                        kayit.Musteri.Telefon, musteri?.Email, musteri?.Adres));
                    satir.RelativeItem().Element(alan => Kutu(alan, "CİHAZ", kayit.CihazAdi,
                        kayit.SeriNo is null ? null : $"Seri no / IMEI: {kayit.SeriNo}",
                        $"Garanti: {(kayit.GarantiKapsaminda ? "kapsamında" : "kapsam dışı")}",
                        $"Kabul tarihi: {Zaman.TarihSaat(kayit.OlusturmaTarihi)}"));
                });

                sutun.Item().Element(alan => Bolum(alan, "TESLİM ALINAN AKSESUARLAR",
                    string.IsNullOrWhiteSpace(kayit.Aksesuarlar) ? "Yok" : kayit.Aksesuarlar));
                sutun.Item().Element(alan => Bolum(alan, "CİHAZIN TESLİM ALINDIĞI ANDAKİ DURUMU",
                    string.IsNullOrWhiteSpace(kayit.FizikselDurum) ? "Belirtilmedi" : kayit.FizikselDurum));

                govde(sutun);
            });

            sayfa.Footer().Element(alan => AltBilgi(alan, $"{isletme.Ad} – {belgeAdi.ToLower(Turkce)} – {kayit.No}"));
        })).GeneratePdf();

    private static void Bolum(IContainer alan, string baslik, string metin, bool kucuk = false) =>
        alan.Column(sutun =>
        {
            sutun.Item().Text(baslik).FontSize(8).Bold().FontColor(Soluk);
            sutun.Item().PaddingTop(2).Text(metin).FontSize(kucuk ? 9 : 10);
        });

    private static void Imzalar(IContainer alan, string solBaslik, string? solAd, string sagBaslik, string? sagAd) =>
        alan.PaddingTop(14).Row(satir =>
        {
            satir.Spacing(40);
            satir.RelativeItem().Element(kutu => Imza(kutu, solBaslik, solAd));
            satir.RelativeItem().Element(kutu => Imza(kutu, sagBaslik, sagAd));
        });

    private static void Imza(IContainer alan, string baslik, string? ad) =>
        alan.Column(sutun =>
        {
            sutun.Item().Text(baslik).FontSize(8).Bold().FontColor(Soluk);
            sutun.Item().Text(string.IsNullOrWhiteSpace(ad) ? " " : ad);
            sutun.Item().PaddingTop(38).BorderTop(1).BorderColor(Koyu).PaddingTop(3).Text("İmza").FontSize(8).FontColor(Soluk);
        });
}
