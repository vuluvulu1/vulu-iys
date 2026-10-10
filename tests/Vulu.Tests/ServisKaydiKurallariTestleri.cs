using Vulu.Api.Kurallar;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Tests;

public class ServisKaydiKurallariTestleri
{
    private static readonly DateTime Simdi = new(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Sonra = Simdi.AddHours(3);

    private static ServisKaydi Tamamlanmis()
    {
        var kayit = new ServisKaydi();
        ServisKaydiKurallari.DurumuDegistir(kayit, ServisDurumu.Tamamlandi, Simdi);
        return kayit;
    }

    [Fact]
    public void Tamamlaninca_tarih_yazilir_geri_alininca_silinir()
    {
        var kayit = new ServisKaydi();

        Assert.Null(ServisKaydiKurallari.DurumuDegistir(kayit, ServisDurumu.Tamamlandi, Simdi));
        Assert.Equal(Simdi, kayit.TamamlanmaTarihi);

        Assert.Null(ServisKaydiKurallari.DurumuDegistir(kayit, ServisDurumu.DevamEdiyor, Sonra));
        Assert.Equal(ServisDurumu.DevamEdiyor, kayit.Durum);
        Assert.Null(kayit.TamamlanmaTarihi);
    }

    [Fact]
    public void Ayni_duruma_gecmek_tamamlanma_tarihini_degistirmez()
    {
        var kayit = Tamamlanmis();

        Assert.Null(ServisKaydiKurallari.DurumuDegistir(kayit, ServisDurumu.Tamamlandi, Sonra));

        Assert.Equal(Simdi, kayit.TamamlanmaTarihi);
    }

    [Theory]
    [InlineData(ServisDurumu.Beklemede)]
    [InlineData(ServisDurumu.DevamEdiyor)]
    [InlineData(ServisDurumu.IptalEdildi)]
    public void Tamamlanmamis_kayda_fatura_kesilemez(ServisDurumu durum)
    {
        var kayit = new ServisKaydi { Durum = durum };

        Assert.NotNull(ServisKaydiKurallari.FaturayiKaydet(kayit, 100m, false, Simdi));
        Assert.Null(kayit.Fatura);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.555")]
    public void Gecersiz_tutar_reddedilir(string tutar)
    {
        var kayit = Tamamlanmis();

        Assert.NotNull(ServisKaydiKurallari.FaturayiKaydet(kayit, decimal.Parse(tutar, System.Globalization.CultureInfo.InvariantCulture), false, Simdi));
        Assert.Null(kayit.Fatura);
    }

    [Fact]
    public void Odeme_tarihi_odendi_isaretlenince_yazilir_ve_tutar_duzeltmesiyle_degismez()
    {
        var kayit = Tamamlanmis();

        ServisKaydiKurallari.FaturayiKaydet(kayit, 750.50m, false, Simdi);
        Assert.Null(kayit.Fatura!.OdemeTarihi);

        ServisKaydiKurallari.FaturayiKaydet(kayit, 750.50m, true, Sonra);
        Assert.Equal(Sonra, kayit.Fatura.OdemeTarihi);

        ServisKaydiKurallari.FaturayiKaydet(kayit, 800m, true, Sonra.AddDays(1));
        Assert.Equal(Sonra, kayit.Fatura.OdemeTarihi);
        Assert.Equal(800m, kayit.Fatura.Tutar);
        Assert.Equal(Simdi, kayit.Fatura.OlusturmaTarihi);

        ServisKaydiKurallari.FaturayiKaydet(kayit, 800m, false, Sonra.AddDays(2));
        Assert.Null(kayit.Fatura.OdemeTarihi);
        Assert.False(kayit.Fatura.OdendiMi);
    }

    [Fact]
    public void Faturasi_olan_kaydin_durumu_degismez_ve_kayit_silinemez()
    {
        var kayit = Tamamlanmis();
        ServisKaydiKurallari.FaturayiKaydet(kayit, 100m, false, Simdi);

        Assert.NotNull(ServisKaydiKurallari.DurumuDegistir(kayit, ServisDurumu.IptalEdildi, Sonra));
        Assert.NotNull(ServisKaydiKurallari.Sil(kayit, Sonra));

        Assert.Equal(ServisDurumu.Tamamlandi, kayit.Durum);
        Assert.Null(kayit.SilinmeTarihi);
    }
}
