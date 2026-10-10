using System.Net;
using System.Net.Http.Json;
using MongoDB.Driver;
using Vulu.Shared;

namespace Vulu.Tests;

public class PlatformTestleri : IClassFixture<ApiFabrikasi>
{
    private readonly ApiFabrikasi _api;

    public PlatformTestleri(ApiFabrikasi api) => _api = api;

    private static string YeniKod() => "plt-" + Guid.NewGuid().ToString("N")[..10];

    private static string Yol(IsletmeYaniti isletme) => $"api/platform/isletmeler/{isletme.Id}";

    private static Task<HttpResponseMessage> Durum(HttpClient yonetici, IsletmeYaniti isletme, bool aktif,
        int personelSiniri = 1000, DateTime? abonelikBitisi = null) =>
        yonetici.PutAsJsonAsync(Yol(isletme), new IsletmeDurumIstegi(isletme.Ad, aktif, personelSiniri, abonelikBitisi));

    private static Task<HttpResponseMessage> PersonelAc(HttpClient admin, string kullaniciAdi) =>
        admin.PostAsJsonAsync("api/kullanicilar",
            new KullaniciOlusturIstegi(kullaniciAdi, ApiFabrikasi.PersonelSifresi, KullaniciRol.Teknisyen, "Kota Personeli"));

    // --- İşletme yöneticisinin şifresini sıfırlama ---

    [Fact]
    public async Task Vulu_yoneticisi_isletme_yoneticisinin_sifresini_sifirlar_ve_eski_oturumu_kapanir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Şifresini Unutan", "sahip", "Eski-Sifre-123");
        var eskiOturum = await _api.GirisYap("sahip", "Eski-Sifre-123", kod);
        using var anonim = _api.CreateClient();

        var yoneticiler = await yonetici.GetFromJsonAsync<List<IsletmeYoneticisiOzeti>>($"{Yol(isletme)}/yoneticiler");
        var sahip = Assert.Single(yoneticiler!);
        using var sifirla = await yonetici.PutAsJsonAsync($"{Yol(isletme)}/yoneticiler/{sahip.Id}/sifre", new SifreSifirlamaIstegi("Yeni-Sifre-456"));

