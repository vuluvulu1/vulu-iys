using System.Globalization;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kurallar;

namespace Vulu.Api.Veri;

// Veri modeli değiştiğinde mevcut belgeleri yeni biçime taşıyan adımlar. Her adım bir kez çalışır;
// uygulanan son adımın numarası "sema" koleksiyonunda tutulur. Adımlar yarıda kesilip yeniden
// çalıştırılabilecek şekilde yazılır ve ham belgeler üzerinde çalışır (sınıflar artık eski alanları tanımaz).
public static class Gecisler
{
    private static readonly (int Surum, string Ad, Func<IMongoDatabase, CancellationToken, Task> Uygula)[] Adimlar =
    [
        (1, "Teknisyenleri kullanıcı hesaplarıyla birleştir", TeknisyenleriKullanicilarlaBirlestir),
        (2, "Servis kayıtlarına kayıt numarası ve takip kodu ver", KayitNumaralariniVer)
    ];

    public static async Task CalistirAsync(IMongoDatabase db, ILogger log, CancellationToken iptal = default)
    {
        var sema = db.GetCollection<BsonDocument>("sema");
        var surumFiltresi = Builders<BsonDocument>.Filter.Eq("_id", "surum");
        var kayit = await sema.Find(surumFiltresi).FirstOrDefaultAsync(iptal);
        var mevcut = kayit?["deger"].ToInt32() ?? 0;

        foreach (var adim in Adimlar.Where(a => a.Surum > mevcut).OrderBy(a => a.Surum))
        {
            log.LogInformation("Veri geçişi {Surum} uygulanıyor: {Ad}", adim.Surum, adim.Ad);
            await adim.Uygula(db, iptal);
            await sema.UpdateOneAsync(surumFiltresi, Builders<BsonDocument>.Update.Set("deger", adim.Surum),
                new UpdateOptions { IsUpsert = true }, iptal);
        }
    }

    // Eskiden teknisyenler ayrı bir koleksiyondaydı ve isteğe bağlı olarak bir kullanıcı hesabına bağlanıyordu.
    // Artık her personel bir kullanıcı hesabıdır: ad, telefon ve uzmanlık hesaba taşınır, servis kayıtları ve
    // randevulardaki teknisyen kimliği hesabın kimliğiyle değiştirilir.
    private static async Task TeknisyenleriKullanicilarlaBirlestir(IMongoDatabase db, CancellationToken iptal)
    {
        var f = Builders<BsonDocument>.Filter;
        var u = Builders<BsonDocument>.Update;
        var kullanicilar = db.GetCollection<BsonDocument>("kullanicilar");
        var teknisyenler = db.GetCollection<BsonDocument>("teknisyenler");
        var isKoleksiyonlari = new[] { db.GetCollection<BsonDocument>("servisKayitlari"), db.GetCollection<BsonDocument>("randevular") };

        // Hesaba bağlı olmayan teknisyenlerin de artık bir hesabı olmalı, ama şifresini kimse bilmiyor:
        // hesap devre dışı açılır, yönetici şifre verip etkinleştirir.
        foreach (var teknisyen in await teknisyenler.Find(f.Ne("tasindi", true)).ToListAsync(iptal))
        {
            var eskiId = teknisyen["_id"];
            BsonValue? hedefId = null;

            if (!teknisyen.Contains("silinmeTarihi"))
            {
                var profil = u.Set("adSoyad", teknisyen["adSoyad"]);
                foreach (var alan in new[] { "telefon", "uzmanlik" })
                    if (teknisyen.Contains(alan)) profil = profil.Set(alan, teknisyen[alan]);

                var bagliHesap = teknisyen.Contains("kullaniciId")
                    ? await kullanicilar.Find(f.Eq("_id", teknisyen["kullaniciId"])).FirstOrDefaultAsync(iptal)
                    : null;

                if (bagliHesap is not null)
                {
                    hedefId = bagliHesap["_id"];
                    await kullanicilar.UpdateOneAsync(f.Eq("_id", hedefId), profil, cancellationToken: iptal);
                }
                else
                {
                    var hesap = new BsonDocument
                    {
                        ["_id"] = ObjectId.GenerateNewId(),
                        ["kullaniciAdi"] = await BosKullaniciAdiBul(kullanicilar, teknisyen["adSoyad"].AsString, iptal),
                        ["sifreHash"] = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
                        ["rol"] = "Teknisyen",
                        ["aktifMi"] = false,
                        ["olusturmaTarihi"] = DateTime.UtcNow
                    };
                    await kullanicilar.InsertOneAsync(hesap, cancellationToken: iptal);
                    hedefId = hesap["_id"];
                    await kullanicilar.UpdateOneAsync(f.Eq("_id", hedefId), profil, cancellationToken: iptal);
                }
            }

            // Silinmiş teknisyenin kayıtlarında ad kopyası kalır, kimlik bağlantısı kaldırılır.
            var atama = hedefId is null ? u.Unset("teknisyenId") : u.Set("teknisyenId", hedefId);
            foreach (var koleksiyon in isKoleksiyonlari)
                await koleksiyon.UpdateManyAsync(f.Eq("teknisyenId", eskiId), atama.Inc("surum", 1), cancellationToken: iptal);

            await teknisyenler.UpdateOneAsync(f.Eq("_id", eskiId), u.Set("tasindi", true), cancellationToken: iptal);
        }

        // Ad soyadı olmayan eski hesaplar listede boş görünmesin diye kullanıcı adını alır.
        foreach (var hesap in await kullanicilar.Find(f.Exists("adSoyad", false)).ToListAsync(iptal))
            await kullanicilar.UpdateOneAsync(f.Eq("_id", hesap["_id"]), u.Set("adSoyad", hesap["kullaniciAdi"]), cancellationToken: iptal);

        // Eski koleksiyon silinmez, yedek olarak yeniden adlandırılır.
        var adlar = await (await db.ListCollectionNamesAsync(cancellationToken: iptal)).ToListAsync(iptal);
        if (adlar.Contains("teknisyenler"))
            await db.RenameCollectionAsync("teknisyenler", "teknisyenler_eski",
                new RenameCollectionOptions { DropTarget = true }, iptal);
    }

