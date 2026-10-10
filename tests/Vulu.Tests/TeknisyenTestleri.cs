using System.Net;
using System.Net.Http.Json;
using MongoDB.Driver;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Tests;

public class TeknisyenTestleri : IClassFixture<ApiFabrikasi>
{
    private const string Sifre = "Gecerli-Sifre-1";

    private readonly ApiFabrikasi _api;

    public TeknisyenTestleri(ApiFabrikasi api) => _api = api;

    private static TeknisyenIstegi Istek(string adSoyad = "Mehmet Demir", string telefon = "05441234567",
        string? uzmanlik = null, string? kullaniciId = null) => new(adSoyad, telefon, uzmanlik, kullaniciId);

    private static async Task<TeknisyenYaniti> Ekle(HttpClient admin, TeknisyenIstegi istek)
    {
        using var yanit = await admin.PostAsJsonAsync("api/teknisyenler", istek);
        Assert.Equal(HttpStatusCode.Created, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<TeknisyenYaniti>())!;
    }

    private static async Task<KullaniciOzeti> HesapOlustur(HttpClient admin)
    {
        using var yanit = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi("t" + Guid.NewGuid().ToString("N")[..10], Sifre, KullaniciRol.Teknisyen));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<KullaniciOzeti>())!;
    }

    [Fact]
    public async Task Teknisyen_hesabiyla_birlikte_eklenir_ve_hesap_adi_doner()
    {
        using var admin = await _api.YetkiliIstemci();
        var hesap = await HesapOlustur(admin);

        var eklenen = await Ekle(admin, Istek(" Ayşe Çelik ", "05331112233", " Anakart ", hesap.Id));
        var okunan = await admin.GetFromJsonAsync<TeknisyenYaniti>($"api/teknisyenler/{eklenen.Id}");

        Assert.Equal("Ayşe Çelik", okunan!.AdSoyad);
        Assert.Equal("Anakart", okunan.Uzmanlik);
        Assert.Equal(hesap.Id, okunan.KullaniciId);
        Assert.Equal(hesap.KullaniciAdi, okunan.KullaniciAdi);
    }

    [Fact]
    public async Task Teknisyen_rolu_listeyi_gorur_ama_degistiremez()
    {
        using var admin = await _api.YetkiliIstemci();
        var hesap = await HesapOlustur(admin);
        var mevcut = await Ekle(admin, Istek("Liste Görünen"));
        using var teknisyen = await _api.YetkiliIstemci(hesap.KullaniciAdi, Sifre);

        var liste = await teknisyen.GetFromJsonAsync<List<TeknisyenYaniti>>("api/teknisyenler");
        using var ekle = await teknisyen.PostAsJsonAsync("api/teknisyenler", Istek("Yetkisiz"));
        using var guncelle = await teknisyen.PutAsJsonAsync($"api/teknisyenler/{mevcut.Id}", Istek("Yetkisiz"));
        using var sil = await teknisyen.DeleteAsync($"api/teknisyenler/{mevcut.Id}");

        Assert.Contains(liste!, t => t.Id == mevcut.Id);
        Assert.Equal(HttpStatusCode.Forbidden, ekle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, guncelle.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sil.StatusCode);
    }

    [Fact]
    public async Task Ayni_hesap_iki_teknisyene_baglanamaz()
    {
        using var admin = await _api.YetkiliIstemci();
        var hesap = await HesapOlustur(admin);
        await Ekle(admin, Istek("Birinci", kullaniciId: hesap.Id));
        var ikinci = await Ekle(admin, Istek("İkinci"));

        using var ekle = await admin.PostAsJsonAsync("api/teknisyenler", Istek("Üçüncü", kullaniciId: hesap.Id));
        using var guncelle = await admin.PutAsJsonAsync($"api/teknisyenler/{ikinci.Id}", Istek("İkinci", kullaniciId: hesap.Id));

        Assert.Equal(HttpStatusCode.Conflict, ekle.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, guncelle.StatusCode);
    }

    [Theory]
    [InlineData("0123456789abcdef01234567")]
    [InlineData("gecersiz-kimlik")]
    public async Task Olmayan_ya_da_gecersiz_hesap_400_doner(string kullaniciId)
    {
        using var admin = await _api.YetkiliIstemci();

        using var yanit = await admin.PostAsJsonAsync("api/teknisyenler", Istek(kullaniciId: kullaniciId));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Theory]
    [InlineData("A", "05441234567")]
    [InlineData("Geçerli Ad", "1234")]
    public async Task Gecersiz_teknisyen_bilgisi_400_doner(string adSoyad, string telefon)
    {
        using var admin = await _api.YetkiliIstemci();

        using var yanit = await admin.PostAsJsonAsync("api/teknisyenler", Istek(adSoyad, telefon));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Uzmanliga_gore_aranir()
    {
        using var admin = await _api.YetkiliIstemci();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var teknisyen = await Ekle(admin, Istek("Arama Deneme", uzmanlik: $"Işık tamiri {ek}"));

        var bulunan = await admin.GetFromJsonAsync<List<TeknisyenYaniti>>(
            $"api/teknisyenler?arama={Uri.EscapeDataString($"IŞIK TAMİRİ {ek}")}");

        Assert.Equal([teknisyen.Id], bulunan!.Select(t => t.Id));
    }

    [Fact]
    public async Task Guncelleme_servis_kaydindaki_teknisyen_adini_da_degistirir()
    {
        using var admin = await _api.YetkiliIstemci();
        var teknisyen = await Ekle(admin, Istek("Eski Ad"));
        var kayit = new ServisKaydi
        {
            MusteriId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            TeknisyenId = teknisyen.Id,
            TeknisyenAdi = "Eski Ad",
            CihazBilgisi = "Laptop",
            Sorun = "Açılmıyor",
            Durum = ServisDurumu.Tamamlandi
        };
        await _api.Veritabani.ServisKayitlari.InsertOneAsync(kayit);

        using var yanit = await admin.PutAsJsonAsync($"api/teknisyenler/{teknisyen.Id}", Istek("Yeni Ad"));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var guncel = await _api.Veritabani.ServisKayitlari.Find(s => s.Id == kayit.Id).SingleAsync();
        Assert.Equal("Yeni Ad", guncel.TeknisyenAdi);
    }

    [Fact]
    public async Task Silinen_teknisyen_listeden_kalkar_ve_hesabi_bosa_cikar()
    {
        using var admin = await _api.YetkiliIstemci();
        var hesap = await HesapOlustur(admin);
        var teknisyen = await Ekle(admin, Istek("Silinecek", kullaniciId: hesap.Id));

        using var sil = await admin.DeleteAsync($"api/teknisyenler/{teknisyen.Id}");
        using var getir = await admin.GetAsync($"api/teknisyenler/{teknisyen.Id}");
        var liste = await admin.GetFromJsonAsync<List<TeknisyenYaniti>>("api/teknisyenler");

        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getir.StatusCode);
        Assert.DoesNotContain(liste!, t => t.Id == teknisyen.Id);

        // Hesap artık başka bir teknisyene bağlanabilir ve silinebilir.
        var yeni = await Ekle(admin, Istek("Yerine Gelen", kullaniciId: hesap.Id));
        Assert.Equal(hesap.Id, yeni.KullaniciId);
    }

    [Fact]
    public async Task Devam_eden_isi_olan_teknisyen_silinemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var teknisyen = await Ekle(admin, Istek("İşi Olan"));
        await _api.Veritabani.ServisKayitlari.InsertOneAsync(new ServisKaydi
        {
            MusteriId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            TeknisyenId = teknisyen.Id,
            CihazBilgisi = "Telefon",
            Sorun = "Ekran kırık",
            Durum = ServisDurumu.Beklemede
        });

        using var sil = await admin.DeleteAsync($"api/teknisyenler/{teknisyen.Id}");

        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);
    }

    [Fact]
    public async Task Teknisyene_bagli_hesap_silinemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var hesap = await HesapOlustur(admin);
        await Ekle(admin, Istek("Hesabı Olan", kullaniciId: hesap.Id));

        using var sil = await admin.DeleteAsync($"api/kullanicilar/{hesap.Id}");

        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);
    }
}
