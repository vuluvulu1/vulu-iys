using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vulu.Shared;

namespace Vulu.Tests;

public class KimlikTestleri : IClassFixture<ApiFabrikasi>
{
    private const string Sifre = "Gecerli-Sifre-1";

    private readonly ApiFabrikasi _api;

    public KimlikTestleri(ApiFabrikasi api) => _api = api;

    private static string YeniAd() => "k" + Guid.NewGuid().ToString("N")[..12];

    private async Task<KullaniciOzeti> KullaniciOlustur(HttpClient admin, KullaniciRol rol, string? ad = null)
    {
        using var yanit = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi(ad ?? YeniAd(), Sifre, rol));
        Assert.Equal(HttpStatusCode.Created, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<KullaniciOzeti>())!;
    }

    private static async Task<string?> HataDetayi(HttpResponseMessage yanit)
    {
        var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();
        return govde.GetProperty("detail").GetString();
    }

    [Fact]
    public async Task Dogru_bilgilerle_giris_yapilir_ve_token_calisir()
    {
        var oturum = await _api.GirisYap("  ADMIN ", ApiFabrikasi.AdminSifresi);

        Assert.Equal(KullaniciRol.Admin, oturum.Kullanici.Rol);
        Assert.NotEmpty(oturum.YenilemeTokeni);

        using var istemci = await _api.YetkiliIstemci();
        var ben = await istemci.GetFromJsonAsync<KullaniciOzeti>("api/kimlik/ben");
        Assert.Equal(ApiFabrikasi.AdminAdi, ben!.KullaniciAdi);
    }

    [Fact]
    public async Task Yanlis_sifre_ve_olmayan_kullanici_ayni_yaniti_alir()
    {
        using var istemci = _api.CreateClient();

        using var yanlisSifre = await istemci.PostAsJsonAsync("api/kimlik/giris",
            new GirisIstegi(ApiFabrikasi.AdminAdi, "yanlis-sifre"));
        using var olmayan = await istemci.PostAsJsonAsync("api/kimlik/giris",
            new GirisIstegi("boyle-biri-yok", "yanlis-sifre"));

        Assert.Equal(HttpStatusCode.Unauthorized, yanlisSifre.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, olmayan.StatusCode);
        Assert.Equal(await HataDetayi(yanlisSifre), await HataDetayi(olmayan));
    }

    [Fact]
    public async Task Bos_giris_istegi_400_doner()
    {
        using var istemci = _api.CreateClient();

        using var yanit = await istemci.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi("", ""));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Token_olmadan_korumali_uca_erisilemez()
    {
        using var istemci = _api.CreateClient();

        using var yanit = await istemci.GetAsync("api/kullanicilar");

        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    [Fact]
    public async Task Teknisyen_kullanici_yonetimine_erisemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var teknisyen = await KullaniciOlustur(admin, KullaniciRol.Teknisyen);

        using var istemci = await _api.YetkiliIstemci(teknisyen.KullaniciAdi, Sifre);
        using var yanit = await istemci.GetAsync("api/kullanicilar");

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    [Fact]
    public async Task Yenileme_tokeni_yalnizca_bir_kez_kullanilabilir()
    {
        var oturum = await _api.GirisYap(ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi);
        using var istemci = _api.CreateClient();

        using var ilk = await istemci.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(oturum.YenilemeTokeni));
        using var ikinci = await istemci.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(oturum.YenilemeTokeni));

        Assert.Equal(HttpStatusCode.OK, ilk.StatusCode);
        var yeni = await ilk.Content.ReadFromJsonAsync<OturumYaniti>();
        Assert.NotEqual(oturum.YenilemeTokeni, yeni!.YenilemeTokeni);
        Assert.Equal(HttpStatusCode.Unauthorized, ikinci.StatusCode);
    }

    [Fact]
    public async Task Cikis_yapinca_yenileme_tokeni_gecersiz_olur()
    {
        var oturum = await _api.GirisYap(ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi);
        using var istemci = _api.CreateClient();

        using var cikis = await istemci.PostAsJsonAsync("api/kimlik/cikis", new YenilemeIstegi(oturum.YenilemeTokeni));
        using var yenile = await istemci.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(oturum.YenilemeTokeni));

        Assert.Equal(HttpStatusCode.NoContent, cikis.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, yenile.StatusCode);
    }

    [Fact]
    public async Task Devre_disi_birakilan_kullanici_giris_yapamaz_ve_oturumunu_yenileyemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var kullanici = await KullaniciOlustur(admin, KullaniciRol.Teknisyen);
        var oturum = await _api.GirisYap(kullanici.KullaniciAdi, Sifre);

        using var guncelle = await admin.PutAsJsonAsync($"api/kullanicilar/{kullanici.Id}",
            new KullaniciGuncelleIstegi(kullanici.KullaniciAdi, KullaniciRol.Teknisyen, AktifMi: false, YeniSifre: null));
        Assert.Equal(HttpStatusCode.OK, guncelle.StatusCode);

        using var istemci = _api.CreateClient();
        using var yenile = await istemci.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(oturum.YenilemeTokeni));
        using var giris = await istemci.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(kullanici.KullaniciAdi, Sifre));

        Assert.Equal(HttpStatusCode.Unauthorized, yenile.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, giris.StatusCode);
    }

    [Fact]
    public async Task Ayni_kullanici_adi_harf_buyuklugu_farkli_olsa_da_ikinci_kez_olusturulamaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var ad = YeniAd();
        await KullaniciOlustur(admin, KullaniciRol.Teknisyen, ad);

        using var yanit = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi(ad.ToUpperInvariant(), Sifre, KullaniciRol.Teknisyen));

        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
    }

    [Theory]
    [InlineData("ab", Sifre)]
    [InlineData("gecerli.ad", "kisa")]
    [InlineData("boşluk var", Sifre)]
    public async Task Gecersiz_kullanici_bilgisi_400_doner(string ad, string sifre)
    {
        using var admin = await _api.YetkiliIstemci();

        using var yanit = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi(ad, sifre, KullaniciRol.Teknisyen));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Yonetici_kendi_hesabini_silemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var ben = await admin.GetFromJsonAsync<KullaniciOzeti>("api/kimlik/ben");

        using var yanit = await admin.DeleteAsync($"api/kullanicilar/{ben!.Id}");

        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
    }

    [Fact]
    public async Task Gecersiz_kimlikle_istek_404_doner()
    {
        using var admin = await _api.YetkiliIstemci();

        using var yanit = await admin.DeleteAsync("api/kullanicilar/gecersiz-kimlik");

        Assert.Equal(HttpStatusCode.NotFound, yanit.StatusCode);
    }

    [Fact]
    public async Task Kullanici_silinir_ve_listeden_kalkar()
    {
        using var admin = await _api.YetkiliIstemci();
        var kullanici = await KullaniciOlustur(admin, KullaniciRol.Teknisyen);

        using var sil = await admin.DeleteAsync($"api/kullanicilar/{kullanici.Id}");
        var liste = await admin.GetFromJsonAsync<List<KullaniciOzeti>>("api/kullanicilar");

        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
        Assert.DoesNotContain(liste!, k => k.Id == kullanici.Id);
    }
}

