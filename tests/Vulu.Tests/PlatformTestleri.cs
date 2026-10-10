using System.Net;
using System.Net.Http.Json;
using MongoDB.Driver;
using Vulu.Shared;

namespace Vulu.Tests;

public class PlatformTestleri : IClassFixture<ApiFabrikasi>
{
    private readonly ApiFabrikasi _api;

    public PlatformTestleri(ApiFabrikasi api) => _api = api;

    private static string YeniKod() => "plt-" + Guid.NewGuid().ToString("N")[..10];

    private static string Yol(IsletmeYaniti isletme) => $"api/platform/isletmeler/{isletme.Id}";

    private static Task<HttpResponseMessage> Durum(HttpClient yonetici, IsletmeYaniti isletme, bool aktif) =>
        yonetici.PutAsJsonAsync(Yol(isletme),
            new IsletmeDurumIstegi(isletme.Ad, aktif, isletme.Adres!, isletme.Telefon!, isletme.Eposta!));

    private static Task<HttpResponseMessage> PersonelAc(HttpClient admin, string kullaniciAdi) =>
        admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi(kullaniciAdi, ApiFabrikasi.PersonelSifresi, KullaniciRol.Teknisyen, "Kota Personeli"));

    // --- İşletme yöneticisinin şifresini sıfırlama ---

    [Fact]
    public async Task Vulu_yoneticisi_isletme_yoneticisinin_sifresini_sifirlar_ve_eski_oturumu_kapanir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Şifresini Unutan", "sahip", "Eski-Sifre-123");
        var eskiOturum = await _api.GirisYap("sahip", "Eski-Sifre-123", kod);
        using var anonim = _api.CreateClient();

        var yoneticiler = await yonetici.GetFromJsonAsync<List<IsletmeYoneticisiOzeti>>($"{Yol(isletme)}/yoneticiler");
        var sahip = Assert.Single(yoneticiler!);
        using var sifirla = await yonetici.PutAsJsonAsync($"{Yol(isletme)}/yoneticiler/{sahip.Id}/sifre", new SifreSifirlamaIstegi("Yeni-Sifre-456"));

        using var eskiSifre = await anonim.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(kod, "sahip", "Eski-Sifre-123"));
        using var eskiYenileme = await anonim.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(eskiOturum.YenilemeTokeni));
        var yeniOturum = await _api.GirisYap("sahip", "Yeni-Sifre-456", kod);

        Assert.Equal("sahip", sahip.KullaniciAdi);
        Assert.Equal(HttpStatusCode.NoContent, sifirla.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, eskiSifre.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, eskiYenileme.StatusCode);
        Assert.Equal(KullaniciRol.Admin, yeniOturum.Kullanici.Rol);
    }

    [Fact]
    public async Task Yonetici_olmayan_personelin_sifresi_vulu_tarafindan_sifirlanamaz()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Teknisyeni Olan");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin);

        using var sifirla = await yonetici.PutAsJsonAsync($"{Yol(isletme)}/yoneticiler/{teknisyen.Id}/sifre", new SifreSifirlamaIstegi("Yeni-Sifre-456"));
        using var kisaSifre = await yonetici.PutAsJsonAsync($"{Yol(isletme)}/yoneticiler/{teknisyen.Id}/sifre", new SifreSifirlamaIstegi("kisa"));

        Assert.Equal(HttpStatusCode.NotFound, sifirla.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, kisaSifre.StatusCode);
        // Teknisyenin şifresi değişmedi.
        await _api.GirisYap(teknisyen.KullaniciAdi, ApiFabrikasi.PersonelSifresi, kod);
    }

    // --- İşletmeyi kalıcı silme ---

    [Fact]
    public async Task Isletme_ancak_askidayken_ve_kodu_yazilarak_silinir_verisi_de_gider()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Silinecek İşletme");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);
        (await admin.PostAsJsonAsync("api/musteriler", new MusteriIstegi("Silinecek Müşteri", "05551112233", null, null))).EnsureSuccessStatusCode();
        var veritabaniAdi = (await _api.Platform.Isletmeler.Find(i => i.Id == isletme.Id).SingleAsync()).VeritabaniAdi;
        var istemci = _api.Platform.Isletmeler.Database.Client;

        using var acikken = await yonetici.DeleteAsync($"{Yol(isletme)}?kod={kod}");
        (await Durum(yonetici, isletme, aktif: false)).EnsureSuccessStatusCode();
        using var kodsuz = await yonetici.DeleteAsync(Yol(isletme));
        using var yanlisKod = await yonetici.DeleteAsync($"{Yol(isletme)}?kod=baska-kod");
        Assert.Equal(HttpStatusCode.Conflict, acikken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, kodsuz.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, yanlisKod.StatusCode);
        Assert.Contains(veritabaniAdi, await (await istemci.ListDatabaseNamesAsync()).ToListAsync());

        using var sil = await yonetici.DeleteAsync($"{Yol(isletme)}?kod={kod}");

        using var anonim = _api.CreateClient();
        using var giris = await anonim.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(kod, ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi));
        var liste = await yonetici.GetFromJsonAsync<List<IsletmeYaniti>>("api/platform/isletmeler");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, giris.StatusCode);
        Assert.DoesNotContain(liste!, i => i.Id == isletme.Id);
        Assert.DoesNotContain(veritabaniAdi, await (await istemci.ListDatabaseNamesAsync()).ToListAsync());
        // Başka işletmelerin verisine dokunulmadı.
        Assert.Contains(liste!, i => i.Kod == ApiFabrikasi.IsletmeKodu);
    }

    // --- İletişim bilgileri ve personel sayısı ---

    [Fact]
    public async Task Isletme_iletisim_bilgileriyle_acilir_vulu_yoneticisi_sonradan_duzeltir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();

        using var ac = await yonetici.PostAsJsonAsync("api/platform/isletmeler",
            new IsletmeOlusturIstegi(kod, "İletişimli İşletme", " Atatürk Cad. No: 12, Düzce ", "03801112233", "iletisim@ornek.com",
                "admin", "Gecerli-Sifre-1", "Ad Soyad"));
        var isletme = (await ac.Content.ReadFromJsonAsync<IsletmeYaniti>())!;
        using var admin = await _api.YetkiliIstemci("admin", "Gecerli-Sifre-1", kod);
        var isletmeninGordugu = await admin.GetFromJsonAsync<IsletmeYaniti>("api/isletme");

        Assert.Equal(HttpStatusCode.Created, ac.StatusCode);
        Assert.Equal("Atatürk Cad. No: 12, Düzce", isletmeninGordugu!.Adres);
        Assert.Equal("03801112233", isletmeninGordugu.Telefon);
        Assert.Equal("iletisim@ornek.com", isletmeninGordugu.Eposta);

        using var duzelt = await yonetici.PutAsJsonAsync(Yol(isletme),
            new IsletmeDurumIstegi("İletişimli İşletme", true, "Yeni Adres Sok. No: 3, Düzce", "03809998877", "yeni@ornek.com"));
        var listedeki = (await yonetici.GetFromJsonAsync<List<IsletmeYaniti>>("api/platform/isletmeler"))!.Single(i => i.Id == isletme.Id);

        Assert.Equal(HttpStatusCode.OK, duzelt.StatusCode);
        Assert.Equal("Yeni Adres Sok. No: 3, Düzce", listedeki.Adres);
        Assert.Equal("03809998877", listedeki.Telefon);
        Assert.Equal("yeni@ornek.com", listedeki.Eposta);
    }

    [Theory]
    [InlineData("", "03801234567", "bilgi@ornek.com")]
    [InlineData("Test Cad. No: 1, Düzce", "", "bilgi@ornek.com")]
    [InlineData("Test Cad. No: 1, Düzce", "12345", "bilgi@ornek.com")]
    [InlineData("Test Cad. No: 1, Düzce", "03801234567", "")]
    [InlineData("Test Cad. No: 1, Düzce", "03801234567", "eposta-degil")]
    public async Task Iletisim_bilgisi_eksik_ya_da_gecersiz_isletme_acilamaz(string adres, string telefon, string eposta)
    {
        using var yonetici = await _api.YoneticiIstemcisi();

        using var yanit = await yonetici.PostAsJsonAsync("api/platform/isletmeler",
            new IsletmeOlusturIstegi(YeniKod(), "Eksik Bilgili", adres, telefon, eposta, "admin", "Gecerli-Sifre-1", "Ad Soyad"));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Personel_sayisi_sinirsizdir_ve_listede_aktif_hesap_sayisi_gorunur()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Kalabalık İşletme");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);

        for (var i = 0; i < 7; i++)
        {
            using var yanit = await PersonelAc(admin, $"personel{i}");
            Assert.Equal(HttpStatusCode.Created, yanit.StatusCode);
        }

        var listedeki = (await yonetici.GetFromJsonAsync<List<IsletmeYaniti>>("api/platform/isletmeler"))!.Single(i => i.Id == isletme.Id);
        Assert.Equal(8, listedeki.PersonelSayisi);
    }

    // --- Vulu yöneticileri ---

    [Fact]
    public async Task Yeni_vulu_yoneticisi_eklenir_giris_yapar_ve_devre_disi_kalinca_giremez()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var ad = "vy" + Guid.NewGuid().ToString("N")[..10];
        using var anonim = _api.CreateClient();

        using var ekle = await yonetici.PostAsJsonAsync("api/platform/yoneticiler", new VuluYoneticisiOlusturIstegi(ad, "Ikinci-Yonetici-1", "İkinci Yönetici"));
        var eklenen = (await ekle.Content.ReadFromJsonAsync<VuluYoneticisiYaniti>())!;
        using var ayniAd = await yonetici.PostAsJsonAsync("api/platform/yoneticiler", new VuluYoneticisiOlusturIstegi(ad.ToUpperInvariant(), "Ikinci-Yonetici-1", "Aynı Ad"));
        var oturum = await _api.GirisYap(ad, "Ikinci-Yonetici-1", IsletmeKurallari.PlatformKodu);
        using var ikinci = await _api.YetkiliIstemci(ad, "Ikinci-Yonetici-1", IsletmeKurallari.PlatformKodu);
        var gordugu = await ikinci.GetFromJsonAsync<List<IsletmeYaniti>>("api/platform/isletmeler");

        Assert.Equal(HttpStatusCode.Created, ekle.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, ayniAd.StatusCode);
        Assert.Null(oturum.Isletme);
        Assert.Equal("İkinci Yönetici", oturum.Kullanici.AdSoyad);
        Assert.Contains(gordugu!, i => i.Kod == ApiFabrikasi.IsletmeKodu);

        (await yonetici.PutAsJsonAsync($"api/platform/yoneticiler/{eklenen.Id}", new VuluYoneticisiGuncelleIstegi("İkinci Yönetici", false, null))).EnsureSuccessStatusCode();
        using var devreDisi = await anonim.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(IsletmeKurallari.PlatformKodu, ad, "Ikinci-Yonetici-1"));
        using var yenileme = await anonim.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(oturum.YenilemeTokeni));
        Assert.Equal(HttpStatusCode.Forbidden, devreDisi.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, yenileme.StatusCode);

        using var sil = await yonetici.DeleteAsync($"api/platform/yoneticiler/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
    }

    [Fact]
    public async Task Vulu_yoneticisi_kendini_silemez_ve_isletme_personeli_yoneticileri_goremez()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        using var isletmeAdmini = await _api.YetkiliIstemci();
        var ben = (await yonetici.GetFromJsonAsync<List<VuluYoneticisiYaniti>>("api/platform/yoneticiler"))!
            .Single(y => y.KullaniciAdi == ApiFabrikasi.YoneticiAdi);

        using var kendiniSil = await yonetici.DeleteAsync($"api/platform/yoneticiler/{ben.Id}");
        using var personelListeler = await isletmeAdmini.GetAsync("api/platform/yoneticiler");
        using var personelEkler = await isletmeAdmini.PostAsJsonAsync("api/platform/yoneticiler",
            new VuluYoneticisiOlusturIstegi("sizan", "Gecerli-Sifre-1", "Sızan Kişi"));

        Assert.Equal(HttpStatusCode.Conflict, kendiniSil.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, personelListeler.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, personelEkler.StatusCode);
    }
}

// "En az bir aktif vulu yöneticisi kalmalı" kuralı diğer yöneticilere bağlı olduğu için kendi veritabanında çalışır.
public class SonVuluYoneticisiTestleri : IClassFixture<ApiFabrikasi>
{
    private readonly ApiFabrikasi _api;

    public SonVuluYoneticisiTestleri(ApiFabrikasi api) => _api = api;

    [Fact]
    public async Task Son_aktif_vulu_yoneticisi_devre_disi_birakilamaz()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var ben = Assert.Single((await yonetici.GetFromJsonAsync<List<VuluYoneticisiYaniti>>("api/platform/yoneticiler"))!);

        using var yanit = await yonetici.PutAsJsonAsync($"api/platform/yoneticiler/{ben.Id}",
            new VuluYoneticisiGuncelleIstegi(ben.AdSoyad, false, null));

        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
    }
}
