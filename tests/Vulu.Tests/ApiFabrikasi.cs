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

// API'yi gerçek bir MongoDB'de, her kullanımda yeni açılıp sonunda silinen bir veritabanıyla çalıştırır.
// Adres VULU_TEST_MONGO ortam değişkeninden okunur, yoksa mongodb://localhost:27017 kullanılır.
public class ApiFabrikasi : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminAdi = "admin";
    public const string AdminSifresi = "Test-Admin-Sifresi-1";
    public const string JwtAnahtari = "yalnizca-testlerde-kullanilan-imza-anahtari-0123456789";

    public static string MongoAdresi =>
        Environment.GetEnvironmentVariable("VULU_TEST_MONGO") ?? "mongodb://localhost:27017";

    private readonly string _veritabaniAdi = "vulu_tsys_test_" + Guid.NewGuid().ToString("N");

    protected virtual int DakikadaGirisDenemesi => 10_000;

    public MongoBaglami Veritabani => Services.GetRequiredService<MongoBaglami>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Mongo:BaglantiDizesi", MongoAdresi);
        builder.UseSetting("Mongo:VeritabaniAdi", _veritabaniAdi);
        builder.UseSetting("Jwt:Anahtar", JwtAnahtari);
        builder.UseSetting("IlkAdmin:KullaniciAdi", AdminAdi);
        builder.UseSetting("IlkAdmin:Sifre", AdminSifresi);
        builder.UseSetting("GirisSiniri:DakikadaDeneme", DakikadaGirisDenemesi.ToString());
    }

    public async Task InitializeAsync()
    {
        // İlk yönetici arka planda oluşturulur; testler başlamadan hazır olmasını bekle.
        var bitis = DateTime.UtcNow.AddSeconds(20);
        while (!await Veritabani.Kullanicilar.Find(FilterDefinition<Kullanici>.Empty).AnyAsync())
        {
            if (DateTime.UtcNow > bitis)
                throw new TimeoutException($"İlk yönetici oluşturulmadı. MongoDB çalışıyor mu? ({MongoAdresi})");
            await Task.Delay(100);
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Services.GetRequiredService<IMongoClient>().DropDatabaseAsync(_veritabaniAdi);
        await base.DisposeAsync();
    }

    public async Task<OturumYaniti> GirisYap(string kullaniciAdi, string sifre)
    {
        using var istemci = CreateClient();
        using var yanit = await istemci.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(kullaniciAdi, sifre));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<OturumYaniti>())!;
    }

    public async Task<HttpClient> YetkiliIstemci(string kullaniciAdi = AdminAdi, string sifre = AdminSifresi)
    {
        var oturum = await GirisYap(kullaniciAdi, sifre);
        var istemci = CreateClient();
        istemci.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", oturum.ErisimTokeni);
        return istemci;
    }
}
