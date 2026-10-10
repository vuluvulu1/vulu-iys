using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Veri;

public class MongoBaglami
{
    public MongoBaglami(IMongoDatabase db)
    {
        Kullanicilar = db.GetCollection<Kullanici>("kullanicilar");
        Musteriler = db.GetCollection<Musteri>("musteriler");
        Teknisyenler = db.GetCollection<Teknisyen>("teknisyenler");
        ServisKayitlari = db.GetCollection<ServisKaydi>("servisKayitlari");
        Randevular = db.GetCollection<Randevu>("randevular");
        YenilemeTokenlari = db.GetCollection<YenilemeTokeni>("yenilemeTokenlari");
    }

    public IMongoCollection<Kullanici> Kullanicilar { get; }
    public IMongoCollection<Musteri> Musteriler { get; }
    public IMongoCollection<Teknisyen> Teknisyenler { get; }
    public IMongoCollection<ServisKaydi> ServisKayitlari { get; }
    public IMongoCollection<Randevu> Randevular { get; }
    public IMongoCollection<YenilemeTokeni> YenilemeTokenlari { get; }

    // Sınıf eşlemeleri ilk kullanımda donduğu için herhangi bir koleksiyona erişmeden önce çağrılmalı.
    public static void KurallariKaydet()
    {
        var kurallar = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true),
            new IgnoreIfNullConvention(true)
        };

        ConventionRegistry.Register("vulu", kurallar,
            t => t.Namespace == typeof(Kullanici).Namespace);
    }

    public async Task IndexleriKurAsync(CancellationToken iptal = default)
    {
        await Kullanicilar.Indexes.CreateOneAsync(
            new CreateIndexModel<Kullanici>(
                Builders<Kullanici>.IndexKeys.Ascending(k => k.KullaniciAdi),
                new CreateIndexOptions { Unique = true }),
            cancellationToken: iptal);

        await YenilemeTokenlari.Indexes.CreateManyAsync(
            [
                new(Builders<YenilemeTokeni>.IndexKeys.Ascending(t => t.TokenHash),
                    new CreateIndexOptions { Unique = true }),
                new(Builders<YenilemeTokeni>.IndexKeys.Ascending(t => t.KullaniciId)),
                // Süresi dolan tokenları MongoDB kendisi siler.
                new(Builders<YenilemeTokeni>.IndexKeys.Ascending(t => t.SonGecerlilik),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero })
            ],
            iptal);

        // Bir kullanıcı hesabı en fazla bir teknisyene bağlanabilir; hesabı olmayanlar kısıta girmez.
        await Teknisyenler.Indexes.CreateOneAsync(
            new CreateIndexModel<Teknisyen>(
                Builders<Teknisyen>.IndexKeys.Ascending(t => t.KullaniciId),
                new CreateIndexOptions<Teknisyen>
                {
                    Unique = true,
                    PartialFilterExpression = Builders<Teknisyen>.Filter.Type(t => t.KullaniciId, BsonType.ObjectId)
                }),
            cancellationToken: iptal);

        await ServisKayitlari.Indexes.CreateManyAsync(
            [
                new(Builders<ServisKaydi>.IndexKeys.Ascending(s => s.MusteriId)),
                new(Builders<ServisKaydi>.IndexKeys.Ascending(s => s.TeknisyenId)),
                new(Builders<ServisKaydi>.IndexKeys.Ascending(s => s.Durum).Descending(s => s.OlusturmaTarihi))
            ],
            iptal);

        await Randevular.Indexes.CreateManyAsync(
            [
                new(Builders<Randevu>.IndexKeys.Ascending(r => r.RandevuTarihi)),
                new(Builders<Randevu>.IndexKeys.Ascending(r => r.MusteriId)),
                new(Builders<Randevu>.IndexKeys.Ascending(r => r.TeknisyenId)),
                new(Builders<Randevu>.IndexKeys.Ascending(r => r.ServisKaydiId))
            ],
            iptal);
    }
}
