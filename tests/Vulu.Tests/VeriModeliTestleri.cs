using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Tests;

// Gerçek bir MongoDB ister: VULU_TEST_MONGO ortam değişkeni, yoksa mongodb://localhost:27017.
public class VeriModeliTestleri : IAsyncLifetime
{
    private readonly MongoClient _istemci;
    private readonly string _veritabaniAdi = "vulu_tsys_test_" + Guid.NewGuid().ToString("N");
    private readonly IMongoDatabase _db;
    private readonly MongoBaglami _baglam;

    public VeriModeliTestleri()
    {
        MongoBaglami.KurallariKaydet();
        _istemci = new MongoClient(
            Environment.GetEnvironmentVariable("VULU_TEST_MONGO") ?? "mongodb://localhost:27017");
        _db = _istemci.GetDatabase(_veritabaniAdi);
        _baglam = new MongoBaglami(_db);
    }

    public Task InitializeAsync() => _baglam.IndexleriKurAsync();

    public Task DisposeAsync() => _istemci.DropDatabaseAsync(_veritabaniAdi);

    [Fact]
    public async Task Ayni_kullanici_adi_iki_kez_eklenemez()
    {
        await _baglam.Kullanicilar.InsertOneAsync(new Kullanici { KullaniciAdi = "admin", SifreHash = "x" });

        var hata = await Assert.ThrowsAsync<MongoWriteException>(() =>
            _baglam.Kullanicilar.InsertOneAsync(new Kullanici { KullaniciAdi = "admin", SifreHash = "y" }));

        Assert.Equal(ServerErrorCategory.DuplicateKey, hata.WriteError.Category);
    }

    [Fact]
    public async Task Servis_kaydi_gomulu_faturayla_beklenen_bicimde_saklanir()
    {
        var kayit = new ServisKaydi
        {
            No = Guid.NewGuid().ToString("N"),
            TakipKodu = Guid.NewGuid().ToString("N"),
            MusteriId = ObjectId.GenerateNewId().ToString(),
            Musteri = new MusteriOzeti { AdSoyad = "Ahmet Yılmaz", Telefon = "05551234567" },
            CihazBilgisi = "Laptop",
            Sorun = "Açılmıyor",
            Durum = ServisDurumu.DevamEdiyor,
            Fatura = new Fatura { Tutar = 750.50m }
        };
        await _baglam.ServisKayitlari.InsertOneAsync(kayit);

        var ham = await _db.GetCollection<BsonDocument>("servisKayitlari")
            .Find(new BsonDocument("_id", ObjectId.Parse(kayit.Id))).SingleAsync();

        Assert.Equal(BsonType.ObjectId, ham["musteriId"].BsonType);
        Assert.Equal("DevamEdiyor", ham["durum"].AsString);
        Assert.Equal(BsonType.Decimal128, ham["fatura"]["tutar"].BsonType);
        Assert.False(ham.Contains("teknisyenId"));

        var okunan = await _baglam.ServisKayitlari.Find(s => s.Id == kayit.Id).SingleAsync();
        Assert.Equal(750.50m, okunan.Fatura!.Tutar);
        Assert.Equal("Ahmet Yılmaz", okunan.Musteri.AdSoyad);
    }
}
