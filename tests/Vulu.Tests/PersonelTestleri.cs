using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Api.Veri;
using Vulu.Shared;

namespace Vulu.Tests;

public class PersonelTestleri : IClassFixture<ApiFabrikasi>
{
    private readonly ApiFabrikasi _api;

    public PersonelTestleri(ApiFabrikasi api) => _api = api;

    private static async Task<string> MusteriEkle(HttpClient istemci)
    {
        using var yanit = await istemci.PostAsJsonAsync("api/musteriler", new MusteriIstegi("Personel Müşterisi", "05552223344", null, null));
        return (await yanit.Content.ReadFromJsonAsync<MusteriYaniti>())!.Id;
    }

    private static async Task<ServisKaydiYaniti> KayitEkle(HttpClient istemci, string musteriId, string? personelId)
    {
        using var yanit = await istemci.PostAsJsonAsync("api/servis-kayitlari", new ServisKaydiIstegi(musteriId, personelId, "Laptop", "Arıza"));
        Assert.Equal(HttpStatusCode.Created, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!;
    }

    private static Task<HttpResponseMessage> Guncelle(HttpClient admin, KullaniciOzeti kisi, string? adSoyad = null, bool aktif = true) =>
        admin.PutAsJsonAsync($"api/kullanicilar/{kisi.Id}",
            new KullaniciGuncelleIstegi(kisi.KullaniciAdi, kisi.Rol, aktif, null, adSoyad ?? kisi.AdSoyad, kisi.Telefon, kisi.Uzmanlik));

    [Fact]
    public async Task Personel_hesabi_ad_telefon_ve_uzmanlikla_acilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var kullaniciAdi = "p" + Guid.NewGuid().ToString("N")[..10];

        using var yanit = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi(kullaniciAdi, ApiFabrikasi.PersonelSifresi, KullaniciRol.Teknisyen, " Ayşe Çelik ", "05331112233", " Anakart "));
        var kisi = (await yanit.Content.ReadFromJsonAsync<KullaniciOzeti>())!;
        var oturum = await _api.GirisYap(kullaniciAdi, ApiFabrikasi.PersonelSifresi);

