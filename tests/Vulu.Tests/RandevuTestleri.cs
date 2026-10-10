using System.Net;
using System.Net.Http.Json;
using Vulu.Shared;

namespace Vulu.Tests;

public class RandevuTestleri : IClassFixture<ApiFabrikasi>
{
    private const string Sifre = "Gecerli-Sifre-1";
    private const string Yol = "api/randevular";

    private readonly ApiFabrikasi _api;

    public RandevuTestleri(ApiFabrikasi api) => _api = api;

    private static DateTime Yarin(int saat = 10) => DateTime.UtcNow.Date.AddDays(1).AddHours(saat);

    private static async Task<T> Oku<T>(HttpResponseMessage yanit, HttpStatusCode beklenen = HttpStatusCode.OK)
    {
        Assert.Equal(beklenen, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<MusteriYaniti> MusteriEkle(HttpClient istemci, string adSoyad = "Randevu Müşterisi")
    {
        using var yanit = await istemci.PostAsJsonAsync("api/musteriler", new MusteriIstegi(adSoyad, "05553334455", null, null));
        return await Oku<MusteriYaniti>(yanit, HttpStatusCode.Created);
    }

    private static async Task<RandevuYaniti> RandevuEkle(HttpClient istemci, string musteriId, DateTime tarih,
        string? teknisyenId = null, string? servisKaydiId = null, string? aciklama = null, int sure = 60)
    {
        using var yanit = await istemci.PostAsJsonAsync(Yol, new RandevuIstegi(musteriId, teknisyenId, servisKaydiId, tarih, sure, aciklama));
        return await Oku<RandevuYaniti>(yanit, HttpStatusCode.Created);
    }

    [Fact]
    public async Task Randevu_musteri_teknisyen_ve_servis_kaydiyla_planlanir()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin, "Ayla Randevu");
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin, "Usta Randevu");
        using var kayitYaniti = await admin.PostAsJsonAsync("api/servis-kayitlari", new ServisKaydiIstegi(musteri.Id, null, "Laptop", "Arıza"));
        var kayit = await Oku<ServisKaydiYaniti>(kayitYaniti, HttpStatusCode.Created);
        var tarih = Yarin(14);

        var randevu = await RandevuEkle(admin, musteri.Id, tarih, teknisyen.Id, kayit.Id, "  İlk inceleme ");
        var okunan = await admin.GetFromJsonAsync<RandevuYaniti>($"{Yol}/{randevu.Id}");

