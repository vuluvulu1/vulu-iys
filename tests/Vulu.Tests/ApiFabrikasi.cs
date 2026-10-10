using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Tests;

// API'yi gerçek bir MongoDB'de çalıştırır. Her kullanımda yeni bir platform veritabanı ve içinde hazır bir
// işletme açılır; sonunda hepsi silinir. Testlerin çoğu bu varsayılan işletmenin yöneticisi olarak çalışır.
// Adres VULU_TEST_MONGO ortam değişkeninden okunur, yoksa mongodb://localhost:27017 kullanılır.
public class ApiFabrikasi : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Vulu yöneticisi (işletmeleri açan hesap).
    public const string YoneticiAdi = "vulu-yonetici";
    public const string YoneticiSifresi = "Test-Yonetici-Sifresi-1";

    // Varsayılan test işletmesi ve onun yöneticisi.
    public const string IsletmeKodu = "test-isletme";
    public const string AdminAdi = "admin";
    public const string AdminSifresi = "Test-Admin-Sifresi-1";

    public const string PersonelSifresi = "Gecerli-Sifre-1";
    public const string JwtAnahtari = "yalnizca-testlerde-kullanilan-imza-anahtari-0123456789";

    public static string MongoAdresi =>
        Environment.GetEnvironmentVariable("VULU_TEST_MONGO") ?? "mongodb://localhost:27017";

    private readonly string _onek = "vulu_test_" + Guid.NewGuid().ToString("N")[..12] + "_";

    protected virtual int DakikadaGirisDenemesi => 10_000;

    public PlatformBaglami Platform => Services.GetRequiredService<PlatformBaglami>();

    // Varsayılan test işletmesinin veritabanı.
    public MongoBaglami Veritabani { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Mongo:BaglantiDizesi", MongoAdresi);
        builder.UseSetting("Mongo:VeritabaniAdi", _onek + "platform");
        builder.UseSetting("Mongo:IsletmeVeritabaniOneki", _onek + "isletme_");
        builder.UseSetting("Jwt:Anahtar", JwtAnahtari);
        builder.UseSetting("IlkAdmin:KullaniciAdi", YoneticiAdi);
        builder.UseSetting("IlkAdmin:Sifre", YoneticiSifresi);
        builder.UseSetting("GirisSiniri:DakikadaDeneme", DakikadaGirisDenemesi.ToString());
    }

    public async Task InitializeAsync()
    {
        // İlk vulu yöneticisi arka planda oluşturulur; testler başlamadan hazır olmasını bekle.
        var bitis = DateTime.UtcNow.AddSeconds(30);
        while (!await Platform.Yoneticiler.Find(FilterDefinition<PlatformYoneticisi>.Empty).AnyAsync())
        {
            if (DateTime.UtcNow > bitis)
                throw new TimeoutException($"İlk vulu yöneticisi oluşturulmadı. MongoDB çalışıyor mu? ({MongoAdresi})");
            await Task.Delay(100);
        }

        using var yonetici = await YoneticiIstemcisi();
        var isletme = await IsletmeAc(yonetici, IsletmeKodu, "Test İşletmesi", AdminAdi, AdminSifresi);
        var belge = await Platform.Isletmeler.Find(i => i.Id == isletme.Id).SingleAsync();
        Veritabani = Platform.IsletmeBaglami(belge);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        var istemci = Services.GetRequiredService<IMongoClient>();
        var adlar = await (await istemci.ListDatabaseNamesAsync()).ToListAsync();
        foreach (var ad in adlar.Where(a => a.StartsWith(_onek, StringComparison.Ordinal)))
            await istemci.DropDatabaseAsync(ad);
        await base.DisposeAsync();
    }

    public async Task<OturumYaniti> GirisYap(string kullaniciAdi, string sifre, string isletmeKodu = IsletmeKodu)
    {
        using var istemci = CreateClient();
        using var yanit = await istemci.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(isletmeKodu, kullaniciAdi, sifre));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<OturumYaniti>())!;
    }

    // Varsayılan olarak test işletmesinin yöneticisi; başka personel ya da başka işletme için parametre verilir.
    public async Task<HttpClient> YetkiliIstemci(string kullaniciAdi = AdminAdi, string sifre = AdminSifresi,
        string isletmeKodu = IsletmeKodu)
    {
        var oturum = await GirisYap(kullaniciAdi, sifre, isletmeKodu);
        var istemci = CreateClient();
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oturum.ErisimTokeni);
        return istemci;
    }

    public Task<HttpClient> YoneticiIstemcisi() =>
        YetkiliIstemci(YoneticiAdi, YoneticiSifresi, IsletmeKurallari.PlatformKodu);

    public static async Task<IsletmeYaniti> IsletmeAc(HttpClient yonetici, string kod, string ad,
        string adminAdi = AdminAdi, string adminSifresi = AdminSifresi, int personelSiniri = 1000)
    {
        using var yanit = await yonetici.PostAsJsonAsync("api/platform/isletmeler",
            new IsletmeOlusturIstegi(kod, ad, adminAdi, adminSifresi, "İşletme Yöneticisi", personelSiniri));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<IsletmeYaniti>())!;
    }

    // Benzersiz kullanıcı adıyla bir personel hesabı açar.
    public static async Task<KullaniciOzeti> PersonelEkle(HttpClient admin, string adSoyad = "Test Personeli",
        KullaniciRol rol = KullaniciRol.Teknisyen, string? uzmanlik = null)
    {
        var kullaniciAdi = "p" + Guid.NewGuid().ToString("N")[..12];
        using var yanit = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi(kullaniciAdi, PersonelSifresi, rol, adSoyad, null, uzmanlik));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<KullaniciOzeti>())!;
    }
}