        Assert.Equal(HttpStatusCode.Created, yanit.StatusCode);
        Assert.Equal("Ayşe Çelik", kisi.AdSoyad);
        Assert.Equal("05331112233", kisi.Telefon);
        Assert.Equal("Anakart", kisi.Uzmanlik);
        Assert.Equal("Ayşe Çelik", oturum.Kullanici.AdSoyad);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("A", null)]
    [InlineData("Geçerli Ad", "1234")]
    public async Task Gecersiz_personel_bilgisi_400_doner(string adSoyad, string? telefon)
    {
        using var admin = await _api.YetkiliIstemci();

        using var yanit = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi("p" + Guid.NewGuid().ToString("N")[..10], ApiFabrikasi.PersonelSifresi, KullaniciRol.Teknisyen, adSoyad, telefon));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Secim_listesi_teknisyene_aciktir_ve_yalnizca_aktif_personeli_verir()
    {
        using var admin = await _api.YetkiliIstemci();
        var aktif = await ApiFabrikasi.PersonelEkle(admin, "Aktif Usta", uzmanlik: "Ekran");
        var pasif = await ApiFabrikasi.PersonelEkle(admin, "Ayrılan Usta");
        (await Guncelle(admin, pasif, aktif: false)).EnsureSuccessStatusCode();
        using var teknisyen = await _api.YetkiliIstemci(aktif.KullaniciAdi, ApiFabrikasi.PersonelSifresi);

        var liste = await teknisyen.GetFromJsonAsync<List<PersonelOzeti>>("api/personel");
        using var yonetim = await teknisyen.GetAsync("api/kullanicilar");

        Assert.Contains(liste!, p => p.Id == aktif.Id && p.AdSoyad == "Aktif Usta" && p.Uzmanlik == "Ekran");
        Assert.DoesNotContain(liste!, p => p.Id == pasif.Id);
        Assert.Equal(HttpStatusCode.Forbidden, yonetim.StatusCode);
    }

    [Fact]
    public async Task Ad_degisince_servis_kaydindaki_ve_randevudaki_kopya_da_degisir()
    {
        using var admin = await _api.YetkiliIstemci();
        var kisi = await ApiFabrikasi.PersonelEkle(admin, "Eski Ad");
        var musteri = await MusteriEkle(admin);
        var kayit = await KayitEkle(admin, musteri, kisi.Id);
        using var randevuYaniti = await admin.PostAsJsonAsync("api/randevular",
            new RandevuIstegi(musteri, kisi.Id, null, DateTime.UtcNow.AddDays(40), 60, null));
        var randevu = (await randevuYaniti.Content.ReadFromJsonAsync<RandevuYaniti>())!;

        (await Guncelle(admin, kisi, adSoyad: "Yeni Ad")).EnsureSuccessStatusCode();

        var guncelKayit = await admin.GetFromJsonAsync<ServisKaydiYaniti>($"api/servis-kayitlari/{kayit.Id}");
        var guncelRandevu = await admin.GetFromJsonAsync<RandevuYaniti>($"api/randevular/{randevu.Id}");
        Assert.Equal("Yeni Ad", guncelKayit!.TeknisyenAdi);
        Assert.Equal("Yeni Ad", guncelRandevu!.TeknisyenAdi);
    }

    [Fact]
    public async Task Devre_disi_personele_is_atanamaz_ama_mevcut_atamasi_duzenlemede_korunur()
    {
        using var admin = await _api.YetkiliIstemci();
        var kisi = await ApiFabrikasi.PersonelEkle(admin, "Ayrılacak Usta");
        var musteri = await MusteriEkle(admin);
        var kayit = await KayitEkle(admin, musteri, kisi.Id);
        (await Guncelle(admin, kisi, aktif: false)).EnsureSuccessStatusCode();

        using var yeniAtama = await admin.PostAsJsonAsync("api/servis-kayitlari", new ServisKaydiIstegi(musteri, kisi.Id, "Telefon", "Arıza"));
        using var duzenleme = await admin.PutAsJsonAsync($"api/servis-kayitlari/{kayit.Id}",
            new ServisKaydiIstegi(musteri, kisi.Id, "Laptop", "Sorun güncellendi"));

        Assert.Equal(HttpStatusCode.BadRequest, yeniAtama.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duzenleme.StatusCode);
    }

    [Fact]
    public async Task Is_gecmisi_olan_personel_silinemez_olmayan_silinir()
    {
        using var admin = await _api.YetkiliIstemci();
        var gecmisli = await ApiFabrikasi.PersonelEkle(admin, "Geçmişi Olan");
        var gecmissiz = await ApiFabrikasi.PersonelEkle(admin, "Yeni Gelen");
        await KayitEkle(admin, await MusteriEkle(admin), gecmisli.Id);

        using var engelli = await admin.DeleteAsync($"api/kullanicilar/{gecmisli.Id}");
        using var serbest = await admin.DeleteAsync($"api/kullanicilar/{gecmissiz.Id}");

        Assert.Equal(HttpStatusCode.Conflict, engelli.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, serbest.StatusCode);
    }

    [Fact]
    public async Task Olmayan_personele_is_atanamaz()
    {
        using var admin = await _api.YetkiliIstemci();

        using var yanit = await admin.PostAsJsonAsync("api/servis-kayitlari",
            new ServisKaydiIstegi(await MusteriEkle(admin), "0123456789abcdef01234567", "Laptop", "Arıza"));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }
}

// Eski biçimdeki veriyi yeni biçime taşıyan geçişi, API'den bağımsız olarak boş bir veritabanında sınar.
public sealed class GecisTestleri : IAsyncLifetime, IDisposable
{
    private readonly MongoClient _istemci = new(ApiFabrikasi.MongoAdresi);
    private readonly string _ad = "vulu_tsys_test_" + Guid.NewGuid().ToString("N");
    private IMongoDatabase Db => _istemci.GetDatabase(_ad);

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => _istemci.DropDatabaseAsync(_ad);
    public void Dispose() => _istemci.Dispose();

    private IMongoCollection<BsonDocument> K(string ad) => Db.GetCollection<BsonDocument>(ad);