    // Kayıt numarası ve takip kodu sonradan eklendi. Eski kayıtlar açılış sırasına göre numaralanır; sayaç yeni
    // kayıtların kullandığı sayaçtır (bkz. ServisKayitlariController.Ekle), böylece numaralar kaldığı yerden sürer.
    private static async Task KayitNumaralariniVer(IMongoDatabase db, CancellationToken iptal)
    {
        var f = Builders<BsonDocument>.Filter;
        var u = Builders<BsonDocument>.Update;
        var kayitlar = db.GetCollection<BsonDocument>("servisKayitlari");
        var baglam = new MongoBaglami(db);

        var numarasizlar = await kayitlar.Find(f.Exists("no", false) | f.Eq("no", ""))
            .Sort(Builders<BsonDocument>.Sort.Ascending("olusturmaTarihi"))
            .Project(Builders<BsonDocument>.Projection.Include("olusturmaTarihi"))
            .ToListAsync(iptal);

        foreach (var kayit in numarasizlar)
        {
            // Açılış tarihi olmayan bozuk bir belge geçişi durdurmasın: kimliğin üretildiği an kullanılır.
            var acilis = kayit.TryGetValue("olusturmaTarihi", out var tarih) && tarih.IsValidDateTime
                ? tarih.ToUniversalTime()
                : kayit["_id"].IsObjectId ? kayit["_id"].AsObjectId.CreationTime : DateTime.UtcNow;
            var yil = Zaman.TurkiyeSaati(acilis).Year;
            var sira = await baglam.SiradakiNumaraAsync(MongoBaglami.ServisSayaci(yil), iptal);
            await kayitlar.UpdateOneAsync(f.Eq("_id", kayit["_id"]),
                u.Set("no", ServisKaydiKurallari.KayitNo(yil, sira)), cancellationToken: iptal);
        }

        foreach (var kayit in await kayitlar.Find(f.Exists("takipKodu", false) | f.Eq("takipKodu", ""))
                     .Project(Builders<BsonDocument>.Projection.Include("_id")).ToListAsync(iptal))
        {
            await kayitlar.UpdateOneAsync(f.Eq("_id", kayit["_id"]),
                u.Set("takipKodu", ServisKaydiKurallari.YeniTakipKodu()), cancellationToken: iptal);
        }
    }

    // "Ayşe Çelik" -> "ayse.celik"; kullanılıyorsa sonuna sayı eklenir.
    private static async Task<string> BosKullaniciAdiBul(IMongoCollection<BsonDocument> kullanicilar, string adSoyad, CancellationToken iptal)
    {
        var sade = new StringBuilder();
        foreach (var harf in adSoyad.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR")).Replace('ı', 'i').Normalize(NormalizationForm.FormD))
        {
            if (harf is >= 'a' and <= 'z' or >= '0' and <= '9') sade.Append(harf);
            else if (char.IsWhiteSpace(harf) && sade.Length > 0 && sade[^1] != '.') sade.Append('.');
        }

        var kok = sade.ToString().Trim('.');
        if (kok.Length < 3) kok = "personel";
        if (kok.Length > 28) kok = kok[..28].Trim('.');

        var aday = kok;
        for (var sayi = 2; await kullanicilar.Find(Builders<BsonDocument>.Filter.Eq("kullaniciAdi", aday)).AnyAsync(iptal); sayi++)
            aday = $"{kok}{sayi}";
        return aday;
    }
}