        using var eskiSifre = await anonim.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(kod, "sahip", "Eski-Sifre-123"));
        using var eskiYenileme = await anonim.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(eskiOturum.YenilemeTokeni));
        var yeniOturum = await _api.GirisYap("sahip", "Yeni-Sifre-456", kod);

        Assert.Equal("sahip", sahip.KullaniciAdi);
        Assert.Equal(HttpStatusCode.NoContent, sifirla.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, eskiSifre.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, eskiYenileme.StatusCode);
        Assert.Equal(KullaniciRol.Admin, yeniOturum.Kullanici.Rol);
    }

    [Fact]
    public async Task Yonetici_olmayan_personelin_sifresi_vulu_tarafindan_sifirlanamaz()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Teknisyeni Olan");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin);

        using var sifirla = await yonetici.PutAsJsonAsync($"{Yol(isletme)}/yoneticiler/{teknisyen.Id}/sifre", new SifreSifirlamaIstegi("Yeni-Sifre-456"));
        using var kisaSifre = await yonetici.PutAsJsonAsync($"{Yol(isletme)}/yoneticiler/{teknisyen.Id}/sifre", new SifreSifirlamaIstegi("kisa"));

        Assert.Equal(HttpStatusCode.NotFound, sifirla.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, kisaSifre.StatusCode);
        // Teknisyenin şifresi değişmedi.
        await _api.GirisYap(teknisyen.KullaniciAdi, ApiFabrikasi.PersonelSifresi, kod);
    }

    // --- İşletmeyi kalıcı silme ---

    [Fact]
    public async Task Isletme_ancak_askidayken_ve_kodu_yazilarak_silinir_verisi_de_gider()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Silinecek İşletme");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);
        (await admin.PostAsJsonAsync("api/musteriler", new MusteriIstegi("Silinecek Müşteri", "05551112233", null, null))).EnsureSuccessStatusCode();
        var veritabaniAdi = (await _api.Platform.Isletmeler.Find(i => i.Id == isletme.Id).SingleAsync()).VeritabaniAdi;
        var istemci = _api.Platform.Isletmeler.Database.Client;

        using var acikken = await yonetici.DeleteAsync($"{Yol(isletme)}?kod={kod}");
        (await Durum(yonetici, isletme, aktif: false)).EnsureSuccessStatusCode();
        using var kodsuz = await yonetici.DeleteAsync(Yol(isletme));
        using var yanlisKod = await yonetici.DeleteAsync($"{Yol(isletme)}?kod=baska-kod");
        Assert.Equal(HttpStatusCode.Conflict, acikken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, kodsuz.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, yanlisKod.StatusCode);
        Assert.Contains(veritabaniAdi, await (await istemci.ListDatabaseNamesAsync()).ToListAsync());

        using var sil = await yonetici.DeleteAsync($"{Yol(isletme)}?kod={kod}");

        using var anonim = _api.CreateClient();
        using var giris = await anonim.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(kod, ApiFabrikasi.AdminAdi, ApiFabrikasi.AdminSifresi));
        var liste = await yonetici.GetFromJsonAsync<List<IsletmeYaniti>>("api/platform/isletmeler");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, giris.StatusCode);
        Assert.DoesNotContain(liste!, i => i.Id == isletme.Id);
        Assert.DoesNotContain(veritabaniAdi, await (await istemci.ListDatabaseNamesAsync()).ToListAsync());
        // Başka işletmelerin verisine dokunulmadı.
        Assert.Contains(liste!, i => i.Kod == ApiFabrikasi.IsletmeKodu);
    }

    // --- Paket: personel sınırı ve abonelik ---

    [Fact]
    public async Task Personel_siniri_dolunca_yeni_hesap_acilamaz_devre_disi_hesaplar_sayilmaz()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Küçük Paket", personelSiniri: 2);
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);

        // Yönetici bir yer tutuyor; bir personel daha açılabilir, üçüncüsü açılamaz.
        using var ikinci = await PersonelAc(admin, "ikinci");
        using var ucuncu = await PersonelAc(admin, "ucuncu");
        Assert.Equal(HttpStatusCode.Created, ikinci.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, ucuncu.StatusCode);

        // Biri devre dışı kalınca yer açılır; sınır doluyken devre dışı hesap yeniden açılamaz.
        var ikinciHesap = (await ikinci.Content.ReadFromJsonAsync<KullaniciOzeti>())!;
        KullaniciGuncelleIstegi Guncelleme(bool aktif) =>
            new(ikinciHesap.KullaniciAdi, ikinciHesap.Rol, aktif, null, ikinciHesap.AdSoyad);
        (await admin.PutAsJsonAsync($"api/kullanicilar/{ikinciHesap.Id}", Guncelleme(aktif: false))).EnsureSuccessStatusCode();
        using var yerine = await PersonelAc(admin, "yerine");
        using var yenidenAc = await admin.PutAsJsonAsync($"api/kullanicilar/{ikinciHesap.Id}", Guncelleme(aktif: true));
        Assert.Equal(HttpStatusCode.Created, yerine.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, yenidenAc.StatusCode);

        // Vulu yöneticisi sınırı artırınca hemen etkili olur ve listede kullanım görünür.
        (await Durum(yonetici, isletme, aktif: true, personelSiniri: 3)).EnsureSuccessStatusCode();
        using var artirinca = await admin.PutAsJsonAsync($"api/kullanicilar/{ikinciHesap.Id}", Guncelleme(aktif: true));
        var listedeki = (await yonetici.GetFromJsonAsync<List<IsletmeYaniti>>("api/platform/isletmeler"))!.Single(i => i.Id == isletme.Id);
        Assert.Equal(HttpStatusCode.OK, artirinca.StatusCode);
        Assert.Equal(3, listedeki.PersonelSiniri);
        Assert.Equal(3, listedeki.PersonelSayisi);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5000)]
    public async Task Gecersiz_personel_siniri_400_doner(int sinir)
    {
        using var yonetici = await _api.YoneticiIstemcisi();

        using var yanit = await yonetici.PostAsJsonAsync("api/platform/isletmeler",
            new IsletmeOlusturIstegi(YeniKod(), "Geçersiz Paket", "admin", "Gecerli-Sifre-1", "Ad Soyad", sinir));

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }

    [Fact]
    public async Task Odeme_kaydedilir_abonelik_bitisi_guncellenir_ve_isletme_kendi_bitisini_gorur()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = YeniKod();
        var isletme = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Abone İşletme");
        var bitis = new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        using var ilk = await yonetici.PostAsJsonAsync($"{Yol(isletme)}/odemeler",
            new OdemeIstegi(1500.50m, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), " Ekim ayı ", bitis));
        using var ikinci = await yonetici.PostAsJsonAsync($"{Yol(isletme)}/odemeler",
            new OdemeIstegi(1500.50m, new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc), null, null));
        using var gecersiz = await yonetici.PostAsJsonAsync($"{Yol(isletme)}/odemeler", new OdemeIstegi(0, DateTime.UtcNow, null, null));
        var odemeler = await yonetici.GetFromJsonAsync<List<OdemeYaniti>>($"{Yol(isletme)}/odemeler");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);
        var isletmeninGordugu = await admin.GetFromJsonAsync<IsletmeYaniti>("api/isletme");
        using var isletmeOdemeleriGoremez = await admin.GetAsync($"{Yol(isletme)}/odemeler");

        Assert.Equal(HttpStatusCode.OK, ilk.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ikinci.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, gecersiz.StatusCode);
        Assert.Equal(2, odemeler!.Count);
        Assert.Equal("Ekim ayı", odemeler[1].Aciklama);
        Assert.Equal(1500.50m, odemeler[0].Tutar);
        // Bitiş tarihi vermeyen ikinci ödeme aboneliği değiştirmez.
        Assert.Equal(bitis, isletmeninGordugu!.AbonelikBitisi?.ToUniversalTime());
        Assert.Equal(HttpStatusCode.Forbidden, isletmeOdemeleriGoremez.StatusCode);

        // Abonelik bitişi kaldırılabilir (süresiz).
        (await Durum(yonetici, isletme, aktif: true, abonelikBitisi: null)).EnsureSuccessStatusCode();
        Assert.Null((await admin.GetFromJsonAsync<IsletmeYaniti>("api/isletme"))!.AbonelikBitisi);
    }

    // --- Vulu yöneticileri ---

    [Fact]
    public async Task Yeni_vulu_yoneticisi_eklenir_giris_yapar_ve_devre_disi_kalinca_giremez()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var ad = "vy" + Guid.NewGuid().ToString("N")[..10];
        using var anonim = _api.CreateClient();

        using var ekle = await yonetici.PostAsJsonAsync("api/platform/yoneticiler", new VuluYoneticisiOlusturIstegi(ad, "Ikinci-Yonetici-1", "İkinci Yönetici"));
        var eklenen = (await ekle.Content.ReadFromJsonAsync<VuluYoneticisiYaniti>())!;
        using var ayniAd = await yonetici.PostAsJsonAsync("api/platform/yoneticiler", new VuluYoneticisiOlusturIstegi(ad.ToUpperInvariant(), "Ikinci-Yonetici-1", "Aynı Ad"));
        var oturum = await _api.GirisYap(ad, "Ikinci-Yonetici-1", IsletmeKurallari.PlatformKodu);
        using var ikinci = await _api.YetkiliIstemci(ad, "Ikinci-Yonetici-1", IsletmeKurallari.PlatformKodu);
        var gordugu = await ikinci.GetFromJsonAsync<List<IsletmeYaniti>>("api/platform/isletmeler");

        Assert.Equal(HttpStatusCode.Created, ekle.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, ayniAd.StatusCode);
        Assert.Null(oturum.Isletme);
        Assert.Equal("İkinci Yönetici", oturum.Kullanici.AdSoyad);
        Assert.Contains(gordugu!, i => i.Kod == ApiFabrikasi.IsletmeKodu);

        (await yonetici.PutAsJsonAsync($"api/platform/yoneticiler/{eklenen.Id}", new VuluYoneticisiGuncelleIstegi("İkinci Yönetici", false, null))).EnsureSuccessStatusCode();
        using var devreDisi = await anonim.PostAsJsonAsync("api/kimlik/giris", new GirisIstegi(IsletmeKurallari.PlatformKodu, ad, "Ikinci-Yonetici-1"));
        using var yenileme = await anonim.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(oturum.YenilemeTokeni));
        Assert.Equal(HttpStatusCode.Forbidden, devreDisi.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, yenileme.StatusCode);

        using var sil = await yonetici.DeleteAsync($"api/platform/yoneticiler/{eklenen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
    }

    [Fact]
    public async Task Vulu_yoneticisi_kendini_silemez_ve_isletme_personeli_yoneticileri_goremez()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        using var isletmeAdmini = await _api.YetkiliIstemci();
        var ben = (await yonetici.GetFromJsonAsync<List<VuluYoneticisiYaniti>>("api/platform/yoneticiler"))!
            .Single(y => y.KullaniciAdi == ApiFabrikasi.YoneticiAdi);

        using var kendiniSil = await yonetici.DeleteAsync($"api/platform/yoneticiler/{ben.Id}");
        using var personelListeler = await isletmeAdmini.GetAsync("api/platform/yoneticiler");
        using var personelEkler = await isletmeAdmini.PostAsJsonAsync("api/platform/yoneticiler",
            new VuluYoneticisiOlusturIstegi("sizan", "Gecerli-Sifre-1", "Sızan Kişi"));

        Assert.Equal(HttpStatusCode.Conflict, kendiniSil.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, personelListeler.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, personelEkler.StatusCode);
    }
}

// "En az bir aktif vulu yöneticisi kalmalı" kuralı diğer yöneticilere bağlı olduğu için kendi veritabanında çalışır.
public class SonVuluYoneticisiTestleri : IClassFixture<ApiFabrikasi>
{
    private readonly ApiFabrikasi _api;

    public SonVuluYoneticisiTestleri(ApiFabrikasi api) => _api = api;

    [Fact]
    public async Task Son_aktif_vulu_yoneticisi_devre_disi_birakilamaz()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var ben = Assert.Single((await yonetici.GetFromJsonAsync<List<VuluYoneticisiYaniti>>("api/platform/yoneticiler"))!);

        using var yanit = await yonetici.PutAsJsonAsync($"api/platform/yoneticiler/{ben.Id}",
            new VuluYoneticisiGuncelleIstegi(ben.AdSoyad, false, null));

        Assert.Equal(HttpStatusCode.Conflict, yanit.StatusCode);
    }
}
