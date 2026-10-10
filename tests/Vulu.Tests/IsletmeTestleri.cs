using System.Net;
using System.Net.Http.Json;
using System.Text;
using Vulu.Shared;

namespace Vulu.Tests;

public class IsletmeTestleri : IClassFixture<ApiFabrikasi>
{
    // 1x1 şeffaf PNG.
    private const string OrnekPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private readonly ApiFabrikasi _api;

    public IsletmeTestleri(ApiFabrikasi api) => _api = api;

    private static string YeniKod() => "isl-" + Guid.NewGuid().ToString("N")[..10];

    private static async Task<string> MusteriEkle(HttpClient istemci, string adSoyad)
    {
        using var yanit = await istemci.PostAsJsonAsync("api/musteriler", new MusteriIstegi(adSoyad, "05551112233", null, null));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<MusteriYaniti>())!.Id;
    }

    private static IsletmeProfilIstegi Profil(string ad = "Düzce Teknik Servis", string? logo = null, string? logoTuru = null,
        bool logoyuKaldir = false) =>
        new(ad, "Atatürk Cad. No: 12, Düzce", "03801234567", "bilgi@ornek.com", "Düzce", "1234567890", 20, logo, logoTuru, logoyuKaldir);

    [Fact]
    public async Task Vulu_yoneticisi_isletme_acar_ve_isletmenin_yoneticisi_giris_yapar()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();

        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod.ToUpperInvariant().ToLowerInvariant(), "Yeni İşletme", "sahip", "Sahip-Sifresi-1");
        var oturum = await _api.GirisYap("sahip", "Sahip-Sifresi-1", kod);
        var liste = await yonetici.GetFromJsonAsync<List<IsletmeYaniti>>("api/platform/isletmeler");

        Assert.Equal(kod, isletme.Kod);
        Assert.True(isletme.AktifMi);
        Assert.Equal(KullaniciRol.Admin, oturum.Kullanici.Rol);
        Assert.Equal("Yeni İşletme", oturum.Isletme!.Ad);
        Assert.Equal(kod, oturum.Isletme.Kod);
        Assert.Contains(liste!, i => i.Id == isletme.Id);
    }

    [Fact]
    public async Task Vulu_yoneticisinin_oturumu_bir_isletmeye_bagli_degildir()
    {
        var oturum = await _api.GirisYap(ApiFabrikasi.YoneticiAdi, ApiFabrikasi.YoneticiSifresi, IsletmeKurallari.PlatformKodu);

        Assert.Null(oturum.Isletme);
    }

    [Fact]
    public async Task Isletmelerin_verisi_birbirinden_ayridir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kodA = YeniKod();
        var kodB = YeniKod();
        await ApiFabrikasi.IsletmeAc(yonetici, kodA, "A İşletmesi");
        await ApiFabrikasi.IsletmeAc(yonetici, kodB, "B İşletmesi");
        using var a = await _api.YetkiliIstemci(isletmeKodu: kodA);
        using var b = await _api.YetkiliIstemci(isletmeKodu: kodB);

        var musteriId = await MusteriEkle(a, "Yalnızca A'nın Müşterisi");
        var personel = await ApiFabrikasi.PersonelEkle(a, "A'nın Ustası");

        var bMusterileri = await b.GetFromJsonAsync<Sayfa<MusteriYaniti>>("api/musteriler");
        var bPersoneli = await b.GetFromJsonAsync<List<KullaniciOzeti>>("api/kullanicilar");
        using var bMusteriyiOkur = await b.GetAsync($"api/musteriler/{musteriId}");
        using var bKayitAcar = await b.PostAsJsonAsync("api/servis-kayitlari", new ServisKaydiIstegi(musteriId, null, "Laptop", "Arıza"));
        using var bPersoneliAtar = await b.PostAsJsonAsync("api/servis-kayitlari",
            new ServisKaydiIstegi(await MusteriEkle(b, "B'nin Müşterisi"), personel.Id, "Laptop", "Arıza"));

        Assert.DoesNotContain(bMusterileri!.Ogeler, m => m.Id == musteriId);
        Assert.DoesNotContain(bPersoneli!, p => p.Id == personel.Id);
        Assert.Equal(HttpStatusCode.NotFound, bMusteriyiOkur.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bKayitAcar.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, bPersoneliAtar.StatusCode);
    }

    [Fact]
    public async Task Ayni_kullanici_adi_farkli_isletmelerde_ayri_hesaplardir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kodA = YeniKod();
        var kodB = YeniKod();
        await ApiFabrikasi.IsletmeAc(yonetici, kodA, "A İşletmesi", "ortak", "A-Isletmesi-Sifresi-1");
        await ApiFabrikasi.IsletmeAc(yonetici, kodB, "B İşletmesi", "ortak", "B-Isletmesi-Sifresi-1");
        using var istemci = _api.CreateClient();

        var a = await _api.GirisYap("ortak", "A-Isletmesi-Sifresi-1", kodA);
        using var aSifresiyleB = await istemci.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(kodB, "ortak", "A-Isletmesi-Sifresi-1"));

        Assert.Equal(kodA, a.Isletme!.Kod);
        Assert.Equal(HttpStatusCode.Unauthorized, aSifresiyleB.StatusCode);
    }

    [Fact]
    public async Task Olmayan_isletme_kodu_yanlis_sifreyle_ayni_yaniti_alir()
    {
        using var istemci = _api.CreateClient();

        using var olmayanKod = await istemci.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi("boyle-isletme-yok", ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi));
        using var yanlisSifre = await istemci.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(ApiFabrikasi.IsletmeKodu, ApiFabrikasi.AdminAdi, "yanlis"));

        Assert.Equal(HttpStatusCode.Unauthorized, olmayanKod.StatusCode);
        Assert.Equal(await olmayanKod.Content.ReadAsStringAsync().ContinueWith(t => Detay(t.Result)),
            await yanlisSifre.Content.ReadAsStringAsync().ContinueWith(t => Detay(t.Result)));

        static string? Detay(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("detail").GetString();
    }

    [Fact]
    public async Task Askiya_alinan_isletmenin_girisi_acik_oturumu_ve_yenilemesi_reddedilir_acilinca_duzelir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Askıya Alınacak");
        var oturum = await _api.GirisYap(ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi, kod);
        using var personel = await _api.YetkiliIstemci(isletmeKodu: kod);
        using var anonim = _api.CreateClient();
        (await personel.GetAsync("api/musteriler")).EnsureSuccessStatusCode();

        (await yonetici.PutAsJsonAsync($"api/platform/isletmeler/{isletme.Id}", new IsletmeDurumIstegi("Askıya Alınacak", false, 1000))).EnsureSuccessStatusCode();

        using var acikOturum = await personel.GetAsync("api/musteriler");
        using var yeniGiris = await anonim.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(kod, ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi));
        using var yenileme = await anonim.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(oturum.YenilemeTokeni));
        Assert.Equal(HttpStatusCode.Forbidden, acikOturum.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, yeniGiris.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, yenileme.StatusCode);

        (await yonetici.PutAsJsonAsync($"api/platform/isletmeler/{isletme.Id}", new IsletmeDurumIstegi("Yeniden Açıldı", true, 1000))).EnsureSuccessStatusCode();

        using var yenidenAcik = await personel.GetAsync("api/musteriler");
        Assert.Equal(HttpStatusCode.OK, yenidenAcik.StatusCode);
    }

    [Fact]
    public async Task Isletme_personeli_vulu_yonetimine_vulu_yoneticisi_isletme_verisine_erisemez()
    {
        using var isletmeYoneticisi = await _api.YetkiliIstemci();
        using var vuluYoneticisi = await _api.YoneticiIstemcisi();

        using var listele = await isletmeYoneticisi.GetAsync("api/platform/isletmeler");
        using var ac = await isletmeYoneticisi.PostAsJsonAsync("api/platform/isletmeler",
            new IsletmeOlusturIstegi(YeniKod(), "Yetkisiz", "admin", "Gecerli-Sifre-1", "Ad Soyad"));
        using var musteriler = await vuluYoneticisi.GetAsync("api/musteriler");
        using var personel = await vuluYoneticisi.GetAsync("api/kullanicilar");
        using var profil = await vuluYoneticisi.GetAsync("api/isletme");

        Assert.Equal(HttpStatusCode.Forbidden, listele.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ac.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, musteriler.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, personel.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, profil.StatusCode);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("Büyük-Harf")]
    [InlineData("bosluk var")]
    [InlineData("-tireyle-baslar")]
    public async Task Gecersiz_isletme_kodu_400_doner(string kod)
    {
        using var yonetici = await _api.YoneticiIstemcisi();

        using var yanit = await yonetici.PostAsJsonAsync("api/platform/isletmeler",
            new IsletmeOlusturIstegi(kod, "Geçersiz Kod", "admin", "Gecerli-Sifre-1", "Ad Soyad"));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Kullanilan_ve_ayrilmis_isletme_kodu_409_doner()
    {
        using var yonetici = await _api.YoneticiIstemcisi();

        using var ayni = await yonetici.PostAsJsonAsync("api/platform/isletmeler",
            new IsletmeOlusturIstegi(ApiFabrikasi.IsletmeKodu, "Aynı Kod", "admin", "Gecerli-Sifre-1", "Ad Soyad"));
        using var ayrilmis = await yonetici.PostAsJsonAsync("api/platform/isletmeler",
            new IsletmeOlusturIstegi(IsletmeKurallari.PlatformKodu, "Ayrılmış Kod", "admin", "Gecerli-Sifre-1", "Ad Soyad"));

        Assert.Equal(HttpStatusCode.Conflict, ayni.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, ayrilmis.StatusCode);
    }

    [Fact]
    public async Task Isletme_yoneticisi_profilini_ve_logosunu_gunceller_teknisyen_guncelleyemez()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        await ApiFabrikasi.IsletmeAc(yonetici, kod, "İlk Ad");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);
        var teknisyenHesabi = await ApiFabrikasi.PersonelEkle(admin);
        using var teknisyen = await _api.YetkiliIstemci(teknisyenHesabi.KullaniciAdi, ApiFabrikasi.PersonelSifresi, kod);

        using var guncelle = await admin.PutAsJsonAsync("api/isletme", Profil(logo: OrnekPng, logoTuru: "image/png"));
        using var yetkisiz = await teknisyen.PutAsJsonAsync("api/isletme", Profil("Teknisyenin Verdiği Ad"));
        var teknisyeninGordugu = await teknisyen.GetFromJsonAsync<IsletmeYaniti>("api/isletme");

        Assert.Equal(HttpStatusCode.OK, guncelle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, yetkisiz.StatusCode);
        Assert.Equal("Düzce Teknik Servis", teknisyeninGordugu!.Ad);
        Assert.Equal("Atatürk Cad. No: 12, Düzce", teknisyeninGordugu.Adres);
        Assert.Equal("1234567890", teknisyeninGordugu.VergiNo);
        Assert.Equal(OrnekPng, teknisyeninGordugu.LogoVerisi);
        Assert.Equal(kod, teknisyeninGordugu.Kod);

        // Logo gönderilmeyen güncelleme logoyu korur; kaldırma isteği siler.
        (await admin.PutAsJsonAsync("api/isletme", Profil("Yeni Ad"))).EnsureSuccessStatusCode();
        Assert.Equal(OrnekPng, (await admin.GetFromJsonAsync<IsletmeYaniti>("api/isletme"))!.LogoVerisi);
        (await admin.PutAsJsonAsync("api/isletme", Profil("Yeni Ad", logoyuKaldir: true))).EnsureSuccessStatusCode();
        Assert.Null((await admin.GetFromJsonAsync<IsletmeYaniti>("api/isletme"))!.LogoVerisi);
    }

    [Fact]
    public async Task Resim_olmayan_ya_da_cok_buyuk_logo_reddedilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var metin = Convert.ToBase64String(Encoding.UTF8.GetBytes("<script>alert(1)</script>"));
        var buyuk = Convert.ToBase64String(new byte[IsletmeKurallari.EnFazlaLogoBayt + 1]);

        using var resimDegil = await admin.PutAsJsonAsync("api/isletme", Profil(logo: metin, logoTuru: "image/png"));
        using var yanlisTur = await admin.PutAsJsonAsync("api/isletme", Profil(logo: OrnekPng, logoTuru: "image/svg+xml"));
        using var cokBuyuk = await admin.PutAsJsonAsync("api/isletme", Profil(logo: buyuk, logoTuru: "image/png"));
        using var bozuk = await admin.PutAsJsonAsync("api/isletme", Profil(logo: "base64-degil!!", logoTuru: "image/png"));

        Assert.All(new[] { resimDegil, yanlisTur, cokBuyuk, bozuk },
            yanit => Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode));
    }

    [Fact]
    public async Task Fatura_isletmenin_kendi_numara_sirasini_ve_on_ekini_kullanir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = "duzce-" + Guid.NewGuid().ToString("N")[..6];
        await ApiFabrikasi.IsletmeAc(yonetici, kod, "Düzce Servis");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);

        using var kayitYaniti = await admin.PostAsJsonAsync("api/servis-kayitlari",
            new ServisKaydiIstegi(await MusteriEkle(admin, "Fatura Müşterisi"), null, "Laptop", "Arıza"));
        var kayit = (await kayitYaniti.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!;
        (await admin.PutAsJsonAsync($"api/servis-kayitlari/{kayit.Id}/durum", new ServisDurumuIstegi(ServisDurumu.Tamamlandi))).EnsureSuccessStatusCode();
        using var faturaYaniti = await admin.PutAsJsonAsync($"api/servis-kayitlari/{kayit.Id}/fatura", new FaturaIstegi(500m, false));
        var faturali = (await faturaYaniti.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!;
        using var pdf = await admin.GetAsync($"api/servis-kayitlari/{kayit.Id}/fatura/pdf");

        // Yeni işletmenin ilk faturası, başka işletmelerin kestiklerinden bağımsız olarak 1 numaradır.
        Assert.Matches(@"^DUZC-\d{4}-000001$", faturali.Fatura!.No);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
    }
}
