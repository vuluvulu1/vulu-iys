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
public class KvkkVeDisaAktarimTestleri : IClassFixture<ApiFabrikasi>
{
    private static readonly XNamespace Ana = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private readonly ApiFabrikasi _api;

    public KvkkVeDisaAktarimTestleri(ApiFabrikasi api) => _api = api;

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

    // --- Kişisel veriler ---

    [Fact]
    public async Task Musteri_verisinin_dokumu_musteriyi_kayitlarini_ve_randevularini_icerir()
    {
        using var admin = await _api.YetkiliIstemci();
        var musteri = await MusteriEkle(admin, "Döküm Müşterisi " + Ek(), "dokum@ornek.com");
        var kayit = await KayitEkle(admin, musteri.Id, "DOKUM-1");
        using var randevu = await admin.PostAsJsonAsync("api/randevular",
            new RandevuIstegi(musteri.Id, null, null, DateTime.UtcNow.AddDays(3), 60, "Kontrol"));
        randevu.EnsureSuccessStatusCode();

        var dokum = Ac(await Indir(admin, $"api/kisisel-veriler/musteriler/{musteri.Id}/dokum"));

        Assert.Equal(new[] { "Müşteri", "Servis kayıtları", "Randevular" }, dokum.Sayfalar);
        Assert.Contains(dokum.Hucreler[0], satir => satir.SequenceEqual(["E-posta", "dokum@ornek.com"]));
        Assert.Contains(dokum.Hucreler[1], satir => satir[0] == kayit.No && satir.Contains("DOKUM-1"));
        Assert.Contains(dokum.Hucreler[2], satir => satir.Contains("Kontrol"));
    }

    [Fact]
    public async Task Kalici_silme_musteriyi_siler_ve_kayitlarini_anonimlestirir()
    {
        using var admin = await _api.YetkiliIstemci();
        var ad = "Unutulacak Kişi " + Ek();
        var musteri = await MusteriEkle(admin, ad, "unut@ornek.com");
        var kayit = await KayitEkle(admin, musteri.Id, "IMEI-" + Ek());
        using var not = await admin.PostAsJsonAsync($"api/servis-kayitlari/{kayit.Id}/notlar", new NotIstegi($"{ad} arandı, evde yoktu"));
        using var iptal = await admin.PutAsJsonAsync($"api/servis-kayitlari/{kayit.Id}/durum", new ServisDurumuIstegi(ServisDurumu.IptalEdildi));
        using var randevu = await admin.PostAsJsonAsync("api/randevular",
            new RandevuIstegi(musteri.Id, null, null, DateTime.UtcNow.AddDays(2), 60, null));
        var eklenenRandevu = (await randevu.Content.ReadFromJsonAsync<RandevuYaniti>())!;
        using var randevuIptali = await admin.PutAsJsonAsync($"api/randevular/{eklenenRandevu.Id}/durum", new RandevuDurumuIstegi(RandevuDurumu.IptalEdildi));
        randevuIptali.EnsureSuccessStatusCode();

        using var silme = await admin.DeleteAsync($"api/kisisel-veriler/musteriler/{musteri.Id}");

        var kalanKayit = await admin.GetFromJsonAsync<ServisKaydiYaniti>($"api/servis-kayitlari/{kayit.Id}");
        var gecmis = await admin.GetFromJsonAsync<IslemYaniti[]>($"api/servis-kayitlari/{kayit.Id}/gecmis");
        var adlaArama = await admin.GetFromJsonAsync<Sayfa<ServisKaydiYaniti>>($"api/servis-kayitlari?arama={Uri.EscapeDataString(ad)}");
        var cihazlaArama = await admin.GetFromJsonAsync<Sayfa<ServisKaydiYaniti>>($"api/servis-kayitlari?arama={kayit.No}");
        using var musteriIstegi = await admin.GetAsync($"api/musteriler/{musteri.Id}");
        using var ikinciSilme = await admin.DeleteAsync($"api/kisisel-veriler/musteriler/{musteri.Id}");

        Assert.Equal(HttpStatusCode.NoContent, silme.StatusCode);
        // Kayıt duruyor ama müşteriyi tanıtan hiçbir şey kalmadı.
        Assert.Equal("Silinen müşteri", kalanKayit!.MusteriAdi);
        Assert.Null(kalanKayit.MusteriTelefonu);
        Assert.Null(kalanKayit.SeriNo);
        Assert.Equal("Laptop", kalanKayit.CihazBilgisi);
        var tekSatir = Assert.Single(gecmis!);
        Assert.DoesNotContain(ad, tekSatir.Aciklama);
        Assert.Empty(adlaArama!.Ogeler);
        Assert.Single(cihazlaArama!.Ogeler);
        Assert.Equal(HttpStatusCode.NotFound, musteriIstegi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ikinciSilme.StatusCode);

        // Veritabanında da müşteri belgesi ve randevuları yok.
        Assert.False(await _api.Veritabani.Musteriler.Find(m => m.Id == musteri.Id).AnyAsync());
        Assert.False(await _api.Veritabani.Randevular.Find(r => r.MusteriId == musteri.Id).AnyAsync());
    }

