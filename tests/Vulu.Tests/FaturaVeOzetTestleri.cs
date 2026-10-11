using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Vulu.Shared;

namespace Vulu.Tests;

public class FaturaBelgesiTestleri : IClassFixture<ApiFabrikasi>
{
    private const string Yol = "api/servis-kayitlari";

    private readonly ApiFabrikasi _api;

    public FaturaBelgesiTestleri(ApiFabrikasi api) => _api = api;

    private static async Task<ServisKaydiYaniti> TamamlanmisKayit(HttpClient admin)
    {
        using var musteriYaniti = await admin.PostAsJsonAsync("api/musteriler",
            new MusteriIstegi("Işıl Çağrı Öztürk", "05551112233", "isil@ornek.com", "Düzce Merkez"));
        var musteri = (await musteriYaniti.Content.ReadFromJsonAsync<MusteriYaniti>())!;
        using var kayitYaniti = await admin.PostAsJsonAsync(Yol, new ServisKaydiIstegi(musteri.Id, null, "Laptop Dell XPS", "Şarj girişi değişimi"));
        var kayit = (await kayitYaniti.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!;
        using var durum = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/durum", new ServisDurumuIstegi(ServisDurumu.Tamamlandi));
        durum.EnsureSuccessStatusCode();
        return kayit;
    }

    private static async Task<ServisKaydiYaniti> FaturaKes(HttpClient admin, string kayitId, decimal tutar, bool odendi = false)
    {
        using var yanit = await admin.PutAsJsonAsync($"{Yol}/{kayitId}/fatura", new FaturaIstegi(tutar, odendi));
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!;
    }

    [Fact]
    public async Task Fatura_numarasi_sirali_verilir_ve_guncellemeyle_degismez()
    {
        using var admin = await _api.YetkiliIstemci();
        var ilkKayit = await TamamlanmisKayit(admin);
        var ikinciKayit = await TamamlanmisKayit(admin);

        var ilk = await FaturaKes(admin, ilkKayit.Id, 100m);
        var ikinci = await FaturaKes(admin, ikinciKayit.Id, 200m);
        var guncellenen = await FaturaKes(admin, ilkKayit.Id, 150m, odendi: true);

        // Numaranın ön eki işletme kodundan gelir ("test-isletme" -> "TEST").
        Assert.Matches(@"^TEST-\d{4}-\d{6}$", ilk.Fatura!.No);
        Assert.Equal(Sira(ilk.Fatura.No) + 1, Sira(ikinci.Fatura!.No));
        Assert.Equal(ilk.Fatura.No, guncellenen.Fatura!.No);

        static int Sira(string no) => int.Parse(no[^6..], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Fatura_pdf_olarak_indirilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await TamamlanmisKayit(admin);
        var faturali = await FaturaKes(admin, kayit.Id, 1250.50m, odendi: true);

        using var yanit = await admin.GetAsync($"{Yol}/{kayit.Id}/fatura/pdf");
        var icerik = await yanit.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Equal("application/pdf", yanit.Content.Headers.ContentType?.MediaType);
        Assert.Contains(faturali.Fatura!.No, yanit.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(icerik, 0, 5));
        Assert.True(icerik.Length > 2000, $"PDF beklenenden küçük: {icerik.Length} bayt");
    }

    [Fact]
    public async Task Faturasi_olmayan_kaydin_pdf_i_yoktur_ve_giris_yapmadan_indirilemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await TamamlanmisKayit(admin);
        using var anonim = _api.CreateClient();

        using var faturasiz = await admin.GetAsync($"{Yol}/{kayit.Id}/fatura/pdf");
        using var girissiz = await anonim.GetAsync($"{Yol}/{kayit.Id}/fatura/pdf");

        Assert.Equal(HttpStatusCode.NotFound, faturasiz.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, girissiz.StatusCode);
    }
}

// Özet tüm veritabanını saydığı için başka testlerin kayıtlarıyla karışmasın diye kendi veritabanında çalışır.
public class OzetTestleri : IClassFixture<ApiFabrikasi>
{
    private readonly ApiFabrikasi _api;

