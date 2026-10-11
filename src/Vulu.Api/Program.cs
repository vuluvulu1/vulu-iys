using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
    new MongoClient(sp.GetRequiredService<IOptions<MongoAyarlari>>().Value.BaglantiDizesi));
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
        .Build());

builder.Services.AddRateLimiter(secenekler =>
{
    secenekler.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    secenekler.AddPolicy(KimlikController.GirisSiniriPolitikasi, baglam =>
        RateLimitPartition.GetFixedWindowLimiter(
            baglam.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = baglam.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue("GirisSiniri:DakikadaDeneme", 10),
                Window = TimeSpan.FromMinutes(1)
            }));

    // Takip sayfası herkese açıktır; kod tahmin etme denemeleri adres başına sınırlanır.
    secenekler.AddPolicy(TakipController.SinirPolitikasi, baglam =>
        RateLimitPartition.GetFixedWindowLimiter(
            baglam.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = baglam.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue("Takip:DakikadaSorgu", 20),
                Window = TimeSpan.FromMinutes(1)
            }));
});

var app = builder.Build();

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

app.UseAuthentication();
app.UseMiddleware<IsletmeCozucu>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program;
