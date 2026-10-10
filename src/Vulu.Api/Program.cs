using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vulu.Api.Veri;

MongoBaglami.KurallariKaydet();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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
builder.Services.AddSingleton<MongoBaglami>();
builder.Services.AddHostedService<IndexKurulumServisi>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
