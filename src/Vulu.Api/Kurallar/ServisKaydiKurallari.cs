using System.Security.Cryptography;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Kurallar;

// Servis kaydının durum, teklif ve fatura kuralları. Her yöntem kaydı yerinde değiştirir;
// kural ihlalinde kayda dokunmadan kullanıcıya gösterilecek mesajı, aksi halde null döner.
public static class ServisKaydiKurallari
{
    // Sorgularda kullanılır: iş henüz bitmemiş ve iptal edilmemiş (bkz. DurumAdlari.AcikMi).
    public static readonly ServisDurumu[] AcikDurumlar =
        [ServisDurumu.Beklemede, ServisDurumu.OnayBekliyor, ServisDurumu.DevamEdiyor];

    // Karıştırılan harf ve rakamlar (0/O, 1/I/L) yoktur; müşteri kodu fişten okuyup elle yazar.
    private const string TakipAlfabesi = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int TakipKoduUzunlugu = 10;

    public static string KayitNo(int yil, long sira) => $"SK-{yil}-{sira:D5}";

    public static string YeniTakipKodu() =>
        RandomNumberGenerator.GetString(TakipAlfabesi, TakipKoduUzunlugu);

    public static string? DurumuDegistir(ServisKaydi kayit, ServisDurumu yeniDurum, DateTime simdi)
    {
        if (kayit.Durum == yeniDurum) return null;

        // Fatura bitmiş işe kesilir; iş yeniden açılacak ya da iptal edilecekse önce fatura kaldırılmalıdır.
        if (kayit.Fatura is not null && !yeniDurum.BittiMi())
            return "Faturası olan kayıt yeniden açılamaz ya da iptal edilemez; önce faturayı silin.";

        var onceBitmisti = kayit.Durum.BittiMi();
        kayit.Durum = yeniDurum;

        // Tamamlanma tarihi işin bittiği andır; cihazın sonradan teslim edilmesi onu değiştirmez.
        if (!yeniDurum.BittiMi()) kayit.TamamlanmaTarihi = null;
        else if (!onceBitmisti) kayit.TamamlanmaTarihi = simdi;

        kayit.TeslimTarihi = yeniDurum == ServisDurumu.TeslimEdildi ? simdi : null;
        return null;
    }

    // Teklif verilince kayıt müşterinin yanıtını beklemeye geçer. Yeni teklif öncekinin yerini alır.
    public static string? TeklifVer(ServisKaydi kayit, decimal tutar, string? aciklama, DateTime simdi)
    {
        if (!kayit.Durum.AcikMi())
            return "Teklif yalnızca devam eden bir kayda verilebilir.";

        if (TutarHatasi(tutar) is { } hata) return hata;

        kayit.Teklif = new Teklif { Tutar = tutar, Aciklama = aciklama, Tarih = simdi };
        kayit.Durum = ServisDurumu.OnayBekliyor;
        return null;
    }

    // Onaylanan iş başlar; reddedilen iş iptal edilir (cihaz yine de teslim formuyla iade edilebilir).
    public static string? TeklifiYanitla(ServisKaydi kayit, bool onaylandi, string? not, DateTime simdi)
    {
        if (kayit.Teklif is not { Durum: TeklifDurumu.Bekliyor } teklif)
            return "Yanıt bekleyen bir teklif yok.";

        if (!kayit.Durum.AcikMi())
            return "Kayıt kapandığı için teklif yanıtlanamaz.";

        teklif.Durum = onaylandi ? TeklifDurumu.Onaylandi : TeklifDurumu.Reddedildi;
        teklif.YanitTarihi = simdi;
        teklif.YanitNotu = not;
        kayit.Durum = onaylandi ? ServisDurumu.DevamEdiyor : ServisDurumu.IptalEdildi;
        return null;
    }

    public static string? FaturayiKaydet(ServisKaydi kayit, decimal tutar, bool odendiMi, DateTime simdi)
    {
        if (!kayit.Durum.BittiMi())
            return "Fatura yalnızca tamamlanmış servis kaydına kesilebilir.";

        if (TutarHatasi(tutar) is { } hata) return hata;

        var fatura = kayit.Fatura ??= new Fatura { OlusturmaTarihi = simdi };
        fatura.Tutar = tutar;

        // Ödeme tarihi ödendi işaretlendiği an yazılır; tutar düzeltmesi tarihi değiştirmez.
        if (odendiMi && !fatura.OdendiMi)
            fatura.OdemeTarihi = simdi;
        else if (!odendiMi)
            fatura.OdemeTarihi = null;

        fatura.OdendiMi = odendiMi;
        return null;
    }

    // Faturadaki tutar parçaları da kapsar; fatura kesildikten sonra parça listesi değişirse belge tutmaz.
    public static string? ParcaCikarilabilirMi(ServisKaydi kayit) =>
        kayit.Fatura is not null
            ? "Faturası olan kaydın parçaları değiştirilemez; önce faturayı silin."
            : null;

    // İptal edilen işten parça çıkarılıp stoğa iade edilebilir, ama yeni parça eklenemez.
    public static string? ParcaEklenebilirMi(ServisKaydi kayit) =>
        ParcaCikarilabilirMi(kayit)
        ?? (kayit.Durum == ServisDurumu.IptalEdildi ? "İptal edilmiş kayda parça eklenemez." : null);

    public static string? Sil(ServisKaydi kayit, DateTime simdi)
    {
        if (kayit.Fatura is not null)
            return "Faturası olan servis kaydı silinemez; önce faturayı silin.";

        // Parçalar stoktan düşülmüştür; kayıt silinirse stokta karşılığı kalmayan bir eksik oluşur.
        if (kayit.Parcalar.Count > 0)
            return "Parça kullanılmış servis kaydı silinemez; önce parçaları kayıttan çıkarın.";

        kayit.SilinmeTarihi = simdi;
        return null;
    }

    private static string? TutarHatasi(decimal tutar) =>
        tutar <= 0 || decimal.Round(tutar, 2) != tutar
            ? "Tutar sıfırdan büyük olmalı ve en fazla iki ondalık hane içermeli."
            : null;
}