        Assert.Equal(RandevuDurumu.Planlandi, okunan!.Durum);
        Assert.Equal("Ayla Randevu", okunan.MusteriAdi);
        Assert.Equal("Usta Randevu", okunan.TeknisyenAdi);
        Assert.Equal(kayit.Id, okunan.ServisKaydiId);
        Assert.Equal("İlk inceleme", okunan.Aciklama);
        Assert.Equal(tarih, okunan.RandevuTarihi.ToUniversalTime());
    }

    [Fact]
    public async Task Gecersiz_baglantilar_ve_gecmis_tarih_reddedilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        var baskasi = await MusteriEkle(admin, "Başka Müşteri");
        using var kayitYaniti = await admin.PostAsJsonAsync("api/servis-kayitlari", new ServisKaydiIstegi(baskasi.Id, null, "Telefon", "Arıza"));
        var baskasininKaydi = await Oku<ServisKaydiYaniti>(kayitYaniti, HttpStatusCode.Created);
        const string olmayan = "0123456789abcdef01234567";

        using var musterisiz = await admin.PostAsJsonAsync(Yol, new RandevuIstegi(olmayan, null, null, Yarin(), 60, null));
        using var teknisyensiz = await admin.PostAsJsonAsync(Yol, new RandevuIstegi(musteri.Id, olmayan, null, Yarin(), 60, null));
        using var yanlisKayit = await admin.PostAsJsonAsync(Yol, new RandevuIstegi(musteri.Id, null, baskasininKaydi.Id, Yarin(), 60, null));
        using var gecmis = await admin.PostAsJsonAsync(Yol, new RandevuIstegi(musteri.Id, null, null, DateTime.UtcNow.AddDays(-1), 60, null));

        Assert.Equal(HttpStatusCode.BadRequest, musterisiz.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, teknisyensiz.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, yanlisKayit.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, gecmis.StatusCode);
    }

    [Fact]
    public async Task Randevu_guncellenir_ve_durumu_degisir()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        var randevu = await RandevuEkle(admin, musteri.Id, Yarin(9));

        using var guncelle = await admin.PutAsJsonAsync($"{Yol}/{randevu.Id}",
            new RandevuIstegi(musteri.Id, null, null, Yarin(16), 90, "Saat değişti"));
        var guncel = await Oku<RandevuYaniti>(guncelle);
        using var tamamla = await admin.PutAsJsonAsync($"{Yol}/{randevu.Id}/durum", new RandevuDurumuIstegi(RandevuDurumu.Tamamlandi));
        var tamamlanan = await Oku<RandevuYaniti>(tamamla);

        Assert.Equal(Yarin(16), guncel.RandevuTarihi.ToUniversalTime());
        Assert.Equal("Saat değişti", guncel.Aciklama);
        Assert.Equal(RandevuDurumu.Tamamlandi, tamamlanan.Durum);
        Assert.Equal("Saat değişti", tamamlanan.Aciklama);
    }

    [Fact]
    public async Task Liste_tarihe_gore_siralanir_ve_duruma_tarih_araligina_musteriye_gore_suzulur()
    {
        using var admin = await _api.YetkiliIstemci();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var musteri = await MusteriEkle(admin, $"Süzgeç {ek}");
        var sonra = await RandevuEkle(admin, musteri.Id, Yarin(15));
        var once = await RandevuEkle(admin, musteri.Id, Yarin(9));
        var iptal = await RandevuEkle(admin, musteri.Id, Yarin(11));
        (await admin.PutAsJsonAsync($"{Yol}/{iptal.Id}/durum", new RandevuDurumuIstegi(RandevuDurumu.IptalEdildi))).EnsureSuccessStatusCode();

        async Task<string[]> Idler(string sorgu) =>
            (await admin.GetFromJsonAsync<Sayfa<RandevuYaniti>>($"{Yol}?arama={ek}&{sorgu}"))!.Ogeler.Select(r => r.Id).ToArray();
        static string Iso(DateTime t) => Uri.EscapeDataString(t.ToString("o"));

        Assert.Equal([once.Id, iptal.Id, sonra.Id], await Idler(""));
        Assert.Equal([once.Id, sonra.Id], await Idler("durum=Planlandi"));
        Assert.Equal([iptal.Id], await Idler("durum=IptalEdildi"));
        Assert.Equal([iptal.Id, sonra.Id], await Idler($"baslangic={Iso(Yarin(10))}"));
        Assert.Equal([once.Id], await Idler($"bitis={Iso(Yarin(10))}"));
        Assert.Equal(3, (await Idler($"musteriId={musteri.Id}")).Length);
    }

    [Fact]
    public async Task Teknisyen_randevu_planlar_ama_silemez_yonetici_siler()
    {
        using var admin = await _api.YetkiliIstemci();
        var ad = "t" + Guid.NewGuid().ToString("N")[..10];
        (await admin.PostAsJsonAsync("api/kullanicilar", new KullaniciOlusturIstegi(ad, Sifre, KullaniciRol.Teknisyen, "Test Personeli"))).EnsureSuccessStatusCode();
        using var teknisyen = await _api.YetkiliIstemci(ad, Sifre);
        var randevu = await RandevuEkle(teknisyen, (await MusteriEkle(teknisyen)).Id, Yarin());

        using var teknisyenSiler = await teknisyen.DeleteAsync($"{Yol}/{randevu.Id}");
        using var adminSiler = await admin.DeleteAsync($"{Yol}/{randevu.Id}");
        using var getir = await admin.GetAsync($"{Yol}/{randevu.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, teknisyenSiler.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, adminSiler.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getir.StatusCode);
    }

    [Fact]
    public async Task Planli_randevu_iptal_edilince_musteri_silinebilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin);
        var randevu = await RandevuEkle(admin, musteri.Id, Yarin());

        using var engelli = await admin.DeleteAsync($"api/musteriler/{musteri.Id}");
        (await admin.PutAsJsonAsync($"{Yol}/{randevu.Id}/durum", new RandevuDurumuIstegi(RandevuDurumu.IptalEdildi))).EnsureSuccessStatusCode();
        using var serbest = await admin.DeleteAsync($"api/musteriler/{musteri.Id}");

        Assert.Equal(HttpStatusCode.Conflict, engelli.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, serbest.StatusCode);
    }
}