// Son yönetici kuralı veritabanındaki diğer yöneticilere bağlı olduğu için kendi veritabanında çalışır.
public class SonYoneticiTestleri : IClassFixture<ApiFabrikasi>
{
    private readonly ApiFabrikasi _api;

    public SonYoneticiTestleri(ApiFabrikasi api) => _api = api;

    [Fact]
    public async Task Son_aktif_yonetici_yetkisini_birakamaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var ben = await admin.GetFromJsonAsync<KullaniciOzeti>("api/kimlik/ben");

        using var rolDusur = await admin.PutAsJsonAsync($"api/kullanicilar/{ben!.Id}",
            new KullaniciGuncelleIstegi(ben.KullaniciAdi, KullaniciRol.Teknisyen, AktifMi: true, YeniSifre: null));
        using var devreDisi = await admin.PutAsJsonAsync($"api/kullanicilar/{ben.Id}",
            new KullaniciGuncelleIstegi(ben.KullaniciAdi, KullaniciRol.Admin, AktifMi: false, YeniSifre: null));

        Assert.Equal(HttpStatusCode.Conflict, rolDusur.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, devreDisi.StatusCode);
    }
}

public class GirisSiniriTestleri : IClassFixture<GirisSiniriTestleri.SinirliApi>
{
    public class SinirliApi : ApiFabrikasi
    {
        protected override int DakikadaGirisDenemesi => 3;
    }

    private readonly SinirliApi _api;

    public GirisSiniriTestleri(SinirliApi api) => _api = api;

    [Fact]
    public async Task Cok_fazla_giris_denemesi_429_doner()
    {
        using var istemci = _api.CreateClient();
        var istek = new GirisIstegi(ApiFabrikasi.AdminAdi, "yanlis-sifre");

        for (var i = 0; i < 3; i++)
        {
            using var deneme = await istemci.PostAsJsonAsync("api/kimlik/giris", istek);
            Assert.Equal(HttpStatusCode.Unauthorized, deneme.StatusCode);
        }

        using var fazlasi = await istemci.PostAsJsonAsync("api/kimlik/giris", istek);
        Assert.Equal(HttpStatusCode.TooManyRequests, fazlasi.StatusCode);
    }
}
