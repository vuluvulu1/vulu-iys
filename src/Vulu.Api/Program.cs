using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Controllers;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri;

MongoBaglami.KurallariKaydet();

var builder = WebApplication.CreateBuilder(args);

// Makineye özel gizli ayarlar (git'e girmez). appsettings dosyalarından sonra, ortam değişkenlerinden önce okunur.
var kaynaklar = builder.Configuration.Sources;
var sonJson = kaynaklar.Select((kaynak, sira) => kaynak is JsonConfigurationSource ? sira : -1).Max();
kaynaklar.Insert(sonJson + 1, new JsonConfigurationSource { Path = "appsettings.Local.json", Optional = true });

// ASP.NET'in çerez ve form şifreleme anahtarları (Data Protection) bu API'de kullanılmaz: giriş, kendi anahtarıyla
// imzalanan tokenla yapılır. Anahtarların kapsayıcıyla birlikte kaybolacağını söyleyen uyarılar bu yüzden gizlenir.
builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteOptions>(o => o.ConstraintMap.Add(ObjectIdKisiti.Ad, typeof(ObjectIdKisiti)));

// MongoDB
builder.Services.AddOptions<MongoAyarlari>()
    .Bind(builder.Configuration.GetSection(MongoAyarlari.Bolum))
    .Validate(a => !string.IsNullOrWhiteSpace(a.BaglantiDizesi), "Mongo:BaglantiDizesi ayarlanmamış.")
    .Validate(a => !string.IsNullOrWhiteSpace(a.VeritabaniAdi), "Mongo:VeritabaniAdi ayarlanmamış.")
    .ValidateOnStart();

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var ayarlar = MongoClientSettings.FromConnectionString(sp.GetRequiredService<IOptions<MongoAyarlari>>().Value.BaglantiDizesi);

    // Barındırma ağı boşta kalan bağlantıları birkaç dakika sonra sessizce kapatır; havuzda kalan böyle bir
    // bağlantıya giden sorgu cevapsız kalır ve istek dakikalarca asılı kalırdı. Bağlantılar bir dakika boşta
    // kalınca bırakılır, bir sorgu da en fazla 30 saniye bekler (hata döner, kullanıcı yeniden dener).
    ayarlar.MaxConnectionIdleTime = TimeSpan.FromMinutes(1);
    ayarlar.SocketTimeout = TimeSpan.FromSeconds(30);
    ayarlar.ConnectTimeout = TimeSpan.FromSeconds(10);
    ayarlar.ServerSelectionTimeout = TimeSpan.FromSeconds(15);
    return new MongoClient(ayarlar);
});
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>()
        .GetDatabase(sp.GetRequiredService<IOptions<MongoAyarlari>>().Value.VeritabaniAdi));
builder.Services.AddSingleton<PlatformBaglami>();
builder.Services.AddHostedService<VeritabaniHazirlamaServisi>();

// Çok işletmeli yapı: her istek oturumdaki işletmenin veritabanıyla çalışır.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<GecerliIsletme>();
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<PlatformBaglami>().IsletmeBaglami(sp.GetRequiredService<GecerliIsletme>().Zorunlu));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IslemGunlugu>();

// Fatura belgesi
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Kimlik doğrulama
builder.Services.AddOptions<JwtAyarlari>()
    .Bind(builder.Configuration.GetSection(JwtAyarlari.Bolum))
    .Validate(a => a.Anahtar.Length >= JwtAyarlari.EnAzAnahtarUzunlugu,
        $"Jwt:Anahtar en az {JwtAyarlari.EnAzAnahtarUzunlugu} karakter olmalı (appsettings.Local.json veya ortam değişkeni ile verin).")
    .ValidateOnStart();
builder.Services.Configure<IlkAdminAyarlari>(builder.Configuration.GetSection(IlkAdminAyarlari.Bolum));
builder.Services.AddSingleton<TokenServisi>();
builder.Services.Configure<GirisKilidiAyarlari>(builder.Configuration.GetSection(GirisKilidiAyarlari.Bolum));
builder.Services.AddSingleton<GirisKilidi>();
builder.Services.AddSingleton<Oturumlar>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtAyarlari>>((secenekler, jwt) =>
    {
        secenekler.MapInboundClaims = false;
        secenekler.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Yayinci,
            ValidAudience = jwt.Value.Hedef,
            IssuerSigningKey = TokenServisi.ImzaAnahtari(jwt.Value),
            // Yalnızca bu API'nin kullandığı imza algoritması kabul edilir.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = TokenServisi.AdTalebi,
            RoleClaimType = TokenServisi.RolTalebi
        };
    });

// Açıkça [AllowAnonymous] denmeyen her uç nokta bir işletmeye bağlı oturum ister. Vulu yöneticisinin
// uçları kendi rol şartını taşır; onun tokenında işletme bulunmadığı için işletme uçlarına giremez.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(TokenServisi.IsletmeTalebi)
        .Build())
    .YetkileriEkle();

builder.Services.AddRateLimiter(secenekler =>
{
    secenekler.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Şifre denemeleri (giriş ve şifre değiştirme) adres başına sınırlanır; hesap başına sınır için bkz. GirisKilidi.
    AdresBasinaSinir(secenekler, KimlikController.GirisSiniriPolitikasi, "GirisSiniri:DakikadaDeneme", 10);

    // Takip sayfası herkese açıktır; kod tahmin etme denemeleri adres başına sınırlanır.
    AdresBasinaSinir(secenekler, TakipController.SinirPolitikasi, "Takip:DakikadaSorgu", 20);
});

// Sunucunun adı ve sürümü yanıtlarda duyurulmaz; istek gövdesi sınırlıdır.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = GuvenlikOnlemleri.EnFazlaGovdeBayt;
});

var app = builder.Build();

// API bir ters vekilin (Render vb.) arkasındaysa istemcinin gerçek adresi ve HTTPS bilgisi X-Forwarded-*
// başlıklarından okunur. Bu başlıkları istemci de yazabildiği için yalnızca vekilin eklediği son
// Vekil:AtlamaSayisi kadar girdiye güvenilir; değer, API'nin önündeki vekil sayısıdır. Ayarlanmamışsa başlıklar
// hiç okunmaz. Adres sınırlarının doğru çalıştığı api/platform/baglanti ucundan denetlenebilir.
var vekilSayisi = app.Configuration.GetValue("Vekil:AtlamaSayisi", 0);
if (vekilSayisi > 0)
{
    var vekil = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = vekilSayisi
    };
    // Vekilin adresi barındırma ortamında sabit değildir; güven, adres yerine atlama sayısıyla sınırlanır.
    vekil.KnownIPNetworks.Clear();
    vekil.KnownProxies.Clear();
    app.UseForwardedHeaders(vekil);
}

app.UseMiddleware<GuvenlikOnlemleri>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<OturumDogrulayici>();
app.UseAuthorization();

app.MapControllers();

app.Run();

static void AdresBasinaSinir(RateLimiterOptions secenekler, string politika, string ayar, int varsayilan) =>
    secenekler.AddPolicy(politika, baglam =>
        RateLimitPartition.GetFixedWindowLimiter(
            baglam.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = baglam.RequestServices.GetRequiredService<IConfiguration>().GetValue(ayar, varsayilan),
                Window = TimeSpan.FromMinutes(1)
            }));

public partial class Program;