    [Fact]
    public async Task Teknisyenler_kullanici_hesaplarina_tasinir_ve_kayitlar_yeni_kimlige_baglanir()
    {
        var hesapId = ObjectId.GenerateNewId();
        var adsizHesapId = ObjectId.GenerateNewId();
        var bagli = ObjectId.GenerateNewId();
        var hesapsiz = ObjectId.GenerateNewId();
        var silinmis = ObjectId.GenerateNewId();

        await K("kullanicilar").InsertManyAsync(
        [
            new BsonDocument { ["_id"] = hesapId, ["kullaniciAdi"] = "ayse", ["sifreHash"] = "x", ["rol"] = "Teknisyen", ["aktifMi"] = true },
            new BsonDocument { ["_id"] = adsizHesapId, ["kullaniciAdi"] = "admin", ["sifreHash"] = "x", ["rol"] = "Admin", ["aktifMi"] = true }
        ]);
        await K("teknisyenler").InsertManyAsync(
        [
            new BsonDocument { ["_id"] = bagli, ["adSoyad"] = "Ayşe Çelik", ["telefon"] = "05331112233", ["uzmanlik"] = "Anakart", ["kullaniciId"] = hesapId },
            new BsonDocument { ["_id"] = hesapsiz, ["adSoyad"] = "Işıl Öztürk", ["telefon"] = "05441112233" },
            new BsonDocument { ["_id"] = silinmis, ["adSoyad"] = "Eski Usta", ["silinmeTarihi"] = DateTime.UtcNow }
        ]);
        await K("servisKayitlari").InsertManyAsync(
        [
            new BsonDocument { ["_id"] = 1, ["teknisyenId"] = bagli, ["teknisyenAdi"] = "Ayşe Çelik", ["surum"] = 3 },
            new BsonDocument { ["_id"] = 2, ["teknisyenId"] = hesapsiz, ["teknisyenAdi"] = "Işıl Öztürk" },
            new BsonDocument { ["_id"] = 3, ["teknisyenId"] = silinmis, ["teknisyenAdi"] = "Eski Usta" }
        ]);
        await K("randevular").InsertOneAsync(new BsonDocument { ["_id"] = 1, ["teknisyenId"] = hesapsiz });

        await Gecisler.CalistirAsync(Db, NullLogger.Instance);
        // İkinci çalıştırma hiçbir şeyi değiştirmemeli.
        await Gecisler.CalistirAsync(Db, NullLogger.Instance);

        var kullanicilar = await K("kullanicilar").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.Equal(3, kullanicilar.Count);

        var ayse = kullanicilar.Single(k => k["_id"] == hesapId);
        Assert.Equal("Ayşe Çelik", ayse["adSoyad"].AsString);
        Assert.Equal("05331112233", ayse["telefon"].AsString);
        Assert.Equal("Anakart", ayse["uzmanlik"].AsString);
        Assert.True(ayse["aktifMi"].AsBoolean);

        // Ad soyadı olmayan eski hesap kullanıcı adını alır.
        Assert.Equal("admin", kullanicilar.Single(k => k["_id"] == adsizHesapId)["adSoyad"].AsString);

        // Hesabı olmayan teknisyene devre dışı bir hesap açılır.
        var isil = kullanicilar.Single(k => k["adSoyad"] == "Işıl Öztürk");
        Assert.Equal("isil.ozturk", isil["kullaniciAdi"].AsString);
        Assert.False(isil["aktifMi"].AsBoolean);
        Assert.Equal("Teknisyen", isil["rol"].AsString);

        var kayitlar = (await K("servisKayitlari").Find(FilterDefinition<BsonDocument>.Empty).ToListAsync()).ToDictionary(k => k["_id"].AsInt32);
        Assert.Equal(hesapId, kayitlar[1]["teknisyenId"]);
        Assert.Equal(4, kayitlar[1]["surum"].AsInt32);
        Assert.Equal(isil["_id"], kayitlar[2]["teknisyenId"]);
        Assert.False(kayitlar[3].Contains("teknisyenId"));
        Assert.Equal("Eski Usta", kayitlar[3]["teknisyenAdi"].AsString);

        var randevu = await K("randevular").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal(isil["_id"], randevu["teknisyenId"]);

        var koleksiyonlar = await (await Db.ListCollectionNamesAsync()).ToListAsync();
        Assert.DoesNotContain("teknisyenler", koleksiyonlar);
        Assert.Contains("teknisyenler_eski", koleksiyonlar);
    }

    [Fact]
    public async Task Bos_veritabaninda_gecis_sorunsuz_tamamlanir()
    {
        await Gecisler.CalistirAsync(Db, NullLogger.Instance);

        var surum = await K("sema").Find(Builders<BsonDocument>.Filter.Eq("_id", "surum")).SingleAsync();
        Assert.True(surum["deger"].AsInt32 >= 1);
    }
}
