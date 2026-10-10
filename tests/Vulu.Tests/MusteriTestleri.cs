using System.Net;
using System.Net.Http.Json;
using MongoDB.Driver;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Tests;

public class MusteriTestleri : IClassFixture<ApiFabrikasi>
{
    private readonly ApiFabrikasi _api;

    public MusteriTestleri(ApiFabrikasi api) => _api = api;

    private static MusteriIstegi Istek(string adSoyad = "Ahmet Yılmaz", string telefon = "05551234567",
        string? email = null, string? adres = null) => new(adSoyad, telefon, email, adres);

    private static async Task<MusteriYaniti> Ekle(HttpClient istemci, MusteriIstegi istek)
    {
        using var yanit = await istemci.PostAsJsonAsync("api/musteriler", istek);
        Assert.Equal(HttpStatusCode.Created, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<MusteriYaniti>())!;
    }

    private static Task<Sayfa<MusteriYaniti>?> Ara(HttpClient istemci, string arama, int sayfa = 1, int boyut = 25) =>
        istemci.GetFromJsonAsync<Sayfa<MusteriYaniti>>(
            $"api/musteriler?arama={Uri.EscapeDataString(arama)}&sayfa={sayfa}&boyut={boyut}");

    [Fact]
    public async Task Musteri_eklenir_ve_getirilir()
    {
        using var istemci = await _api.YetkiliIstemci();

        var eklenen = await Ekle(istemci, Istek("  Zeynep Kaya ", "05321112233", "zeynep@ornek.com", " Düzce "));
        var okunan = await istemci.GetFromJsonAsync<MusteriYaniti>($"api/musteriler/{eklenen.Id}");

        Assert.Equal("Zeynep Kaya", okunan!.AdSoyad);
        Assert.Equal("05321112233", okunan.Telefon);
        Assert.Equal("zeynep@ornek.com", okunan.Email);
        Assert.Equal("Düzce", okunan.Adres);
    }

    [Fact]
    public async Task Bos_eposta_ve_adres_null_olarak_saklanir()
    {
        using var istemci = await _api.YetkiliIstemci();

        var eklenen = await Ekle(istemci, Istek(email: null, adres: "   "));

        Assert.Null(eklenen.Email);
        Assert.Null(eklenen.Adres);
    }

    [Theory]
    [InlineData("A", "05551234567", null)]
    [InlineData("Geçerli Ad", "5551234567", null)]
    [InlineData("Geçerli Ad", "0555123456a", null)]
    [InlineData("Geçerli Ad", "05551234567", "eposta-degil")]
    public async Task Gecersiz_musteri_bilgisi_400_doner(string adSoyad, string telefon, string? email)
    {
        using var istemci = await _api.YetkiliIstemci();

        using var yanit = await istemci.PostAsJsonAsync("api/musteriler", Istek(adSoyad, telefon, email));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Arama_turkce_harflerde_buyuk_kucuk_ayrimi_yapmaz()
    {
        using var istemci = await _api.YetkiliIstemci();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var musteri = await Ekle(istemci, Istek($"Işıl İnce {ek}"));

        var buyuk = await Ara(istemci, $"IŞIL İNCE {ek}");
        var kucuk = await Ara(istemci, $"ışıl ince {ek}");

        Assert.Contains(buyuk!.Ogeler, m => m.Id == musteri.Id);
        Assert.Contains(kucuk!.Ogeler, m => m.Id == musteri.Id);
    }

    [Fact]
    public async Task Telefonla_aranir_ve_ozel_karakterler_desen_sayilmaz()
    {
        using var istemci = await _api.YetkiliIstemci();
        var musteri = await Ekle(istemci, Istek("Telefon Araması", "05077778899"));

        var bulunan = await Ara(istemci, "0507777");
        var desen = await Ara(istemci, ".*");

        Assert.Contains(bulunan!.Ogeler, m => m.Id == musteri.Id);
        Assert.Empty(desen!.Ogeler);
    }

    [Fact]
    public async Task Liste_sayfalanir_ve_ada_gore_siralanir()
    {
        using var istemci = await _api.YetkiliIstemci();
        var ek = "sayfa" + Guid.NewGuid().ToString("N")[..8];
        foreach (var ad in new[] { "Çağrı", "Ali", "Zeki" })
            await Ekle(istemci, Istek($"{ad} {ek}"));

        var ilk = await Ara(istemci, ek, sayfa: 1, boyut: 2);
        var ikinci = await Ara(istemci, ek, sayfa: 2, boyut: 2);

        Assert.Equal(3, ilk!.Toplam);
        Assert.Equal([$"Ali {ek}", $"Çağrı {ek}"], ilk.Ogeler.Select(m => m.AdSoyad));
        Assert.Equal([$"Zeki {ek}"], ikinci!.Ogeler.Select(m => m.AdSoyad));
    }

    [Fact]
    public async Task Guncelleme_servis_kaydindaki_musteri_ozetini_de_degistirir()
    {
        using var istemci = await _api.YetkiliIstemci();
        var musteri = await Ekle(istemci, Istek("Eski Ad", "05550000001"));
        var kayit = new ServisKaydi
        {
            MusteriId = musteri.Id,
            Musteri = new MusteriOzeti { AdSoyad = "Eski Ad", Telefon = "05550000001" },
            CihazBilgisi = "Laptop",
            Sorun = "Açılmıyor",
            Durum = ServisDurumu.Tamamlandi
        };
        await _api.Veritabani.ServisKayitlari.InsertOneAsync(kayit);

        using var yanit = await istemci.PutAsJsonAsync($"api/musteriler/{musteri.Id}", Istek("Yeni Ad", "05550000002"));

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        var guncel = await _api.Veritabani.ServisKayitlari.Find(s => s.Id == kayit.Id).SingleAsync();
        Assert.Equal("Yeni Ad", guncel.Musteri.AdSoyad);
        Assert.Equal("05550000002", guncel.Musteri.Telefon);
    }

    [Fact]
    public async Task Teknisyen_musteri_ekleyebilir_ama_silemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var ad = "t" + Guid.NewGuid().ToString("N")[..10];
        using var olustur = await admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi(ad, "Gecerli-Sifre-1", KullaniciRol.Teknisyen));
        olustur.EnsureSuccessStatusCode();

