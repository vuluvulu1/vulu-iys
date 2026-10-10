using System.Net;
using System.Net.Http.Json;
using MongoDB.Driver;
using Vulu.Shared;

namespace Vulu.Tests;

public class RandevuCakismaTestleri : IClassFixture<ApiFabrikasi>
{
    private const string Yol = "api/randevular";

    private readonly ApiFabrikasi _api;

    public RandevuCakismaTestleri(ApiFabrikasi api) => _api = api;

    // Her test başka bir güne yazar ki aynı teknisyeni kullanan testler birbirini engellemesin.
    private static int _gunSayaci;
    private static DateTime YeniGun(int saat) =>
        DateTime.UtcNow.Date.AddDays(10 + Interlocked.Increment(ref _gunSayaci)).AddHours(saat);

    private static async Task<string> MusteriEkle(HttpClient admin)
    {
        using var yanit = await admin.PostAsJsonAsync("api/musteriler", new MusteriIstegi("Çakışma Müşterisi", "05554445566", null, null));
        return (await yanit.Content.ReadFromJsonAsync<MusteriYaniti>())!.Id;
    }

    private static async Task<string> TeknisyenEkle(HttpClient admin, string ad = "Çakışma Ustası")
    {
        return (await ApiFabrikasi.PersonelEkle(admin, ad)).Id;
    }

    private static Task<HttpResponseMessage> Ekle(HttpClient admin, string musteriId, string? teknisyenId, DateTime tarih, int sure = 60) =>
        admin.PostAsJsonAsync(Yol, new RandevuIstegi(musteriId, teknisyenId, null, tarih, sure, null));

    private static async Task<RandevuYaniti> Eklenen(Task<HttpResponseMessage> istek)
    {
        using var yanit = await istek;
        Assert.Equal(HttpStatusCode.Created, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<RandevuYaniti>())!;
    }

    [Fact]
    public async Task Ayni_teknisyene_ust_uste_binen_randevu_verilemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        var teknisyen = await TeknisyenEkle(admin);
        var saat10 = YeniGun(10);
        await Eklenen(Ekle(admin, musteri, teknisyen, saat10, sure: 60));

        using var ayniSaat = await Ekle(admin, musteri, teknisyen, saat10);
        using var icinde = await Ekle(admin, musteri, teknisyen, saat10.AddMinutes(30));
        using var oncedenTasan = await Ekle(admin, musteri, teknisyen, saat10.AddMinutes(-30), sure: 60);
        using var kapsayan = await Ekle(admin, musteri, teknisyen, saat10.AddHours(-1), sure: 180);

        Assert.All(new[] { ayniSaat, icinde, oncedenTasan, kapsayan },
            yanit => Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode));
    }

    [Fact]
    public async Task Bitisik_saatler_baska_teknisyen_ve_teknisyensiz_randevular_cakismaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        var teknisyen = await TeknisyenEkle(admin);
        var baskaTeknisyen = await TeknisyenEkle(admin, "Diğer Usta");
        var saat10 = YeniGun(10);
        await Eklenen(Ekle(admin, musteri, teknisyen, saat10, sure: 60));

        await Eklenen(Ekle(admin, musteri, teknisyen, saat10.AddHours(1)));
        await Eklenen(Ekle(admin, musteri, teknisyen, saat10.AddHours(-1), sure: 60));
        await Eklenen(Ekle(admin, musteri, baskaTeknisyen, saat10));
        await Eklenen(Ekle(admin, musteri, null, saat10));
        await Eklenen(Ekle(admin, musteri, null, saat10));
    }

    [Fact]
    public async Task Iptal_edilen_randevu_saati_bosaltir_ve_dolu_saate_yeniden_planlanamaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        var teknisyen = await TeknisyenEkle(admin);
        var saat = YeniGun(14);
        var ilk = await Eklenen(Ekle(admin, musteri, teknisyen, saat));

        (await admin.PutAsJsonAsync($"{Yol}/{ilk.Id}/durum", new RandevuDurumuIstegi(RandevuDurumu.IptalEdildi))).EnsureSuccessStatusCode();
        await Eklenen(Ekle(admin, musteri, teknisyen, saat));
        using var yenidenPlanla = await admin.PutAsJsonAsync($"{Yol}/{ilk.Id}/durum", new RandevuDurumuIstegi(RandevuDurumu.Planlandi));

        Assert.Equal(HttpStatusCode.Conflict, yenidenPlanla.StatusCode);
    }

    [Fact]
    public async Task Randevu_kendi_saatiyle_cakismaz_ama_baskasinin_saatine_tasinamaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        var teknisyen = await TeknisyenEkle(admin);
        var saat10 = YeniGun(10);
        var ilk = await Eklenen(Ekle(admin, musteri, teknisyen, saat10));
        var ikinci = await Eklenen(Ekle(admin, musteri, teknisyen, saat10.AddHours(2)));

        using var ayniyiKaydet = await admin.PutAsJsonAsync($"{Yol}/{ilk.Id}",
            new RandevuIstegi(musteri, teknisyen, null, saat10, 90, "Uzatıldı"));
        using var ustuneTasi = await admin.PutAsJsonAsync($"{Yol}/{ikinci.Id}",
            new RandevuIstegi(musteri, teknisyen, null, saat10.AddMinutes(60), 60, null));

        Assert.Equal(HttpStatusCode.OK, ayniyiKaydet.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, ustuneTasi.StatusCode);
    }

    [Fact]
    public async Task Her_yazma_randevunun_surumunu_artirir()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        var randevu = await Eklenen(Ekle(admin, musteri, null, YeniGun(9)));
        var koleksiyon = _api.Veritabani.Randevular;
        var okunan = await koleksiyon.Find(r => r.Id == randevu.Id).SingleAsync();

        (await admin.PutAsJsonAsync($"{Yol}/{randevu.Id}/durum", new RandevuDurumuIstegi(RandevuDurumu.Tamamlandi))).EnsureSuccessStatusCode();

        var guncel = await koleksiyon.Find(r => r.Id == randevu.Id).SingleAsync();
        Assert.Equal(okunan.Surum + 1, guncel.Surum);
        // Eski kopyayla yazmaya çalışan biri reddedilir.
        var eskiyleYazma = await koleksiyon.ReplaceOneAsync(r => r.Id == okunan.Id && r.Surum == okunan.Surum, okunan);
        Assert.Equal(0, eskiyleYazma.MatchedCount);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1000)]
    public async Task Gecersiz_sure_400_doner(int sure)
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);

        using var yanit = await Ekle(admin, musteri, null, YeniGun(9), sure);

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }
}
