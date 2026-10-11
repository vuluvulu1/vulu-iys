using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vulu.Shared;

namespace Vulu.Tests;

// Saldırgan gözüyle yazılmış testler: her biri bir saldırı denemesidir ve API'nin onu reddettiğini doğrular.
public class GuvenlikTestleri : IClassFixture<ApiFabrikasi>
{
    private const string Olmayan = "0123456789abcdef01234567";

    private readonly ApiFabrikasi _api;

    public GuvenlikTestleri(ApiFabrikasi api) => _api = api;

    private HttpClient TokenliIstemci(string token)
    {
        var istemci = _api.CreateClient();
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return istemci;
    }

    private static StringContent Json(string govde) => new(govde, Encoding.UTF8, "application/json");

    private static async Task<MusteriYaniti> MusteriEkle(HttpClient istemci, string ad = "Güvenlik Müşterisi")
    {
        using var yanit = await istemci.PostAsJsonAsync("api/musteriler", new MusteriIstegi(ad, "05551112233", null, null));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<MusteriYaniti>())!;
    }

    private static async Task<ServisKaydiYaniti> KayitEkle(HttpClient istemci, string cihaz = "Laptop")
    {
        var musteri = await MusteriEkle(istemci);
        using var yanit = await istemci.PostAsJsonAsync("api/servis-kayitlari", new ServisKaydiIstegi(musteri.Id, null, cihaz, "Arıza"));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!;
    }

    // --- Uç noktaların envanteri ---

    [Fact]
    public void Oturumsuz_erisilebilen_uclar_yalnizca_bilinen_listedir()
    {
        // Yeni bir uç yanlışlıkla herkese açık bırakılırsa bu test kırılır.
        string[] beklenen =
        [
            "GET api/saglik",
            "GET api/saglik/canli",
            "GET takip/{isletmeKodu}",
            "POST api/kimlik/cikis",
            "POST api/kimlik/giris",
            "POST api/kimlik/yenile"
        ];

        var aciklar = _api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(u => u.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            // OpenAPI belgesi yalnızca geliştirme ortamında yayımlanır (testler o ortamda çalışır).
            .Where(u => u.RoutePattern.RawText?.TrimStart('/').StartsWith("openapi", StringComparison.Ordinal) != true)
            .SelectMany(u => (u.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(yontem => $"{yontem} {u.RoutePattern.RawText}"))
            .Order()
            .ToArray();

        Assert.Equal(beklenen, aciklar);
    }

    [Theory]
    [InlineData("GET", "api/musteriler")]
    [InlineData("GET", "api/servis-kayitlari")]
    [InlineData("GET", "api/randevular")]
    [InlineData("GET", "api/parcalar")]
    [InlineData("GET", "api/personel")]
    [InlineData("GET", "api/kullanicilar")]
    [InlineData("GET", "api/isletme")]
    [InlineData("GET", "api/raporlar")]
    [InlineData("GET", "api/ozet")]
    [InlineData("GET", "api/kimlik/ben")]
    [InlineData("GET", "api/platform/isletmeler")]
    [InlineData("GET", "api/platform/yoneticiler")]
    [InlineData("GET", "api/servis-kayitlari/" + Olmayan + "/fatura/pdf")]
    [InlineData("GET", "api/servis-kayitlari/" + Olmayan + "/kabul-formu")]
    [InlineData("POST", "api/kimlik/sifre")]
    public async Task Oturumsuz_istek_401_alir(string yontem, string yol)
    {
        using var anonim = _api.CreateClient();
        using var yanit = await anonim.SendAsync(new HttpRequestMessage(new HttpMethod(yontem), yol));
        Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
    }

    [Theory]
    [InlineData("GET", "api/kullanicilar")]
    [InlineData("POST", "api/kullanicilar")]
    [InlineData("PUT", "api/kullanicilar/" + Olmayan)]
    [InlineData("DELETE", "api/kullanicilar/" + Olmayan)]
    [InlineData("PUT", "api/isletme")]
    [InlineData("GET", "api/raporlar")]
    [InlineData("POST", "api/parcalar")]
    [InlineData("PUT", "api/parcalar/" + Olmayan)]
    [InlineData("DELETE", "api/parcalar/" + Olmayan)]
    [InlineData("POST", "api/parcalar/" + Olmayan + "/stok")]
    [InlineData("DELETE", "api/musteriler/" + Olmayan)]
    [InlineData("DELETE", "api/servis-kayitlari/" + Olmayan)]
    [InlineData("DELETE", "api/servis-kayitlari/" + Olmayan + "/fatura")]
    [InlineData("DELETE", "api/randevular/" + Olmayan)]
    [InlineData("GET", "api/platform/isletmeler")]
    [InlineData("POST", "api/platform/isletmeler")]
    [InlineData("DELETE", "api/platform/isletmeler/" + Olmayan)]
    [InlineData("GET", "api/platform/isletmeler/" + Olmayan + "/yoneticiler")]
    [InlineData("PUT", "api/platform/isletmeler/" + Olmayan + "/yoneticiler/" + Olmayan + "/sifre")]
    [InlineData("GET", "api/platform/yoneticiler")]
    [InlineData("POST", "api/platform/yoneticiler")]
    public async Task Teknisyen_yonetici_uclarina_erisemez(string yontem, string yol)
    {
        using var admin = await _api.YetkiliIstemci();
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin);
        using var usta = await _api.YetkiliIstemci(teknisyen.KullaniciAdi, ApiFabrikasi.PersonelSifresi);

        using var istek = new HttpRequestMessage(new HttpMethod(yontem), yol);
        if (yontem is "POST" or "PUT") istek.Content = Json("{}");
        using var yanit = await usta.SendAsync(istek);

        // Yetki, gövde doğrulamasından önce denetlenir: geçersiz gövdeyle bile 400 değil 403 döner.
        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    [Theory]
    [InlineData("GET", "api/platform/isletmeler")]
    [InlineData("POST", "api/platform/isletmeler")]
    [InlineData("DELETE", "api/platform/isletmeler/" + Olmayan)]
    [InlineData("PUT", "api/platform/isletmeler/" + Olmayan + "/yoneticiler/" + Olmayan + "/sifre")]
    [InlineData("GET", "api/platform/yoneticiler")]
    [InlineData("POST", "api/platform/yoneticiler")]
    public async Task Isletme_yoneticisi_vulu_yonetimine_erisemez(string yontem, string yol)
    {
        using var admin = await _api.YetkiliIstemci();
        using var istek = new HttpRequestMessage(new HttpMethod(yontem), yol);
        if (yontem is "POST" or "PUT") istek.Content = Json("{}");
        using var yanit = await admin.SendAsync(istek);

        Assert.Equal(HttpStatusCode.Forbidden, yanit.StatusCode);
    }

    // --- Sahte ve oynanmış tokenlar ---

    private static string TokenUret(string anahtar, string algoritma, string rol, string? isletmeId, DateTime? bitis = null,
        string yayinci = "vulu-tsys", string hedef = "vulu-tsys-istemci")
    {
        var talepler = new List<Claim> { new("sub", Olmayan), new("name", "saldirgan"), new("role", rol) };
        if (isletmeId is not null) talepler.Add(new Claim("isletme", isletmeId));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = yayinci,
            Audience = hedef,
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = bitis ?? DateTime.UtcNow.AddMinutes(10),
            Subject = new ClaimsIdentity(talepler),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(anahtar)), algoritma)
        });
    }

    [Fact]
    public async Task Baska_anahtarla_imzalanmis_suresi_dolmus_ya_da_yanlis_hedefli_token_reddedilir()
    {
        var oturum = await _api.GirisYap(ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi);
        var isletmeId = oturum.Isletme!.Id;
        const string baskaAnahtar = "saldirganin-kendi-urettigi-imza-anahtari-0123456789";

        string[] sahteler =
        [
            TokenUret(baskaAnahtar, SecurityAlgorithms.HmacSha256, "Admin", isletmeId),
            TokenUret(baskaAnahtar, SecurityAlgorithms.HmacSha256, "Platform", null),
            TokenUret(ApiFabrikasi.JwtAnahtari, SecurityAlgorithms.HmacSha256, "Admin", isletmeId, DateTime.UtcNow.AddMinutes(-5)),
            TokenUret(ApiFabrikasi.JwtAnahtari, SecurityAlgorithms.HmacSha256, "Admin", isletmeId, yayinci: "baska-yayinci"),
            TokenUret(ApiFabrikasi.JwtAnahtari, SecurityAlgorithms.HmacSha256, "Admin", isletmeId, hedef: "baska-hedef"),
            "bu-bir-token-degil",
            ""
        ];

        foreach (var sahte in sahteler)
        {
            using var istemci = TokenliIstemci(sahte);
            using var isletmeUcu = await istemci.GetAsync("api/musteriler");
            using var platformUcu = await istemci.GetAsync("api/platform/isletmeler");

            Assert.Equal(HttpStatusCode.Unauthorized, isletmeUcu.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, platformUcu.StatusCode);
        }
    }

    [Fact]
    public async Task Imzasiz_ve_govdesi_degistirilmis_token_reddedilir()
    {
        var oturum = await _api.GirisYap(ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi);
        var parcalar = oturum.ErisimTokeni.Split('.');

        static string B64(string metin) => Base64UrlEncoder.Encode(metin);
        var govde = Base64UrlEncoder.Decode(parcalar[1]);

        // "alg: none": imza doğrulamasını atlatma denemesi.
        var imzasiz = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{parcalar[1]}.";
        // Rolü Platform yapılmış gövde, eski imzayla.
        var yukseltilmis = $"{parcalar[0]}.{B64(govde.Replace("\"Admin\"", "\"Platform\""))}.{parcalar[2]}";

        using var imzasizIstemci = TokenliIstemci(imzasiz);
        using var yukseltilmisIstemci = TokenliIstemci(yukseltilmis);
        using var ilk = await imzasizIstemci.GetAsync("api/musteriler");
        using var ikinci = await yukseltilmisIstemci.GetAsync("api/platform/isletmeler");

        Assert.Contains("\"Admin\"", govde);
        Assert.Equal(HttpStatusCode.Unauthorized, ilk.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ikinci.StatusCode);
    }

    [Fact]
    public async Task Gecerli_imzali_ama_var_olmayan_hesaba_ait_token_reddedilir()
    {
        // İmza anahtarı sızmış olsa bile, veritabanında karşılığı olmayan bir hesap adına işlem yapılamaz.
        var oturum = await _api.GirisYap(ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi);
        var isletmeToken = TokenUret(ApiFabrikasi.JwtAnahtari, SecurityAlgorithms.HmacSha256, "Admin", oturum.Isletme!.Id);
        var platformToken = TokenUret(ApiFabrikasi.JwtAnahtari, SecurityAlgorithms.HmacSha256, "Platform", null);

        using var isletmeIstemcisi = TokenliIstemci(isletmeToken);
        using var platformIstemcisi = TokenliIstemci(platformToken);
        using var ilk = await isletmeIstemcisi.GetAsync("api/musteriler");
        using var ikinci = await platformIstemcisi.GetAsync("api/platform/isletmeler");

        Assert.Equal(HttpStatusCode.Unauthorized, ilk.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ikinci.StatusCode);
    }

    // --- Yetkisi alınan hesabın açık oturumu ---

    [Fact]
    public async Task Devre_disi_birakilan_personelin_acik_oturumu_hemen_kapanir()
    {
        using var admin = await _api.YetkiliIstemci();
        var personel = await ApiFabrikasi.PersonelEkle(admin);
        using var usta = await _api.YetkiliIstemci(personel.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
        using var once = await usta.GetAsync("api/musteriler");

        using var guncelleme = await admin.PutAsJsonAsync($"api/kullanicilar/{personel.Id}",
            new KullaniciGuncelleIstegi(personel.KullaniciAdi, KullaniciRol.Teknisyen, false, null, personel.AdSoyad));
        guncelleme.EnsureSuccessStatusCode();
        using var sonra = await usta.GetAsync("api/musteriler");

        Assert.Equal(HttpStatusCode.OK, once.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sonra.StatusCode);
    }

    [Fact]
    public async Task Yoneticiligi_alinan_hesabin_eski_tokeni_yonetici_yetkisi_tasimaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var ikinci = await ApiFabrikasi.PersonelEkle(admin, rol: KullaniciRol.Admin);
        using var eskiYonetici = await _api.YetkiliIstemci(ikinci.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
        using var once = await eskiYonetici.GetAsync("api/kullanicilar");

        using var guncelleme = await admin.PutAsJsonAsync($"api/kullanicilar/{ikinci.Id}",
            new KullaniciGuncelleIstegi(ikinci.KullaniciAdi, KullaniciRol.Teknisyen, true, null, ikinci.AdSoyad));
        guncelleme.EnsureSuccessStatusCode();
        using var sonra = await eskiYonetici.GetAsync("api/kullanicilar");

        Assert.Equal(HttpStatusCode.OK, once.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sonra.StatusCode);
    }

    [Fact]
    public async Task Silinen_ya_da_sifresi_degistirilen_personelin_eski_tokeni_reddedilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var silinecek = await ApiFabrikasi.PersonelEkle(admin);
        var sifresiDegisecek = await ApiFabrikasi.PersonelEkle(admin);
        using var silinen = await _api.YetkiliIstemci(silinecek.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
        using var sifreli = await _api.YetkiliIstemci(sifresiDegisecek.KullaniciAdi, ApiFabrikasi.PersonelSifresi);

        using var silme = await admin.DeleteAsync($"api/kullanicilar/{silinecek.Id}");
        using var sifre = await admin.PutAsJsonAsync($"api/kullanicilar/{sifresiDegisecek.Id}",
            new KullaniciGuncelleIstegi(sifresiDegisecek.KullaniciAdi, KullaniciRol.Teknisyen, true, "Yepyeni-Sifre-9", sifresiDegisecek.AdSoyad));
        silme.EnsureSuccessStatusCode();
        sifre.EnsureSuccessStatusCode();

        using var silinenIstegi = await silinen.GetAsync("api/musteriler");
        using var sifreliIstegi = await sifreli.GetAsync("api/musteriler");

        Assert.Equal(HttpStatusCode.Unauthorized, silinenIstegi.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sifreliIstegi.StatusCode);
    }

    [Fact]
    public async Task Devre_disi_birakilan_vulu_yoneticisinin_acik_oturumu_hemen_kapanir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var ad = "vy" + Guid.NewGuid().ToString("N")[..10];
        using var ekleme = await yonetici.PostAsJsonAsync("api/platform/yoneticiler",
            new VuluYoneticisiOlusturIstegi(ad, ApiFabrikasi.PersonelSifresi, "İkinci Yönetici"));
        var eklenen = (await ekleme.Content.ReadFromJsonAsync<VuluYoneticisiYaniti>())!;
        using var ikinci = await _api.YetkiliIstemci(ad, ApiFabrikasi.PersonelSifresi, IsletmeKurallari.PlatformKodu);
        using var once = await ikinci.GetAsync("api/platform/isletmeler");

        using var kapatma = await yonetici.PutAsJsonAsync($"api/platform/yoneticiler/{eklenen.Id}",
            new VuluYoneticisiGuncelleIstegi("İkinci Yönetici", false, null));
        kapatma.EnsureSuccessStatusCode();
        using var sonra = await ikinci.GetAsync("api/platform/isletmeler");

        Assert.Equal(HttpStatusCode.OK, once.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sonra.StatusCode);
    }

    // --- Yenileme tokenı hırsızlığı ---

    [Fact]
    public async Task Kullanilmis_yenileme_tokeni_yeniden_sunulursa_o_hesabin_butun_oturumlari_kapanir()
    {
        using var admin = await _api.YetkiliIstemci();
        var personel = await ApiFabrikasi.PersonelEkle(admin);
        var oturum = await _api.GirisYap(personel.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
        using var istemci = _api.CreateClient();

        // Token bir kez meşru sahibi, bir kez de onu çalan kişi tarafından kullanılır. Hangisinin hangisi olduğu
        // bilinemez; ikisinin de oturumu kapatılır ve gerçek kullanıcı yeniden giriş yapar.
        using var ilk = await istemci.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(oturum.YenilemeTokeni));
        var yenilenen = (await ilk.Content.ReadFromJsonAsync<OturumYaniti>())!;
        using var tekrar = await istemci.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(oturum.YenilemeTokeni));
        using var yenilenenle = await istemci.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(yenilenen.YenilemeTokeni));

        Assert.Equal(HttpStatusCode.OK, ilk.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tekrar.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, yenilenenle.StatusCode);
    }

    // --- Kendi şifresini değiştirme ---

    [Fact]
    public async Task Personel_kendi_sifresini_mevcut_sifresini_bilerek_degistirir()
    {
        using var admin = await _api.YetkiliIstemci();
        var personel = await ApiFabrikasi.PersonelEkle(admin);
        var digerOturum = await _api.GirisYap(personel.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
        using var usta = await _api.YetkiliIstemci(personel.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
        const string yeni = "Kendi-Sectigim-Sifre-7";

        using var yanlisMevcut = await usta.PostAsJsonAsync("api/kimlik/sifre", new SifreDegistirmeIstegi("yanlis-sifre", yeni));
        using var kisa = await usta.PostAsJsonAsync("api/kimlik/sifre", new SifreDegistirmeIstegi(ApiFabrikasi.PersonelSifresi, "kisa"));
        using var ayni = await usta.PostAsJsonAsync("api/kimlik/sifre",
            new SifreDegistirmeIstegi(ApiFabrikasi.PersonelSifresi, ApiFabrikasi.PersonelSifresi));
        using var degistirme = await usta.PostAsJsonAsync("api/kimlik/sifre", new SifreDegistirmeIstegi(ApiFabrikasi.PersonelSifresi, yeni));
        var yeniOturum = await degistirme.Content.ReadFromJsonAsync<OturumYaniti>();

        using var anonim = _api.CreateClient();
        using var eskiSifreyle = await anonim.PostAsJsonAsync("api/kimlik/giris",
            new GirisIstegi(ApiFabrikasi.IsletmeKodu, personel.KullaniciAdi, ApiFabrikasi.PersonelSifresi));
        using var yeniSifreyle = await anonim.PostAsJsonAsync("api/kimlik/giris",
            new GirisIstegi(ApiFabrikasi.IsletmeKodu, personel.KullaniciAdi, yeni));
        // Başka cihazdaki oturum ve bu istemcinin eski tokenı kapanır; yanıttaki yeni oturum çalışır.
        using var digerYenileme = await anonim.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(digerOturum.YenilemeTokeni));
        using var eskiTokenla = await usta.GetAsync("api/musteriler");
        using var yeniIstemci = TokenliIstemci(yeniOturum!.ErisimTokeni);
        using var yeniTokenla = await yeniIstemci.GetAsync("api/musteriler");

        Assert.Equal(HttpStatusCode.BadRequest, yanlisMevcut.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, kisa.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ayni.StatusCode);
        Assert.Equal(HttpStatusCode.OK, degistirme.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, eskiSifreyle.StatusCode);
        Assert.Equal(HttpStatusCode.OK, yeniSifreyle.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, digerYenileme.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, eskiTokenla.StatusCode);
        Assert.Equal(HttpStatusCode.OK, yeniTokenla.StatusCode);
    }

    [Fact]
    public async Task Vulu_yoneticisi_de_kendi_sifresini_degistirir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var ad = "vy" + Guid.NewGuid().ToString("N")[..10];
        using var ekleme = await yonetici.PostAsJsonAsync("api/platform/yoneticiler",
            new VuluYoneticisiOlusturIstegi(ad, ApiFabrikasi.PersonelSifresi, "Şifre Değiştiren"));
        ekleme.EnsureSuccessStatusCode();
        using var ikinci = await _api.YetkiliIstemci(ad, ApiFabrikasi.PersonelSifresi, IsletmeKurallari.PlatformKodu);
        const string yeni = "Vulu-Yeni-Sifre-42";

        using var degistirme = await ikinci.PostAsJsonAsync("api/kimlik/sifre", new SifreDegistirmeIstegi(ApiFabrikasi.PersonelSifresi, yeni));
        using var anonim = _api.CreateClient();
        using var yeniSifreyle = await anonim.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(IsletmeKurallari.PlatformKodu, ad, yeni));

        Assert.Equal(HttpStatusCode.OK, degistirme.StatusCode);
        Assert.Equal(HttpStatusCode.OK, yeniSifreyle.StatusCode);
    }

    [Fact]
    public async Task Bcryptin_kesecegi_kadar_uzun_sifre_kabul_edilmez()
    {
        // bcrypt yalnızca ilk 72 baytı kullanır; daha uzun şifrenin sonu sessizce yok sayılırdı.
        using var admin = await _api.YetkiliIstemci();
        var uzun = new string('a', 73);
        var cokBaytli = new string('ş', 40);

        using var asciiUzun = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi("u" + Guid.NewGuid().ToString("N")[..10], uzun, KullaniciRol.Teknisyen, "Uzun Şifreli"));
        using var baytUzun = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi("u" + Guid.NewGuid().ToString("N")[..10], cokBaytli, KullaniciRol.Teknisyen, "Uzun Şifreli"));
        using var sinirda = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi("u" + Guid.NewGuid().ToString("N")[..10], new string('a', 72), KullaniciRol.Teknisyen, "Sınırdaki"));

        Assert.Equal(HttpStatusCode.BadRequest, asciiUzun.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, baytUzun.StatusCode);
        Assert.Equal(HttpStatusCode.Created, sinirda.StatusCode);
    }

    // --- Enjeksiyon ve bozuk girdi ---

    [Theory]
    [InlineData("{\"isletmeKodu\":{\"$ne\":null},\"kullaniciAdi\":{\"$ne\":null},\"sifre\":{\"$ne\":null}}")]
    [InlineData("{\"isletmeKodu\":\"test-isletme\",\"kullaniciAdi\":{\"$gt\":\"\"},\"sifre\":\"x\"}")]
    [InlineData("{\"isletmeKodu\":[\"test-isletme\"],\"kullaniciAdi\":\"admin\",\"sifre\":\"x\"}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task Sorgu_operatoru_tasiyan_giris_govdesi_400_alir(string govde)
    {
        using var anonim = _api.CreateClient();
        using var yanit = await anonim.PostAsync("api/kimlik/giris", Json(govde));
        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Theory]
    [InlineData(".*")]
    [InlineData("^")]
    [InlineData("((((((((((")]
    [InlineData("(a+)+$")]
    [InlineData("[")]
    [InlineData("\\")]
    [InlineData("$where")]
    [InlineData("'; db.dropDatabase(); //")]
    public async Task Arama_metni_duzenli_ifade_olarak_yorumlanmaz(string arama)
    {
        using var admin = await _api.YetkiliIstemci();
        await MusteriEkle(admin, "Düzenli İfade Kurbanı");
        var sorgu = Uri.EscapeDataString(arama);

        foreach (var yol in new[] { "api/musteriler", "api/servis-kayitlari", "api/randevular", "api/parcalar" })
        {
            using var yanit = await admin.GetAsync($"{yol}?arama={sorgu}");
            Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        }

        // ".*" her şeyle eşleşen bir desen değil, aranan düz metindir.
        var musteriler = await admin.GetFromJsonAsync<Sayfa<MusteriYaniti>>($"api/musteriler?arama={sorgu}");
        Assert.Empty(musteriler!.Ogeler);
    }

    [Theory]
    [InlineData("api/servis-kayitlari?musteriId[$ne]=1")]
    [InlineData("api/servis-kayitlari?musteriId=%7B%22%24ne%22%3Anull%7D")]
    [InlineData("api/randevular?teknisyenId=1%20or%201=1")]
    public async Task Kimlik_suzgecine_operator_yazilamaz(string yol)
    {
        using var admin = await _api.YetkiliIstemci();
        await KayitEkle(admin);

        using var yanit = await admin.GetAsync(yol);

        // Ya istek reddedilir ya da süzgeç yok sayılır; hiçbir durumda sunucu hatası olmaz.
        Assert.True(yanit.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.OK, yanit.StatusCode.ToString());
    }

    [Theory]
    [InlineData("api/musteriler?sayfa=2147483647&boyut=100")]
    [InlineData("api/servis-kayitlari?sayfa=2147483647&boyut=100")]
    [InlineData("api/randevular?sayfa=2147483647&boyut=200")]
    [InlineData("api/parcalar?sayfa=2147483647&boyut=100")]
    [InlineData("api/musteriler?sayfa=-5&boyut=-5")]
    [InlineData("api/ozet?gunBaslangici=9999-12-31T23:59:59Z&ayBaslangici=9999-12-31T23:59:59Z")]
    [InlineData("api/ozet?gunBaslangici=0001-01-01T00:00:01Z&ayBaslangici=0001-01-01T00:00:01Z")]
    [InlineData("api/raporlar?baslangic=0001-01-02T00:00:00Z&bitis=0001-06-01T00:00:00Z")]
    [InlineData("api/raporlar?baslangic=9999-01-01T00:00:00Z&bitis=9999-12-31T23:59:59Z")]
    [InlineData("api/randevular?baslangic=9999-12-31T23:59:59Z&bitis=0001-01-01T00:00:00Z")]
    public async Task Uc_degerler_sunucu_hatasina_yol_acmaz(string yol)
    {
        using var admin = await _api.YetkiliIstemci();
        using var yanit = await admin.GetAsync(yol);

        Assert.True((int)yanit.StatusCode < 500, $"{yol} -> {(int)yanit.StatusCode}");
    }

    [Fact]
    public async Task Asiri_uzak_tarihli_randevu_reddedilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);

        using var yanit = await admin.PostAsJsonAsync("api/randevular",
            new RandevuIstegi(musteri.Id, null, null, new DateTime(9999, 12, 31, 23, 50, 0, DateTimeKind.Utc), 480, null));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Asiri_uzun_alanlar_ve_buyuk_govde_islenmeden_reddedilir()
    {
        using var anonim = _api.CreateClient();
        using var admin = await _api.YetkiliIstemci();
        var uzun = new string('a', 20_000);

        using var uzunGiris = await anonim.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(uzun, uzun, uzun));
        using var uzunYenileme = await anonim.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(uzun));
        using var uzunArama = await admin.GetAsync($"api/musteriler?arama={new string('a', 500)}");
        using var buyukGovde = await anonim.PostAsync("api/kimlik/giris",
            Json("{\"isletmeKodu\":\"" + new string('a', 6 * 1024 * 1024) + "\"}"));

        Assert.Equal(HttpStatusCode.BadRequest, uzunGiris.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, uzunYenileme.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, uzunArama.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, buyukGovde.StatusCode);
    }

    // --- İstekle değiştirilmemesi gereken alanlar ---

    [Fact]
    public async Task Istege_eklenen_fazladan_alanlar_kaydi_degistirmez()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);

        using var kayitYaniti = await admin.PostAsync("api/servis-kayitlari", Json($$"""
            {
              "musteriId": "{{musteri.Id}}", "cihazBilgisi": "Laptop", "sorun": "Arıza",
              "durum": "TeslimEdildi", "no": "SK-1999-00001", "takipKodu": "AAAAAAAAAA", "surum": 99,
              "silinmeTarihi": "2020-01-01T00:00:00Z", "tamamlanmaTarihi": "2020-01-01T00:00:00Z",
              "fatura": { "no": "SAHTE-1", "tutar": 1, "odendiMi": true },
              "parcalar": [ { "parcaId": "{{Olmayan}}", "ad": "Bedava parça", "adet": 5, "birimFiyat": 0 } ],
              "teklif": { "tutar": 1, "durum": "Onaylandi" }
            }
            """));
        var kayit = (await kayitYaniti.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!;

        using var profilYaniti = await admin.PutAsync("api/isletme", Json("""
            { "ad": "Test İşletmesi", "kdvOrani": 20, "logoyuKaldir": false,
              "kod": "baska-kod", "aktifMi": false, "veritabaniAdi": "admin", "id": "0123456789abcdef01234567" }
            """));
        var profil = (await profilYaniti.Content.ReadFromJsonAsync<IsletmeYaniti>())!;

        Assert.Equal(HttpStatusCode.Created, kayitYaniti.StatusCode);
        Assert.Equal(ServisDurumu.Beklemede, kayit.Durum);
        Assert.StartsWith($"SK-{DateTime.UtcNow.AddHours(3).Year}-", kayit.No);
        Assert.NotEqual("AAAAAAAAAA", kayit.TakipKodu);
        Assert.Null(kayit.Fatura);
        Assert.Null(kayit.Teklif);
        Assert.Null(kayit.TamamlanmaTarihi);
        Assert.Empty(kayit.Parcalar);

        Assert.Equal(HttpStatusCode.OK, profilYaniti.StatusCode);
        Assert.Equal(ApiFabrikasi.IsletmeKodu, profil.Kod);
        Assert.True(profil.AktifMi);
    }

    [Fact]
    public async Task Baska_kayda_ait_parca_satiri_cikarilamaz()
    {
        using var admin = await _api.YetkiliIstemci();
        using var parcaYaniti = await admin.PostAsJsonAsync("api/parcalar", new ParcaIstegi("Güvenlik parçası", null, 10m, 0));
        var parca = (await parcaYaniti.Content.ReadFromJsonAsync<ParcaYaniti>())!;
        using var stok = await admin.PostAsJsonAsync($"api/parcalar/{parca.Id}/stok", new StokHareketiIstegi(5, null));
        stok.EnsureSuccessStatusCode();

        var sahibi = await KayitEkle(admin);
        var diger = await KayitEkle(admin);
        using var ekleme = await admin.PostAsJsonAsync($"api/servis-kayitlari/{sahibi.Id}/parcalar", new ParcaKullanimIstegi(parca.Id, 2));
        var satir = (await ekleme.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!.Parcalar[0];

        using var yanlisKayit = await admin.DeleteAsync($"api/servis-kayitlari/{diger.Id}/parcalar/{satir.Id}");
        var stokSonra = await admin.GetFromJsonAsync<ParcaYaniti>($"api/parcalar/{parca.Id}");

        Assert.Equal(HttpStatusCode.NotFound, yanlisKayit.StatusCode);
        Assert.Equal(3, stokSonra!.StokAdedi);
    }

    // --- Betik ekleme (XSS) ---

    [Fact]
    public async Task Takip_sayfasi_kayittaki_html_metnini_etkisiz_gosterir()
    {
        const string zararli = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin, zararli);
        using var anonim = _api.CreateClient();

        using var yanit = await anonim.GetAsync($"takip/{ApiFabrikasi.IsletmeKodu}?kod={kayit.TakipKodu}");
        var sayfa = await yanit.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.DoesNotContain("<img", sayfa);
        Assert.DoesNotContain("<script", sayfa);
        Assert.Contains("&lt;img", sayfa);
        // Tarayıcı betik çalıştırmayı zaten reddeder: sayfanın içerik politikası betiğe izin vermez.
        Assert.Contains("default-src 'none'", yanit.Headers.GetValues("Content-Security-Policy").Single());
    }

    // --- Logo ---

    [Fact]
    public async Task Resim_gibi_gorunen_bozuk_ya_da_devasa_logo_reddedilir()
    {
        using var admin = await _api.YetkiliIstemci();

        // Geçerli PNG imzası, ardından anlamsız veri.
        byte[] bozuk = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat((byte)0x41, 200)];
        // Başlığı 30000x30000 piksel bildiren PNG: açılırsa gigabaytlarca bellek ister.
        byte[] devasa =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x75, 0x30, 0x00, 0x00, 0x75, 0x30,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x9A, 0x1B, 0x3F, 0x5C
        ];

        foreach (var logo in new[] { bozuk, devasa })
        {
            using var yanit = await admin.PutAsJsonAsync("api/isletme", new IsletmeProfilIstegi(
                "Test İşletmesi", null, null, null, null, null, 20, Convert.ToBase64String(logo), "image/png", false));
            Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        }

        // Kayıt bozulmadı: belgeler hâlâ üretilebiliyor.
        var kayit = await KayitEkle(admin);
        using var form = await admin.GetAsync($"api/servis-kayitlari/{kayit.Id}/kabul-formu");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
    }

    // --- Günlüğe ya da hata mesajına düşebilecek gizli alanlar ---

    [Fact]
    public void Sifre_ya_da_token_tasiyan_turler_metne_cevrilince_icerigini_yazmaz()
    {
        // Yeni bir tür şifre ya da token alanı taşıyıp ToString'i ezmezse bu test kırılır.
        var gizliAlanliTurler = typeof(GirisIstegi).Assembly.GetTypes()
            .Where(tur => tur.GetProperties().Any(alan =>
                alan.PropertyType == typeof(string)
                && (alan.Name.Contains("Sifre", StringComparison.Ordinal) || alan.Name.Contains("Token", StringComparison.Ordinal))))
            .ToList();

        Assert.Contains(typeof(GirisIstegi), gizliAlanliTurler);
        Assert.Contains(typeof(OturumYaniti), gizliAlanliTurler);

        foreach (var tur in gizliAlanliTurler)
        {
            var yontem = tur.GetMethod(nameof(ToString), Type.EmptyTypes)!;
            var derleyiciUretti = yontem.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false);
            Assert.False(derleyiciUretti, $"{tur.Name} içeriğini ToString ile yazdırıyor.");
        }

        Assert.DoesNotContain("cok-gizli", new GirisIstegi("isletme", "kullanici", "cok-gizli").ToString());
    }

    // --- Yanıt başlıkları ve hata ayrıntısı ---

    [Fact]
    public async Task Api_yanitlari_onbellege_alinmaz_ve_tur_tahminine_kapalidir()
    {
        using var admin = await _api.YetkiliIstemci();
        using var anonim = _api.CreateClient();

        using var liste = await admin.GetAsync("api/musteriler");
        using var giris = await anonim.PostAsJsonAsync("api/kimlik/giris",
            new GirisIstegi(ApiFabrikasi.IsletmeKodu, ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi));
        using var reddedilen = await anonim.GetAsync("api/musteriler");

        foreach (var yanit in new[] { liste, giris, reddedilen })
        {
            Assert.Equal("nosniff", yanit.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.True(yanit.Headers.CacheControl?.NoStore, "Cache-Control: no-store bekleniyordu");
            Assert.False(yanit.Headers.Contains("Server"));
            Assert.False(yanit.Headers.Contains("X-Powered-By"));
        }
    }

    [Fact]
    public async Task Hata_yanitlari_ic_ayrinti_sizdirmaz()
    {
        using var admin = await _api.YetkiliIstemci();

        using var bozukJson = await admin.PostAsync("api/musteriler", Json("{ bozuk"));
        using var yanlisTur = await admin.PostAsync("api/musteriler", Json("{\"adSoyad\": 5}"));
        using var olmayanYol = await admin.GetAsync("api/boyle-bir-uc-yok");

        foreach (var yanit in new[] { bozukJson, yanlisTur, olmayanYol })
        {
            var govde = await yanit.Content.ReadAsStringAsync();
            Assert.True((int)yanit.StatusCode is 400 or 404);
            Assert.DoesNotContain("Vulu.Api", govde);
            Assert.DoesNotContain("   at ", govde);
            Assert.DoesNotContain("MongoDB", govde);
            Assert.DoesNotContain("System.", govde);
        }
    }
}

