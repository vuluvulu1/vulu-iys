using System.Net;
using System.Net.Http.Json;
using MongoDB.Driver;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Tests;

public class ServisKaydiTestleri : IClassFixture<ApiFabrikasi>
{
    private const string Sifre = "Gecerli-Sifre-1";
    private const string Yol = "api/servis-kayitlari";

    private readonly ApiFabrikasi _api;

    public ServisKaydiTestleri(ApiFabrikasi api) => _api = api;

    private static string Ek() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<T> Oku<T>(HttpResponseMessage yanit, HttpStatusCode beklenen = HttpStatusCode.OK)
    {
        Assert.Equal(beklenen, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<MusteriYaniti> MusteriEkle(HttpClient istemci, string adSoyad = "Servis Müşterisi")
    {
        using var yanit = await istemci.PostAsJsonAsync("api/musteriler", new MusteriIstegi(adSoyad, "05551112233", null, null));
        return await Oku<MusteriYaniti>(yanit, HttpStatusCode.Created);
    }

    private static Task<KullaniciOzeti> TeknisyenEkle(HttpClient admin, string adSoyad = "Servis Teknisyeni") =>
        ApiFabrikasi.PersonelEkle(admin, adSoyad);

    private static async Task<ServisKaydiYaniti> KayitEkle(HttpClient istemci, string musteriId, string? teknisyenId = null,
        string cihaz = "Laptop Dell XPS", string sorun = "Açılmıyor")
    {
        using var yanit = await istemci.PostAsJsonAsync(Yol, new ServisKaydiIstegi(musteriId, teknisyenId, cihaz, sorun));
        return await Oku<ServisKaydiYaniti>(yanit, HttpStatusCode.Created);
    }

    private static async Task<ServisKaydiYaniti> DurumDegistir(HttpClient istemci, string id, ServisDurumu durum)
    {
        using var yanit = await istemci.PutAsJsonAsync($"{Yol}/{id}/durum", new ServisDurumuIstegi(durum));
        return await Oku<ServisKaydiYaniti>(yanit);
    }

    private static Task<Sayfa<ServisKaydiYaniti>?> Listele(HttpClient istemci, string sorgu) =>
        istemci.GetFromJsonAsync<Sayfa<ServisKaydiYaniti>>($"{Yol}?{sorgu}");

    [Fact]
    public async Task Kayit_musteri_ve_teknisyen_bilgisiyle_beklemede_acilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin, "Ali Veli");
        var teknisyen = await TeknisyenEkle(admin, "Usta Hasan");

        var kayit = await KayitEkle(admin, musteri.Id, teknisyen.Id, "  Telefon  ", " Ekran kırık ");
        var okunan = await admin.GetFromJsonAsync<ServisKaydiYaniti>($"{Yol}/{kayit.Id}");

        Assert.Equal(ServisDurumu.Beklemede, okunan!.Durum);
        Assert.Equal("Ali Veli", okunan.MusteriAdi);
        Assert.Equal("05551112233", okunan.MusteriTelefonu);
        Assert.Equal("Usta Hasan", okunan.TeknisyenAdi);
        Assert.Equal("Telefon", okunan.CihazBilgisi);
        Assert.Equal("Ekran kırık", okunan.Sorun);
        Assert.Null(okunan.TamamlanmaTarihi);
        Assert.Null(okunan.Fatura);
    }

    [Fact]
    public async Task Olmayan_musteri_ya_da_teknisyenle_kayit_acilamaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        const string olmayan = "0123456789abcdef01234567";

        using var musterisiz = await admin.PostAsJsonAsync(Yol, new ServisKaydiIstegi(olmayan, null, "Laptop", "Arıza"));
        using var teknisyensiz = await admin.PostAsJsonAsync(Yol, new ServisKaydiIstegi(musteri.Id, olmayan, "Laptop", "Arıza"));
        using var gecersiz = await admin.PostAsJsonAsync(Yol, new ServisKaydiIstegi("kimlik-degil", null, "Laptop", "Arıza"));
        using var bos = await admin.PostAsJsonAsync(Yol, new ServisKaydiIstegi(musteri.Id, null, "", ""));