    public OzetTestleri(ApiFabrikasi api) => _api = api;

    [Fact]
    public async Task Ozet_acik_kayitlari_bugunku_randevulari_ve_fatura_toplamlarini_verir()
    {
        using var admin = await _api.YetkiliIstemci();
        var gun = DateTime.UtcNow.Date;
        var ay = new DateTime(gun.Year, gun.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        using var musteriYaniti = await admin.PostAsJsonAsync("api/musteriler", new MusteriIstegi("Özet Müşterisi", "05556667788", null, null));
        var musteri = (await musteriYaniti.Content.ReadFromJsonAsync<MusteriYaniti>())!;

        async Task<string> Kayit(ServisDurumu durum, decimal? tutar = null, bool odendi = false)
        {
            using var ekle = await admin.PostAsJsonAsync("api/servis-kayitlari", new ServisKaydiIstegi(musteri.Id, null, "Cihaz", "Sorun"));
            var kayit = (await ekle.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!;
            if (durum != ServisDurumu.Beklemede)
                (await admin.PutAsJsonAsync($"api/servis-kayitlari/{kayit.Id}/durum", new ServisDurumuIstegi(durum))).EnsureSuccessStatusCode();
            if (tutar is not null)
                (await admin.PutAsJsonAsync($"api/servis-kayitlari/{kayit.Id}/fatura", new FaturaIstegi(tutar.Value, odendi))).EnsureSuccessStatusCode();
            return kayit.Id;
        }

        await Kayit(ServisDurumu.Beklemede);
        await Kayit(ServisDurumu.Beklemede);
        await Kayit(ServisDurumu.DevamEdiyor);
        await Kayit(ServisDurumu.IptalEdildi);
        await Kayit(ServisDurumu.Tamamlandi, 100.10m);
        await Kayit(ServisDurumu.Tamamlandi, 200.20m);
        await Kayit(ServisDurumu.Tamamlandi, 500m, odendi: true);

        // Bugün için ileri bir saat seçilir; gün bitmek üzereyse yarının başına taşar ve "bugün" sayılmaz.
        var bugunSaat = DateTime.UtcNow.AddMinutes(5);
        var bugunIcinde = bugunSaat < gun.AddDays(1);
        (await admin.PostAsJsonAsync("api/randevular", new RandevuIstegi(musteri.Id, null, null, bugunSaat, 30, null))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("api/randevular", new RandevuIstegi(musteri.Id, null, null, gun.AddDays(3), 30, null))).EnsureSuccessStatusCode();

        var sorgu = $"gunBaslangici={Uri.EscapeDataString(gun.ToString("o"))}&ayBaslangici={Uri.EscapeDataString(ay.ToString("o"))}";
        var ozet = await admin.GetFromJsonAsync<OzetYaniti>($"api/ozet?{sorgu}");

        Assert.Equal(2, ozet!.BekleyenKayit);
        Assert.Equal(1, ozet.DevamEdenKayit);
        Assert.Equal(bugunIcinde ? 1 : 0, ozet.BugunkuRandevu);
        Assert.Equal(2, ozet.OdenmemisFatura);
        Assert.Equal(300.30m, ozet.OdenmemisTutar);
        Assert.Equal(500m, ozet.BuAyTahsilat);
        Assert.Equal(2, ozet.YaklasanRandevular.Count);
        Assert.Equal(3, ozet.AcikKayitlar.Count);
        Assert.All(ozet.AcikKayitlar, k => Assert.True(k.Durum is ServisDurumu.Beklemede or ServisDurumu.DevamEdiyor));
    }

    [Fact]
    public async Task Tarih_sinirlari_verilmezse_400_doner()
    {
        using var admin = await _api.YetkiliIstemci();

        using var yanit = await admin.GetAsync("api/ozet");

        Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
    }
}
