using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Veri;

// Platform veritabanı: işletme listesi, vulu yöneticileri ve yenileme tokenları. İşletmelerin kendi verisi
// burada değil, her işletmenin ayrı veritabanındadır (bkz. MongoBaglami).
public class PlatformBaglami
{
    private readonly IMongoClient _istemci;
    private readonly MongoAyarlari _ayarlar;

    public PlatformBaglami(IMongoClient istemci, IMongoDatabase platform, IOptions<MongoAyarlari> ayarlar)
    {
        _istemci = istemci;
        _ayarlar = ayarlar.Value;

        Isletmeler = platform.GetCollection<Isletme>("isletmeler");
        Yoneticiler = platform.GetCollection<PlatformYoneticisi>("yoneticiler");
        YenilemeTokenlari = platform.GetCollection<YenilemeTokeni>("yenilemeTokenlari");
    }

    public IMongoCollection<Isletme> Isletmeler { get; }
    public IMongoCollection<PlatformYoneticisi> Yoneticiler { get; }
    public IMongoCollection<YenilemeTokeni> YenilemeTokenlari { get; }

    // İşletmenin kendi veritabanına erişim.
    public MongoBaglami IsletmeBaglami(Isletme isletme) => new(_istemci.GetDatabase(isletme.VeritabaniAdi));

    public string YeniVeritabaniAdi(string kod) => _ayarlar.IsletmeVeritabaniOneki + kod.Replace('-', '_');

    // İşletmenin bütün verisini geri dönüşsüz siler.
    public Task IsletmeVeritabaniniSilAsync(Isletme isletme, CancellationToken iptal = default)
    {
        // Kayıttaki ad bozulmuş olsa bile platform veritabanı ve MongoDB'nin kendi veritabanları asla silinmez.
        string[] korunanlar = [_ayarlar.VeritabaniAdi, "admin", "local", "config"];
        if (string.IsNullOrWhiteSpace(isletme.VeritabaniAdi) || korunanlar.Contains(isletme.VeritabaniAdi))
            throw new InvalidOperationException($"'{isletme.VeritabaniAdi}' bir işletme veritabanı değil; silinmedi.");

        return _istemci.DropDatabaseAsync(isletme.VeritabaniAdi, iptal);
    }

    // İşletme veritabanını kullanıma hazırlar: bekleyen veri geçişlerini uygular ve indexleri kurar.
    public async Task IsletmeyiHazirlaAsync(Isletme isletme, ILogger log, CancellationToken iptal = default)
    {
        var baglam = IsletmeBaglami(isletme);
        await Gecisler.CalistirAsync(baglam.Veritabani, log, iptal);
        await baglam.IndexleriKurAsync(iptal);
    }

    public async Task IndexleriKurAsync(CancellationToken iptal = default)
    {
        await Isletmeler.Indexes.CreateOneAsync(
            new CreateIndexModel<Isletme>(
                Builders<Isletme>.IndexKeys.Ascending(i => i.Kod),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: iptal);

        await Yoneticiler.Indexes.CreateOneAsync(
            new CreateIndexModel<PlatformYoneticisi>(
                Builders<PlatformYoneticisi>.IndexKeys.Ascending(y => y.KullaniciAdi),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: iptal);

        await YenilemeTokenlari.Indexes.CreateManyAsync(
            [
                new(Builders<YenilemeTokeni>.IndexKeys.Ascending(t => t.TokenHash),
                    new CreateIndexOptions { Unique = true }),
                new(Builders<YenilemeTokeni>.IndexKeys.Ascending(t => t.KullaniciId)),
                new(Builders<YenilemeTokeni>.IndexKeys.Ascending(t => t.IsletmeId)),
                // Süresi dolan tokenları MongoDB kendisi siler.
                new(Builders<YenilemeTokeni>.IndexKeys.Ascending(t => t.SonGecerlilik),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero })
            ],
            iptal);
    }
}