        using var teknisyen = await _api.YetkiliIstemci(ad, "Gecerli-Sifre-1");
        var musteri = await Ekle(teknisyen, Istek("Teknisyenin Müşterisi"));
        using var sil = await teknisyen.DeleteAsync($"api/musteriler/{musteri.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, sil.StatusCode);
    }

    [Fact]
    public async Task Silinen_musteri_listede_gorunmez_ama_belgesi_kalir()
    {
        using var istemci = await _api.YetkiliIstemci();
        var ek = Guid.NewGuid().ToString("N")[..8];
        var musteri = await Ekle(istemci, Istek($"Silinecek {ek}"));

        using var sil = await istemci.DeleteAsync($"api/musteriler/{musteri.Id}");
        using var getir = await istemci.GetAsync($"api/musteriler/{musteri.Id}");
        using var tekrarSil = await istemci.DeleteAsync($"api/musteriler/{musteri.Id}");
        var liste = await Ara(istemci, ek);

        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getir.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tekrarSil.StatusCode);
        Assert.Empty(liste!.Ogeler);
        var belge = await _api.Veritabani.Musteriler.Find(m => m.Id == musteri.Id).SingleAsync();
        Assert.NotNull(belge.SilinmeTarihi);
    }

    [Fact]
    public async Task Devam_eden_servis_kaydi_olan_musteri_silinemez()
    {
        using var istemci = await _api.YetkiliIstemci();
        var musteri = await Ekle(istemci, Istek("Açık Kaydı Olan"));
        await _api.Veritabani.ServisKayitlari.InsertOneAsync(new ServisKaydi
        {
            MusteriId = musteri.Id,
            CihazBilgisi = "Telefon",
            Sorun = "Ekran kırık",
            Durum = ServisDurumu.DevamEdiyor
        });

        using var sil = await istemci.DeleteAsync($"api/musteriler/{musteri.Id}");

        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);
    }

    [Fact]
    public async Task Planli_randevusu_olan_musteri_silinemez()
    {
        using var istemci = await _api.YetkiliIstemci();
        var musteri = await Ekle(istemci, Istek("Randevusu Olan"));
        await _api.Veritabani.Randevular.InsertOneAsync(new Randevu
        {
            MusteriId = musteri.Id,
            RandevuTarihi = DateTime.UtcNow.AddDays(1),
            Durum = RandevuDurumu.Planlandi
        });

        using var sil = await istemci.DeleteAsync($"api/musteriler/{musteri.Id}");

        Assert.Equal(HttpStatusCode.Conflict, sil.StatusCode);
    }
}