// Hesap kilidi: yanlış şifre denemeleri adres sınırına takılmasa da hesap bazında sınırlanır.
public class HesapKilidiTestleri : IClassFixture<HesapKilidiTestleri.KilitliApi>
{
    private const int Deneme = 3;

    public class KilitliApi : ApiFabrikasi
    {
        protected override IReadOnlyDictionary<string, string> EkAyarlar => new Dictionary<string, string>
        {
            ["GirisKilidi:Deneme"] = Deneme.ToString(CultureInfo.InvariantCulture),
            ["GirisKilidi:KilitDakika"] = "5"
        };
    }

    private readonly KilitliApi _api;

    public HesapKilidiTestleri(KilitliApi api) => _api = api;

    private static Task<HttpResponseMessage> Dene(HttpClient istemci, string ad, string sifre, string kod = ApiFabrikasi.IsletmeKodu) =>
        istemci.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(kod, ad, sifre));

    [Fact]
    public async Task Art_arda_yanlis_sifre_hesabi_gecici_olarak_kilitler_baskalarini_etkilemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var kurban = await ApiFabrikasi.PersonelEkle(admin);
        var diger = await ApiFabrikasi.PersonelEkle(admin);
        using var anonim = _api.CreateClient();

        for (var i = 0; i < Deneme; i++)
        {
            using var yanlis = await Dene(anonim, kurban.KullaniciAdi, "yanlis-sifre-" + i);
            Assert.Equal(HttpStatusCode.Unauthorized, yanlis.StatusCode);
        }

        // Kilitliyken doğru şifre de kabul edilmez; aksi halde kilit, şifre denemeyi durdurmazdı.
        using var dogruSifre = await Dene(anonim, kurban.KullaniciAdi.ToUpperInvariant(), ApiFabrikasi.PersonelSifresi);
        using var digerHesap = await Dene(anonim, diger.KullaniciAdi, ApiFabrikasi.PersonelSifresi);

        Assert.Equal(HttpStatusCode.TooManyRequests, dogruSifre.StatusCode);
        Assert.True(dogruSifre.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(HttpStatusCode.OK, digerHesap.StatusCode);
    }

    [Fact]
    public async Task Basarili_giris_yanlis_deneme_sayacini_sifirlar()
    {
        using var admin = await _api.YetkiliIstemci();
        var personel = await ApiFabrikasi.PersonelEkle(admin);
        using var anonim = _api.CreateClient();

        for (var tur = 0; tur < 2; tur++)
        {
            for (var i = 0; i < Deneme - 1; i++)
            {
                using var yanlis = await Dene(anonim, personel.KullaniciAdi, "yanlis-sifre");
                Assert.Equal(HttpStatusCode.Unauthorized, yanlis.StatusCode);
            }

            using var dogru = await Dene(anonim, personel.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
            Assert.Equal(HttpStatusCode.OK, dogru.StatusCode);
        }
    }

    [Fact]
    public async Task Olmayan_hesap_da_var_olan_gibi_kilitlenir()
    {
        // Yalnızca var olan hesaplar kilitlenseydi, kilit yanıtı hesabın varlığını ele verirdi.
        using var anonim = _api.CreateClient();
        var olmayan = "yok" + Guid.NewGuid().ToString("N")[..10];

        for (var i = 0; i < Deneme; i++)
        {
            using var yanlis = await Dene(anonim, olmayan, "yanlis-sifre");
            Assert.Equal(HttpStatusCode.Unauthorized, yanlis.StatusCode);
        }

        using var sonraki = await Dene(anonim, olmayan, "yanlis-sifre");
        using var olmayanIsletme = await Dene(anonim, "admin", "yanlis-sifre", "olmayan-isletme-" + Guid.NewGuid().ToString("N")[..6]);

        Assert.Equal(HttpStatusCode.TooManyRequests, sonraki.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, olmayanIsletme.StatusCode);
    }
}