    [Fact]
    public async Task Acik_isi_ya_da_saklama_suresi_dolmamis_faturasi_olan_musteri_kalici_silinemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin);
        using var usta = await _api.YetkiliIstemci(teknisyen.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
        var musteri = await MusteriEkle(admin, "Faturalı Müşteri " + Ek());
        var kayit = await KayitEkle(admin, musteri.Id);
        var yol = $"api/kisisel-veriler/musteriler/{musteri.Id}";

        using var acikken = await admin.DeleteAsync(yol);

        using var tamam = await admin.PutAsJsonAsync($"api/servis-kayitlari/{kayit.Id}/durum", new ServisDurumuIstegi(ServisDurumu.Tamamlandi));
        using var fatura = await admin.PutAsJsonAsync($"api/servis-kayitlari/{kayit.Id}/fatura", new FaturaIstegi(500m, true));
        fatura.EnsureSuccessStatusCode();
        using var faturaYeniyken = await admin.DeleteAsync(yol);
        var gerekce = await faturaYeniyken.Content.ReadAsStringAsync();
        using var teknisyenIstegi = await usta.DeleteAsync(yol);

        // Fatura on bir yıl önce kesilmiş olsaydı (varsayılan saklama süresi on yıl) silinebilirdi.
        await _api.Veritabani.ServisKayitlari.UpdateOneAsync(s => s.Id == kayit.Id,
            Builders<ServisKaydi>.Update.Set(s => s.Fatura!.OlusturmaTarihi, DateTime.UtcNow.AddYears(-11)));
        using var sureDolunca = await admin.DeleteAsync(yol);
        var faturaliKayit = await admin.GetFromJsonAsync<ServisKaydiYaniti>($"api/servis-kayitlari/{kayit.Id}");

        Assert.Equal(HttpStatusCode.Conflict, acikken.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, faturaYeniyken.StatusCode);
        Assert.Contains("10 yıl", gerekce);
        Assert.Equal(HttpStatusCode.Forbidden, teknisyenIstegi.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, sureDolunca.StatusCode);
        // Faturanın kendisi (tutar, numara) gelir kaydı olarak kalır.
        Assert.Equal(500m, faturaliKayit!.Fatura!.Tutar);
        Assert.Equal("Silinen müşteri", faturaliKayit.MusteriAdi);
    }

    [Fact]
    public async Task Kisisel_veriler_ekrani_silinmis_ve_saklama_suresi_dolmus_musterileri_listeler()
    {
        // Listeler başka testlerin müşterileriyle karışmasın diye ayrı bir işletmede çalışır.
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = "kvkk-" + Ek();
        await ApiFabrikasi.IsletmeAc(yonetici, kod, "KVKK İşletmesi");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin);
        using var usta = await _api.YetkiliIstemci(teknisyen.KullaniciAdi, ApiFabrikasi.PersonelSifresi, kod);

        var guncel = await MusteriEkle(admin, "Güncel Müşteri");
        var silinmis = await MusteriEkle(admin, "Silinmiş Müşteri");
        var eski = await MusteriEkle(admin, "Eski Müşteri");
        var eskiAmaGeriGelen = await MusteriEkle(admin, "Eski Ama Yeni İşi Olan");
        using var silme = await admin.DeleteAsync($"api/musteriler/{silinmis.Id}");
        silme.EnsureSuccessStatusCode();

        // İki müşterinin kaydı on bir yıl önce açılmış gibi; birinin yeni bir servis kaydı var.
        var isletme = await _api.Platform.Isletmeler.Find(i => i.Kod == kod).SingleAsync();
        var db = _api.Platform.IsletmeBaglami(isletme);
        await db.Musteriler.UpdateManyAsync(m => m.Id == eski.Id || m.Id == eskiAmaGeriGelen.Id,
            Builders<Musteri>.Update.Set(m => m.OlusturmaTarihi, DateTime.UtcNow.AddYears(-11)));
        await KayitEkle(admin, eskiAmaGeriGelen.Id);

        var durum = await admin.GetFromJsonAsync<KvkkDurumu>("api/kisisel-veriler");
        using var teknisyenIstegi = await usta.GetAsync("api/kisisel-veriler");

        Assert.Equal(10, durum!.SaklamaYili);
        Assert.Equal(silinmis.Id, Assert.Single(durum.Silinmisler).Id);
        Assert.Equal(eski.Id, Assert.Single(durum.SuresiDolanlar).Id);
        Assert.DoesNotContain(durum.SuresiDolanlar, m => m.Id == guncel.Id);
        Assert.Equal(HttpStatusCode.Forbidden, teknisyenIstegi.StatusCode);

        // Silinmiş olarak işaretlenmiş müşteri de kalıcı olarak silinebilir.
        using var kaliciSilme = await admin.DeleteAsync($"api/kisisel-veriler/musteriler/{silinmis.Id}");
        var sonra = await admin.GetFromJsonAsync<KvkkDurumu>("api/kisisel-veriler");
        Assert.Equal(HttpStatusCode.NoContent, kaliciSilme.StatusCode);
        Assert.Empty(sonra!.Silinmisler);
    }

    [Fact]
    public async Task Saklama_suresi_ve_aydinlatma_metni_isletme_profilinde_tutulur_ve_forma_yazilir()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = "metin-" + Ek();
        await ApiFabrikasi.IsletmeAc(yonetici, kod, "Metin İşletmesi");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);

        IsletmeProfilIstegi Profil(int saklamaYili, string? metin) =>
            new("Metin İşletmesi", null, null, null, null, null, 20, null, null, false, null, saklamaYili, metin);

        using var guncelleme = await admin.PutAsJsonAsync("api/isletme", Profil(5, "  Kendi aydınlatma metnimiz.  "));
        var profil = (await guncelleme.Content.ReadFromJsonAsync<IsletmeYaniti>())!;
        using var gecersiz = await admin.PutAsJsonAsync("api/isletme", Profil(0, null));
        var durum = await admin.GetFromJsonAsync<KvkkDurumu>("api/kisisel-veriler");

        var musteri = await MusteriEkle(admin, "Form Müşterisi");
        var kayit = await KayitEkle(admin, musteri.Id);
        using var form = await admin.GetAsync($"api/servis-kayitlari/{kayit.Id}/kabul-formu");

        Assert.Equal(5, profil.SaklamaYili);
        Assert.Equal("Kendi aydınlatma metnimiz.", profil.AydinlatmaMetni);
        Assert.Equal(HttpStatusCode.BadRequest, gecersiz.StatusCode);
        Assert.Equal(5, durum!.SaklamaYili);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
    }
}