        Assert.Equal(HttpStatusCode.BadRequest, musterisiz.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, teknisyensiz.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, gecersiz.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bos.StatusCode);
    }

    [Fact]
    public async Task Silinmis_musteriye_kayit_acilamaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        using var sil = await admin.DeleteAsync($"api/musteriler/{musteri.Id}");
        sil.EnsureSuccessStatusCode();

        using var yanit = await admin.PostAsJsonAsync(Yol, new ServisKaydiIstegi(musteri.Id, null, "Laptop", "Arıza"));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Kayit_guncellenir_ve_teknisyen_atamasi_kaldirilabilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        var teknisyen = await TeknisyenEkle(admin);
        var kayit = await KayitEkle(admin, musteri.Id, teknisyen.Id);

        using var yanit = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}",
            new ServisKaydiIstegi(musteri.Id, null, "Masaüstü", "Fan sesi"));
        var guncel = await Oku<ServisKaydiYaniti>(yanit);

        Assert.Null(guncel.TeknisyenId);
        Assert.Null(guncel.TeknisyenAdi);
        Assert.Equal("Masaüstü", guncel.CihazBilgisi);
        Assert.Equal(ServisDurumu.Beklemede, guncel.Durum);
    }

    [Fact]
    public async Task Durum_degisince_tamamlanma_tarihi_yazilir_ve_geri_alininca_silinir()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin, (await MusteriEkle(admin)).Id);

        var tamam = await DurumDegistir(admin, kayit.Id, ServisDurumu.Tamamlandi);
        var geri = await DurumDegistir(admin, kayit.Id, ServisDurumu.DevamEdiyor);

        Assert.NotNull(tamam.TamamlanmaTarihi);
        Assert.Equal(ServisDurumu.DevamEdiyor, geri.Durum);
        Assert.Null(geri.TamamlanmaTarihi);
    }

    [Fact]
    public async Task Fatura_yalnizca_tamamlanmis_kayda_kesilir_ve_odeme_tarihi_izlenir()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin, (await MusteriEkle(admin)).Id);

        using var erken = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/fatura", new FaturaIstegi(750.50m, false));
        Assert.Equal(HttpStatusCode.Conflict, erken.StatusCode);

        await DurumDegistir(admin, kayit.Id, ServisDurumu.Tamamlandi);
        using var kes = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/fatura", new FaturaIstegi(750.50m, false));
        var faturali = await Oku<ServisKaydiYaniti>(kes);
        using var ode = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/fatura", new FaturaIstegi(750.50m, true));
        var odenmis = await Oku<ServisKaydiYaniti>(ode);

        Assert.Equal(750.50m, faturali.Fatura!.Tutar);
        Assert.False(faturali.Fatura.OdendiMi);
        Assert.Null(faturali.Fatura.OdemeTarihi);
        Assert.True(odenmis.Fatura!.OdendiMi);
        Assert.NotNull(odenmis.Fatura.OdemeTarihi);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-10")]
    [InlineData("12.345")]
    [InlineData("99999999")]
    public async Task Gecersiz_fatura_tutari_reddedilir(string tutar)
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin, (await MusteriEkle(admin)).Id);
        await DurumDegistir(admin, kayit.Id, ServisDurumu.Tamamlandi);

        using var yanit = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/fatura",
            new FaturaIstegi(decimal.Parse(tutar, System.Globalization.CultureInfo.InvariantCulture), false));

        Assert.True(yanit.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict, $"Beklenmeyen: {yanit.StatusCode}");
        var okunan = await admin.GetFromJsonAsync<ServisKaydiYaniti>($"{Yol}/{kayit.Id}");
        Assert.Null(okunan!.Fatura);
    }

    [Fact]
    public async Task Faturasi_olan_kaydin_durumu_degismez_kayit_silinmez_fatura_silinince_serbest_kalir()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin, (await MusteriEkle(admin)).Id);
        await DurumDegistir(admin, kayit.Id, ServisDurumu.Tamamlandi);
        using var kes = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/fatura", new FaturaIstegi(100m, true));
        kes.EnsureSuccessStatusCode();

        using var durum = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/durum", new ServisDurumuIstegi(ServisDurumu.IptalEdildi));
        using var sil = await admin.DeleteAsync($"{Yol}/{kayit.Id}");
        Assert.Equal(HttpStatusCode.Conflict, durum.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);

        using var faturaSil = await admin.DeleteAsync($"{Yol}/{kayit.Id}/fatura");
        Assert.Null((await Oku<ServisKaydiYaniti>(faturaSil)).Fatura);
        using var tekrarSil = await admin.DeleteAsync($"{Yol}/{kayit.Id}");
        using var getir = await admin.GetAsync($"{Yol}/{kayit.Id}");

        Assert.Equal(HttpStatusCode.NoContent, tekrarSil.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getir.StatusCode);
        var belge = await _api.Veritabani.ServisKayitlari.Find(s => s.Id == kayit.Id).SingleAsync();
        Assert.NotNull(belge.SilinmeTarihi);
    }

    [Fact]
    public async Task Teknisyen_kayit_acar_durum_degistirir_fatura_keser_ama_silemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var ad = "t" + Guid.NewGuid().ToString("N")[..10];
        using var hesap = await admin.PostAsJsonAsync("api/kullanicilar", new KullaniciOlusturIstegi(ad, Sifre, KullaniciRol.Teknisyen, "Test Personeli"));
        hesap.EnsureSuccessStatusCode();
        using var teknisyen = await _api.YetkiliIstemci(ad, Sifre);

        var kayit = await KayitEkle(teknisyen, (await MusteriEkle(teknisyen)).Id);
        await DurumDegistir(teknisyen, kayit.Id, ServisDurumu.Tamamlandi);
        using var kes = await teknisyen.PutAsJsonAsync($"{Yol}/{kayit.Id}/fatura", new FaturaIstegi(250m, false));
        using var faturaSil = await teknisyen.DeleteAsync($"{Yol}/{kayit.Id}/fatura");
        using var sil = await teknisyen.DeleteAsync($"{Yol}/{kayit.Id}");

        Assert.Equal(HttpStatusCode.OK, kes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, faturaSil.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sil.StatusCode);
    }

    [Fact]
    public async Task Liste_duruma_faturaya_teknisyene_ve_musteriye_gore_suzulur()
    {
        using var admin = await _api.YetkiliIstemci();
        var ek = Ek();
        var musteri = await MusteriEkle(admin);
        var teknisyen = await TeknisyenEkle(admin);
        var bekleyen = await KayitEkle(admin, musteri.Id, cihaz: $"Bekleyen {ek}");
        var faturasiz = await KayitEkle(admin, musteri.Id, teknisyen.Id, cihaz: $"Faturasız {ek}");
        var odenmemis = await KayitEkle(admin, musteri.Id, cihaz: $"Ödenmemiş {ek}");
        var odenmis = await KayitEkle(admin, musteri.Id, cihaz: $"Ödenmiş {ek}");
        foreach (var k in new[] { faturasiz, odenmemis, odenmis })
            await DurumDegistir(admin, k.Id, ServisDurumu.Tamamlandi);
        (await admin.PutAsJsonAsync($"{Yol}/{odenmemis.Id}/fatura", new FaturaIstegi(100m, false))).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"{Yol}/{odenmis.Id}/fatura", new FaturaIstegi(200m, true))).EnsureSuccessStatusCode();

        async Task<string[]> Idler(string sorgu) =>
            (await Listele(admin, $"arama={ek}&{sorgu}"))!.Ogeler.Select(k => k.Id).Order().ToArray();
        string[] Sirali(params ServisKaydiYaniti[] kayitlar) => kayitlar.Select(k => k.Id).Order().ToArray();

        Assert.Equal(Sirali(bekleyen), await Idler("durum=Beklemede"));
        Assert.Equal(Sirali(faturasiz, odenmemis, odenmis), await Idler("durum=Tamamlandi"));
        Assert.Equal(Sirali(odenmemis, odenmis), await Idler("fatura=Faturali"));
        Assert.Equal(Sirali(odenmemis), await Idler("fatura=Odenmemis"));
        Assert.Equal(Sirali(odenmis), await Idler("fatura=Odenmis"));
        Assert.Equal(Sirali(faturasiz), await Idler($"teknisyenId={teknisyen.Id}"));
        Assert.Equal(4, (await Idler($"musteriId={musteri.Id}")).Length);
    }

    [Fact]
    public async Task Gecersiz_suzgec_400_doner()
    {
        using var admin = await _api.YetkiliIstemci();

        using var kimlik = await admin.GetAsync($"{Yol}?musteriId=kimlik-degil");
        using var durum = await admin.GetAsync($"{Yol}?durum=BoyleBirDurumYok");

        Assert.Equal(HttpStatusCode.BadRequest, kimlik.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, durum.StatusCode);
    }

    [Fact]
    public async Task Liste_yeniden_eskiye_siralanir_ve_sayfalanir()
    {
        using var admin = await _api.YetkiliIstemci();
        var ek = Ek();
        var musteri = await MusteriEkle(admin);
        var ilk = await KayitEkle(admin, musteri.Id, cihaz: $"Birinci {ek}");
        var ikinci = await KayitEkle(admin, musteri.Id, cihaz: $"İkinci {ek}");
        var ucuncu = await KayitEkle(admin, musteri.Id, cihaz: $"Üçüncü {ek}");

        var sayfa1 = await Listele(admin, $"arama={ek}&sayfa=1&boyut=2");
        var sayfa2 = await Listele(admin, $"arama={ek}&sayfa=2&boyut=2");

        Assert.Equal(3, sayfa1!.Toplam);
        Assert.Equal([ucuncu.Id, ikinci.Id], sayfa1.Ogeler.Select(k => k.Id));
        Assert.Equal([ilk.Id], sayfa2!.Ogeler.Select(k => k.Id));
    }

    [Fact]
    public async Task Musteri_adi_degisince_kayit_yeni_adla_bulunur_eski_adla_bulunmaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var eski = $"Eskiad {Ek()}";
        var yeni = $"Işıl Yeniad {Ek()}";
        var musteri = await MusteriEkle(admin, eski);
        var kayit = await KayitEkle(admin, musteri.Id);

        using var guncelle = await admin.PutAsJsonAsync($"api/musteriler/{musteri.Id}",
            new MusteriIstegi(yeni, "05559998877", null, null));
        guncelle.EnsureSuccessStatusCode();

        var yeniyle = await Listele(admin, $"arama={Uri.EscapeDataString(yeni.ToUpper(new System.Globalization.CultureInfo("tr-TR")))}");
        var eskiyle = await Listele(admin, $"arama={Uri.EscapeDataString(eski)}");
        var telefonla = await Listele(admin, "arama=05559998877");

        Assert.Equal([kayit.Id], yeniyle!.Ogeler.Select(k => k.Id));
        Assert.Equal(yeni, yeniyle.Ogeler[0].MusteriAdi);
        Assert.Empty(eskiyle!.Ogeler);
        Assert.Contains(telefonla!.Ogeler, k => k.Id == kayit.Id);
    }

    [Fact]
    public async Task Okunduktan_sonra_degisen_kayit_eski_surumle_yazilamaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin, (await MusteriEkle(admin)).Id);
        var koleksiyon = _api.Veritabani.ServisKayitlari;

        // İki kişi aynı kaydı okur; biri yazar, diğeri eski sürümle yazmaya çalışır.
        var birinci = await koleksiyon.Find(s => s.Id == kayit.Id).SingleAsync();
        var ikinci = await koleksiyon.Find(s => s.Id == kayit.Id).SingleAsync();
        await DurumDegistir(admin, kayit.Id, ServisDurumu.DevamEdiyor);

        var guncel = await koleksiyon.Find(s => s.Id == kayit.Id).SingleAsync();
        Assert.Equal(birinci.Surum + 1, guncel.Surum);

        ikinci.Sorun = "Eski kopyadan gelen değişiklik";
        var sonuc = await koleksiyon.ReplaceOneAsync(s => s.Id == ikinci.Id && s.Surum == ikinci.Surum, ikinci);
        Assert.Equal(0, sonuc.MatchedCount);
    }
}