public class TakipSiniriTestleri : IClassFixture<TakipSiniriTestleri.SinirliApi>
{
    private const int Sinir = 4;

    public class SinirliApi : ApiFabrikasi
    {
        protected override IReadOnlyDictionary<string, string> EkAyarlar => new Dictionary<string, string>
        {
            ["Takip:DakikadaSorgu"] = Sinir.ToString(CultureInfo.InvariantCulture)
        };
    }

    private readonly SinirliApi _api;

    public TakipSiniriTestleri(SinirliApi api) => _api = api;

    [Fact]
    public async Task Takip_kodu_tahmin_denemeleri_sinirlanir()
    {
        using var anonim = _api.CreateClient();
        var durumlar = new List<HttpStatusCode>();

        for (var i = 0; i < Sinir + 2; i++)
        {
            using var yanit = await anonim.GetAsync($"takip/{ApiFabrikasi.IsletmeKodu}?kod=TAHMIN{i:D4}");
            durumlar.Add(yanit.StatusCode);
        }

        Assert.All(durumlar.Take(Sinir), durum => Assert.Equal(HttpStatusCode.NotFound, durum));
        Assert.All(durumlar.Skip(Sinir), durum => Assert.Equal(HttpStatusCode.TooManyRequests, durum));
    }
}

