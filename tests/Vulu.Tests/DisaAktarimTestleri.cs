using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Tests;

// Excel'e aktarma ve kişisel veri (KVKK) işlemleri.
public class DisaAktarimTestleri : IClassFixture<ApiFabrikasi>
{
    private static readonly XNamespace Ana = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private readonly ApiFabrikasi _api;

    public DisaAktarimTestleri(ApiFabrikasi api) => _api = api;

    private static string Ek() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<MusteriYaniti> MusteriEkle(HttpClient istemci, string ad, string? eposta = null)
    {
        using var yanit = await istemci.PostAsJsonAsync("api/musteriler", new MusteriIstegi(ad, "05557654321", eposta, "Düzce"));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<MusteriYaniti>())!;
    }

    private static async Task<ServisKaydiYaniti> KayitEkle(HttpClient istemci, string musteriId, string? seriNo = null)
    {
        using var yanit = await istemci.PostAsJsonAsync("api/servis-kayitlari",
            new ServisKaydiIstegi(musteriId, null, "Laptop", "Açılmıyor", "Dell", "XPS", seriNo));
        yanit.EnsureSuccessStatusCode();
        return (await yanit.Content.ReadFromJsonAsync<ServisKaydiYaniti>())!;
    }

    // Excel dosyasını açar: sayfa adları ve her sayfanın hücre metinleri (satır satır).
    private static (List<string> Sayfalar, List<List<List<string>>> Hucreler, List<XDocument> Belgeler) Ac(byte[] dosya)
    {
        using var zip = new ZipArchive(new MemoryStream(dosya), ZipArchiveMode.Read);
        XDocument Oku(string yol)
        {
            using var akis = zip.GetEntry(yol)!.Open();
            return XDocument.Load(akis);
        }

        var sayfaAdlari = Oku("xl/workbook.xml").Descendants(Ana + "sheet").Select(s => s.Attribute("name")!.Value).ToList();
        var belgeler = Enumerable.Range(1, sayfaAdlari.Count).Select(i => Oku($"xl/worksheets/sheet{i}.xml")).ToList();
        var hucreler = belgeler
            .Select(b => b.Descendants(Ana + "row")
                .Select(satir => satir.Elements(Ana + "c").Select(h => h.Value).ToList()).ToList())
            .ToList();

        // Dosyanın diğer parçaları da geçerli XML olmalı.
        foreach (var yol in new[] { "[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml" })
            Oku(yol);

        return (sayfaAdlari, hucreler, belgeler);
    }

    private static async Task<byte[]> Indir(HttpClient istemci, string yol)
    {
        using var yanit = await istemci.GetAsync(yol);
        Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        Assert.Equal(ExcelYazici.IcerikTuru, yanit.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".xlsx", yanit.Content.Headers.ContentDisposition?.FileNameStar ?? yanit.Content.Headers.ContentDisposition?.FileName);
        return await yanit.Content.ReadAsByteArrayAsync();
    }

    // --- Excel ---

    [Fact]
    public void Excel_dosyasi_metni_sayiyi_tarihi_ve_bos_hucreyi_dogru_yazar()
    {
        var tarih = new DateTime(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);
        var dosya = ExcelYazici.Olustur(
            new ExcelSayfasi("Öz/et: [1]*?", ["Ad", "Tutar", "Tarih", "Adet", "Evet mi", "Boş"],
                [["Işıl Çağrı\u0001 <b>&\"", 1250.50m, tarih, 3, true, null]]),
            new ExcelSayfasi("", ["Tek sütun"], []));

        var (sayfalar, hucreler, belgeler) = Ac(dosya);
        var satir = belgeler[0].Descendants(Ana + "row").ElementAt(1).Elements(Ana + "c").ToList();

        // Sayfa adındaki yasak karakterler atılır; boş ad yerine sıra numaralı bir ad verilir.
        Assert.Equal(new[] { "Özet 1", "Sayfa2" }, sayfalar);
        Assert.Equal(new[] { "Ad", "Tutar", "Tarih", "Adet", "Evet mi", "Boş" }, hucreler[0][0]);

        // Denetim karakteri atılır, özel karakterler metin olarak korunur.
        Assert.Equal("Işıl Çağrı <b>&\"", satir[0].Value);
        Assert.Equal("inlineStr", satir[0].Attribute("t")!.Value);
        Assert.Equal("1250.5", satir[1].Value);
        // 11.10.2026 12:30 (Türkiye saati), Excel'in gün sayısı olarak.
        Assert.Equal(new DateTime(2026, 10, 11, 12, 30, 0).ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture), satir[2].Value);
        Assert.Equal("3", satir[3].Value);
        Assert.Equal("Evet", satir[4].Value);
        // Boş değer için hücre yazılmaz.
        Assert.Equal(5, satir.Count);
    }

    [Fact]
    public async Task Formul_gibi_gorunen_musteri_adi_excelde_formul_olmaz()
    {
        using var admin = await _api.YetkiliIstemci();
        const string zararli = "=HYPERLINK(\"http://kotu.example\",\"tıkla\")";
        await MusteriEkle(admin, zararli);
        await MusteriEkle(admin, "+90 ile başlayan ad");

        var (_, hucreler, belgeler) = Ac(await Indir(admin, "api/musteriler/disa-aktar"));

        Assert.Contains(hucreler[0], satir => satir[0] == zararli);
        // Hiçbir hücrede formül yok; kullanıcının yazdığı her şey düz metin türünde.
        Assert.Empty(belgeler[0].Descendants(Ana + "f"));
        Assert.All(belgeler[0].Descendants(Ana + "c").Where(h => h.Value == zararli),
            hucre => Assert.Equal("inlineStr", hucre.Attribute("t")!.Value));
    }

    [Fact]
    public async Task Listeler_excele_aktarilir_musteri_verisi_iceren_listeleri_yalnizca_yonetici_alir()
    {
        using var admin = await _api.YetkiliIstemci();
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin);
        using var usta = await _api.YetkiliIstemci(teknisyen.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
        var ad = "Aktarım Müşterisi " + Ek();
        var musteri = await MusteriEkle(admin, ad);
        var kayit = await KayitEkle(admin, musteri.Id);
        using var parca = await admin.PostAsJsonAsync("api/parcalar", new ParcaIstegi("Aktarım parçası " + Ek(), "KOD-1", 99.90m, 1));
        parca.EnsureSuccessStatusCode();

        var musteriler = Ac(await Indir(admin, "api/musteriler/disa-aktar"));
        var kayitlar = Ac(await Indir(admin, $"api/servis-kayitlari/disa-aktar?arama={Uri.EscapeDataString(ad)}"));
        var stok = Ac(await Indir(usta, "api/parcalar/disa-aktar"));
        var simdi = DateTime.UtcNow;
        var rapor = Ac(await Indir(admin,
            $"api/raporlar/disa-aktar?baslangic={Uri.EscapeDataString(simdi.AddDays(-1).ToString("o"))}&bitis={Uri.EscapeDataString(simdi.AddDays(1).ToString("o"))}"));

        using var ustaMusteriler = await usta.GetAsync("api/musteriler/disa-aktar");
        using var ustaKayitlar = await usta.GetAsync("api/servis-kayitlari/disa-aktar");
        using var ustaRapor = await usta.GetAsync("api/raporlar/disa-aktar");
        using var anonim = _api.CreateClient();
        using var oturumsuz = await anonim.GetAsync("api/musteriler/disa-aktar");

        Assert.Contains(musteriler.Hucreler[0], satir => satir[0] == ad && satir[1] == "05557654321");
        // Süzgeç dışa aktarmada da geçerli: yalnızca aranan müşterinin kaydı.
        Assert.Equal(2, kayitlar.Hucreler[0].Count);
        Assert.Equal(kayit.No, kayitlar.Hucreler[0][1][0]);
        Assert.Contains(stok.Hucreler[0], satir => satir.Contains("KOD-1"));
        Assert.Equal(new[] { "Özet", "Teknisyenler", "Aylar", "Parçalar" }, rapor.Sayfalar);

        Assert.Equal(HttpStatusCode.Forbidden, ustaMusteriler.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ustaKayitlar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ustaRapor.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oturumsuz.StatusCode);
    }
}