// Ters vekil arkasında istemci adresi: yalnızca vekilin eklediği girdiye güvenilir.
public class VekilTestleri : IClassFixture<VekilTestleri.VekilliApi>, IClassFixture<ApiFabrikasi>
{
    public class VekilliApi : ApiFabrikasi
    {
        protected override IReadOnlyDictionary<string, string> EkAyarlar => new Dictionary<string, string>
        {
            ["Vekil:AtlamaSayisi"] = "1"
        };
    }

    private readonly VekilliApi _vekilli;
    private readonly ApiFabrikasi _vekilsiz;

    public VekilTestleri(VekilliApi vekilli, ApiFabrikasi vekilsiz)
    {
        _vekilli = vekilli;
        _vekilsiz = vekilsiz;
    }

    private static async Task<BaglantiYaniti> Baglanti(HttpClient yonetici, string iletilen)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, "api/saglik/baglanti");
        istek.Headers.Add("X-Forwarded-For", iletilen);
        istek.Headers.Add("X-Forwarded-Proto", "https");
        using var yanit = await yonetici.SendAsync(istek);
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<BaglantiYaniti>())!;
    }

    [Fact]
    public async Task Vekilin_ekledigi_adres_kullanilir_istemcinin_yazdigi_sahte_adres_kullanilmaz()
    {
        using var yonetici = await _vekilli.YoneticiIstemcisi();

        var tek = await Baglanti(yonetici, "203.0.113.7");
        // İstemci başlığa kendi yazdığı adresi koyar; vekil gerçek adresi sona ekler.
        var sahte = await Baglanti(yonetici, "10.0.0.1, 203.0.113.7");

        Assert.Equal("203.0.113.7", tek.IstemciAdresi);
        Assert.True(tek.Https);
        Assert.Equal("203.0.113.7", sahte.IstemciAdresi);
        Assert.Equal(1, sahte.IletilenAdresSayisi);
    }

    [Fact]
    public async Task Vekil_ayari_yoksa_iletilen_adres_basliklari_hic_okunmaz()
    {
        using var yonetici = await _vekilsiz.YoneticiIstemcisi();

        var yanit = await Baglanti(yonetici, "203.0.113.7");

        Assert.NotEqual("203.0.113.7", yanit.IstemciAdresi);
        Assert.False(yanit.Https);
    }

    [Fact]
    public async Task Baglanti_bilgisini_yalnizca_vulu_yoneticisi_gorur()
    {
        using var admin = await _vekilsiz.YetkiliIstemci();
        using var anonim = _vekilsiz.CreateClient();

        using var isletmeYoneticisi = await admin.GetAsync("api/saglik/baglanti");
        using var oturumsuz = await anonim.GetAsync("api/saglik/baglanti");

        Assert.Equal(HttpStatusCode.Forbidden, isletmeYoneticisi.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oturumsuz.StatusCode);
    }
}
