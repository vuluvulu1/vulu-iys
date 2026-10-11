using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Api.Kurallar;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Tests;

// Cihaz ayrıntısı, işlem geçmişi, formlar, teklif, parça/stok, rapor, takip sayfası ve bildirim.
public class ServisGenislemeTestleri : IClassFixture<ApiFabrikasi>
{
    private const string Yol = "api/servis-kayitlari";

    private readonly ApiFabrikasi _api;

    public ServisGenislemeTestleri(ApiFabrikasi api) => _api = api;

    private static async Task<T> Oku<T>(HttpResponseMessage yanit, HttpStatusCode beklenen = HttpStatusCode.OK)
    {
        Assert.Equal(beklenen, yanit.StatusCode);
        return (await yanit.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<MusteriYaniti> MusteriEkle(HttpClient istemci, string ad = "Gizli Müşteri Adı")
    {
        using var yanit = await istemci.PostAsJsonAsync("api/musteriler",
            new MusteriIstegi(ad, "05551112233", "musteri@ornek.com", "Düzce"));
        return await Oku<MusteriYaniti>(yanit, HttpStatusCode.Created);
    }

    private static async Task<ServisKaydiYaniti> KayitEkle(HttpClient istemci, string? seriNo = null, string? teknisyenId = null,
        bool garanti = false)
    {
        var musteri = await MusteriEkle(istemci);
        using var yanit = await istemci.PostAsJsonAsync(Yol, new ServisKaydiIstegi(musteri.Id, teknisyenId, "Laptop", "Açılmıyor",
            "Dell", "XPS 13", seriNo, garanti, "Şarj aleti, çanta", "Kapakta çizik var"));
        return await Oku<ServisKaydiYaniti>(yanit, HttpStatusCode.Created);
    }

    private static async Task<ServisKaydiYaniti> Durum(HttpClient istemci, string id, ServisDurumu durum)
    {
        using var yanit = await istemci.PutAsJsonAsync($"{Yol}/{id}/durum", new ServisDurumuIstegi(durum));
        return await Oku<ServisKaydiYaniti>(yanit);
    }

    private static async Task<ParcaYaniti> ParcaEkle(HttpClient admin, string ad, decimal fiyat, int stok, int kritik = 1)
    {
        using var yanit = await admin.PostAsJsonAsync("api/parcalar", new ParcaIstegi(ad, null, fiyat, kritik));
        var parca = await Oku<ParcaYaniti>(yanit, HttpStatusCode.Created);
        if (stok == 0) return parca;

        using var giris = await admin.PostAsJsonAsync($"api/parcalar/{parca.Id}/stok", new StokHareketiIstegi(stok, null));
        return await Oku<ParcaYaniti>(giris);
    }

    private static Task<IslemYaniti[]?> Gecmis(HttpClient istemci, string id) =>
        istemci.GetFromJsonAsync<IslemYaniti[]>($"{Yol}/{id}/gecmis");

    // --- Cihaz ayrıntısı ve kayıt numarası ---

    [Fact]
    public async Task Kayit_cihaz_ayrintilariyla_numara_ve_takip_kodu_alarak_acilir()
    {
        using var admin = await _api.YetkiliIstemci();

        var ilk = await KayitEkle(admin, " sn-abc-123 ", garanti: true);
        var ikinci = await KayitEkle(admin);

        Assert.Matches(@"^SK-\d{4}-\d{5}$", ilk.No);
        Assert.Equal(int.Parse(ilk.No[^5..], CultureInfo.InvariantCulture) + 1, int.Parse(ikinci.No[^5..], CultureInfo.InvariantCulture));
        Assert.Matches("^[A-Z2-9]{10}$", ilk.TakipKodu);
        Assert.NotEqual(ilk.TakipKodu, ikinci.TakipKodu);

        Assert.Equal("Dell", ilk.Marka);
        Assert.Equal("XPS 13", ilk.Model);
        Assert.Equal("SN-ABC-123", ilk.SeriNo);
        Assert.True(ilk.GarantiKapsaminda);
        Assert.Equal("Şarj aleti, çanta", ilk.Aksesuarlar);
        Assert.Equal("Kapakta çizik var", ilk.FizikselDurum);
    }

    [Fact]
    public async Task Ayni_cihazin_kayitlari_seri_numarasiyla_ve_kayit_numarasiyla_bulunur()
    {
        using var admin = await _api.YetkiliIstemci();
        var seri = "IMEI" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var ilk = await KayitEkle(admin, seri);
        var ikinci = await KayitEkle(admin, seri.ToLowerInvariant());
        await KayitEkle(admin, "BASKA-" + seri);

        var ayniCihaz = await admin.GetFromJsonAsync<Sayfa<ServisKaydiYaniti>>($"{Yol}?seriNo={seri.ToLowerInvariant()}");
        var numarayla = await admin.GetFromJsonAsync<Sayfa<ServisKaydiYaniti>>($"{Yol}?arama={ilk.No.ToLowerInvariant()}");

        Assert.Equal(new[] { ikinci.Id, ilk.Id }, ayniCihaz!.Ogeler.Select(k => k.Id));
        Assert.Equal(ilk.Id, Assert.Single(numarayla!.Ogeler).Id);
    }

    // --- İşlem geçmişi ---

    [Fact]
    public async Task Islem_gecmisi_kimin_neyi_degistirdigini_tutar()
    {
        using var admin = await _api.YetkiliIstemci();
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin, "Geçmiş Ustası");
        using var usta = await _api.YetkiliIstemci(teknisyen.KullaniciAdi, ApiFabrikasi.PersonelSifresi);

        var kayit = await KayitEkle(admin);
        await Durum(usta, kayit.Id, ServisDurumu.DevamEdiyor);
        using var duzenleme = await usta.PutAsJsonAsync($"{Yol}/{kayit.Id}",
            new ServisKaydiIstegi(kayit.MusteriId, teknisyen.Id, "Laptop", "Açılmıyor, fan sesi var", "Dell", "XPS 15"));
        duzenleme.EnsureSuccessStatusCode();
        using var not = await usta.PostAsJsonAsync($"{Yol}/{kayit.Id}/notlar", new NotIstegi(" Müşteri arandı, parça bekleniyor "));
        var notSonrasi = await Oku<IslemYaniti[]>(not);

        var gecmis = (await Gecmis(admin, kayit.Id))!;

        // En yeni üstte.
        Assert.Equal(new[] { IslemTuru.Not, IslemTuru.KayitGuncellendi, IslemTuru.DurumDegisti, IslemTuru.KayitAcildi },
            gecmis.Select(g => g.Tur));
        Assert.Equal(gecmis.Length, notSonrasi.Length);
        Assert.Equal("Müşteri arandı, parça bekleniyor", gecmis[0].Aciklama);
        Assert.Equal("Geçmiş Ustası", gecmis[0].KullaniciAdi);
        Assert.Contains("Model: XPS 13 → XPS 15", gecmis[1].Aciklama);
        Assert.Contains("Teknisyen: atanmadı → Geçmiş Ustası", gecmis[1].Aciklama);
        Assert.Contains("Sorun değişti", gecmis[1].Aciklama);
        Assert.Equal("Beklemede → Devam ediyor", gecmis[2].Aciklama);
        Assert.Equal("İşletme Yöneticisi", gecmis[3].KullaniciAdi);
    }

    [Fact]
    public async Task Degisiklik_icermeyen_kayit_gecmise_satir_eklemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin);

        await Durum(admin, kayit.Id, ServisDurumu.Beklemede);
        using var ayni = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}", new ServisKaydiIstegi(kayit.MusteriId, null, "Laptop",
            "Açılmıyor", "Dell", "XPS 13", null, false, "Şarj aleti, çanta", "Kapakta çizik var"));
        ayni.EnsureSuccessStatusCode();

        Assert.Equal(IslemTuru.KayitAcildi, Assert.Single((await Gecmis(admin, kayit.Id))!).Tur);
    }

    // --- Durum ve teslim ---

    [Fact]
    public async Task Teslim_edilen_kayit_tamamlanma_tarihini_korur_ve_faturasi_kesilebilir()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin);

        var tamam = await Durum(admin, kayit.Id, ServisDurumu.Tamamlandi);
        using var fatura = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/fatura", new FaturaIstegi(500m, true));
        fatura.EnsureSuccessStatusCode();
        var teslim = await Durum(admin, kayit.Id, ServisDurumu.TeslimEdildi);
        using var yenidenAc = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/durum", new ServisDurumuIstegi(ServisDurumu.DevamEdiyor));

        Assert.NotNull(teslim.TeslimTarihi);
        // Veritabanı tarihleri milisaniye hassasiyetinde saklar.
        Assert.Equal(tamam.TamamlanmaTarihi!.Value, teslim.TamamlanmaTarihi!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(HttpStatusCode.Conflict, yenidenAc.StatusCode);
    }

    [Fact]
    public void Durum_kurallari_tamamlanma_ve_teslim_tarihlerini_yonetir()
    {
        var simdi = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
        var kayit = new ServisKaydi();

        ServisKaydiKurallari.DurumuDegistir(kayit, ServisDurumu.TeslimEdildi, simdi);
        Assert.Equal(simdi, kayit.TamamlanmaTarihi);
        Assert.Equal(simdi, kayit.TeslimTarihi);

        ServisKaydiKurallari.DurumuDegistir(kayit, ServisDurumu.Tamamlandi, simdi.AddHours(1));
        Assert.Equal(simdi, kayit.TamamlanmaTarihi);
        Assert.Null(kayit.TeslimTarihi);

        ServisKaydiKurallari.DurumuDegistir(kayit, ServisDurumu.IptalEdildi, simdi.AddHours(2));
        Assert.Null(kayit.TamamlanmaTarihi);
    }

    // --- Teklif ---

    [Fact]
    public async Task Teklif_verilince_kayit_onay_bekler_onaylaninca_is_baslar()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin);

        using var teklifYaniti = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/teklif", new TeklifIstegi(1500m, "Anakart onarımı"));
        var teklifli = await Oku<ServisKaydiYaniti>(teklifYaniti);
        using var onayYaniti = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/teklif/yanit", new TeklifYanitIstegi(true, "Telefonla onayladı"));
        var onayli = await Oku<ServisKaydiYaniti>(onayYaniti);
        using var ikinciYanit = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/teklif/yanit", new TeklifYanitIstegi(false, null));

        Assert.Equal(ServisDurumu.OnayBekliyor, teklifli.Durum);
        Assert.Equal(TeklifDurumu.Bekliyor, teklifli.Teklif!.Durum);
        Assert.Equal(ServisDurumu.DevamEdiyor, onayli.Durum);
        Assert.Equal(TeklifDurumu.Onaylandi, onayli.Teklif!.Durum);
        Assert.Equal("Telefonla onayladı", onayli.Teklif.YanitNotu);
        Assert.NotNull(onayli.Teklif.YanitTarihi);
        Assert.Equal(HttpStatusCode.Conflict, ikinciYanit.StatusCode);

        var gecmis = (await Gecmis(admin, kayit.Id))!;
        Assert.Equal(IslemTuru.TeklifYanitlandi, gecmis[0].Tur);
        Assert.Contains("onayladı – Telefonla onayladı", gecmis[0].Aciklama);
    }

    [Fact]
    public async Task Reddedilen_teklif_kaydi_iptal_eder_ve_kapali_kayda_teklif_verilemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin);

        using var teklif = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/teklif", new TeklifIstegi(900m, null));
        teklif.EnsureSuccessStatusCode();
        using var ret = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/teklif/yanit", new TeklifYanitIstegi(false, null));
        var reddedilen = await Oku<ServisKaydiYaniti>(ret);
        using var yeniTeklif = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/teklif", new TeklifIstegi(500m, null));
        using var gecersiz = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/teklif", new TeklifIstegi(0m, null));

        Assert.Equal(ServisDurumu.IptalEdildi, reddedilen.Durum);
        Assert.Equal(TeklifDurumu.Reddedildi, reddedilen.Teklif!.Durum);
        Assert.Equal(HttpStatusCode.Conflict, yeniTeklif.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, gecersiz.StatusCode);
    }

    // --- Stok ve kullanılan parçalar ---

    [Fact]
    public async Task Kullanilan_parca_stoktan_duser_cikarilinca_geri_gelir()
    {
        using var admin = await _api.YetkiliIstemci();
        var parca = await ParcaEkle(admin, "Ekran paneli " + Guid.NewGuid().ToString("N")[..6], 1200.50m, stok: 5);
        var kayit = await KayitEkle(admin);

        using var ekleme = await admin.PostAsJsonAsync($"{Yol}/{kayit.Id}/parcalar", new ParcaKullanimIstegi(parca.Id, 2));
        var parcali = await Oku<ServisKaydiYaniti>(ekleme);
        var kullanimSonrasi = await admin.GetFromJsonAsync<ParcaYaniti>($"api/parcalar/{parca.Id}");

        var satir = Assert.Single(parcali.Parcalar);
        Assert.Equal((parca.Ad, 2, 1200.50m), (satir.Ad, satir.Adet, satir.BirimFiyat));
        Assert.Equal(2401m, parcali.ParcaToplami);
        Assert.Equal(3, kullanimSonrasi!.StokAdedi);

        // Stok kartının fiyatı sonradan değişse de kayıttaki fiyat değişmez.
        using var zam = await admin.PutAsJsonAsync($"api/parcalar/{parca.Id}", new ParcaIstegi(parca.Ad, "KOD-1", 2000m, 1));
        zam.EnsureSuccessStatusCode();
        var zamSonrasi = await admin.GetFromJsonAsync<ServisKaydiYaniti>($"{Yol}/{kayit.Id}");
        Assert.Equal(1200.50m, zamSonrasi!.Parcalar[0].BirimFiyat);

        using var cikarma = await admin.DeleteAsync($"{Yol}/{kayit.Id}/parcalar/{satir.Id}");
        var parcasiz = await Oku<ServisKaydiYaniti>(cikarma);
        var iadeSonrasi = await admin.GetFromJsonAsync<ParcaYaniti>($"api/parcalar/{parca.Id}");
        var hareketler = await admin.GetFromJsonAsync<StokHareketiYaniti[]>($"api/parcalar/{parca.Id}/hareketler");

        Assert.Empty(parcasiz.Parcalar);
        Assert.Equal(5, iadeSonrasi!.StokAdedi);
        Assert.Equal(new[] { 2, -2, 5 }, hareketler!.Select(h => h.Adet));
        Assert.Equal(kayit.No, hareketler![1].ServisKaydiNo);
    }

    [Fact]
    public async Task Stokta_olmayan_parca_kullanilamaz_ve_stok_eksiye_dusmez()
    {
        using var admin = await _api.YetkiliIstemci();
        var parca = await ParcaEkle(admin, "Batarya " + Guid.NewGuid().ToString("N")[..6], 400m, stok: 1);
        var kayit = await KayitEkle(admin);

        using var fazla = await admin.PostAsJsonAsync($"{Yol}/{kayit.Id}/parcalar", new ParcaKullanimIstegi(parca.Id, 2));
        using var olmayan = await admin.PostAsJsonAsync($"{Yol}/{kayit.Id}/parcalar",
            new ParcaKullanimIstegi("0123456789abcdef01234567", 1));
        using var eksiDuzeltme = await admin.PostAsJsonAsync($"api/parcalar/{parca.Id}/stok", new StokHareketiIstegi(-2, null));
        using var sifir = await admin.PostAsJsonAsync($"api/parcalar/{parca.Id}/stok", new StokHareketiIstegi(0, null));

        // Aynı anda gelen iki istekten yalnızca biri son parçayı alabilir.
        var digerKayit = await KayitEkle(admin);
        var yanitlar = await Task.WhenAll(
            admin.PostAsJsonAsync($"{Yol}/{kayit.Id}/parcalar", new ParcaKullanimIstegi(parca.Id, 1)),
            admin.PostAsJsonAsync($"{Yol}/{digerKayit.Id}/parcalar", new ParcaKullanimIstegi(parca.Id, 1)));
        var sonDurum = await admin.GetFromJsonAsync<ParcaYaniti>($"api/parcalar/{parca.Id}");

        Assert.Equal(HttpStatusCode.Conflict, fazla.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, olmayan.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, eksiDuzeltme.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sifir.StatusCode);
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, yanitlar.Select(y => y.StatusCode).Order());
        Assert.Equal(0, sonDurum!.StokAdedi);
        Assert.True(sonDurum.AzaldiMi);
        foreach (var yanit in yanitlar) yanit.Dispose();
    }

    [Fact]
    public async Task Faturali_kaydin_parcalari_degismez_ve_parcali_kayit_silinemez()
    {
        using var admin = await _api.YetkiliIstemci();
        var parca = await ParcaEkle(admin, "Fan " + Guid.NewGuid().ToString("N")[..6], 250m, stok: 3);
        var kayit = await KayitEkle(admin);
        using var ekleme = await admin.PostAsJsonAsync($"{Yol}/{kayit.Id}/parcalar", new ParcaKullanimIstegi(parca.Id, 1));
        var parcali = await Oku<ServisKaydiYaniti>(ekleme);

        using var silme = await admin.DeleteAsync($"{Yol}/{kayit.Id}");
        await Durum(admin, kayit.Id, ServisDurumu.Tamamlandi);
        using var fatura = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/fatura", new FaturaIstegi(600m, false));
        fatura.EnsureSuccessStatusCode();
        using var faturaliEkleme = await admin.PostAsJsonAsync($"{Yol}/{kayit.Id}/parcalar", new ParcaKullanimIstegi(parca.Id, 1));
        using var faturaliCikarma = await admin.DeleteAsync($"{Yol}/{kayit.Id}/parcalar/{parcali.Parcalar[0].Id}");
        var stok = await admin.GetFromJsonAsync<ParcaYaniti>($"api/parcalar/{parca.Id}");

        Assert.Equal(HttpStatusCode.Conflict, silme.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, faturaliEkleme.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, faturaliCikarma.StatusCode);
        Assert.Equal(2, stok!.StokAdedi);

        // Parçalı faturanın belgesi de üretilebilmeli.
        using var pdf = await admin.GetAsync($"{Yol}/{kayit.Id}/fatura/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
    }

    [Fact]
    public async Task Stok_kartlarini_yalnizca_yonetici_yonetir_teknisyen_gorur_ve_kullanir()
    {
        using var admin = await _api.YetkiliIstemci();
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin);
        using var usta = await _api.YetkiliIstemci(teknisyen.KullaniciAdi, ApiFabrikasi.PersonelSifresi);
        var ad = "Klavye " + Guid.NewGuid().ToString("N")[..6];
        var parca = await ParcaEkle(admin, ad, 300m, stok: 2, kritik: 2);
        var kayit = await KayitEkle(admin);

        using var kartAcma = await usta.PostAsJsonAsync("api/parcalar", new ParcaIstegi("Yetkisiz", null, 1m, 0));
        using var stokGirisi = await usta.PostAsJsonAsync($"api/parcalar/{parca.Id}/stok", new StokHareketiIstegi(5, null));
        using var kartSilme = await usta.DeleteAsync($"api/parcalar/{parca.Id}");
        var liste = await usta.GetFromJsonAsync<Sayfa<ParcaYaniti>>($"api/parcalar?arama={Uri.EscapeDataString(ad.ToUpperInvariant())}");
        var azalanlar = await usta.GetFromJsonAsync<Sayfa<ParcaYaniti>>("api/parcalar?azalan=true&boyut=100");
        using var kullanma = await usta.PostAsJsonAsync($"{Yol}/{kayit.Id}/parcalar", new ParcaKullanimIstegi(parca.Id, 1));

        Assert.Equal(HttpStatusCode.Forbidden, kartAcma.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, stokGirisi.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, kartSilme.StatusCode);
        Assert.Equal(parca.Id, Assert.Single(liste!.Ogeler).Id);
        Assert.Contains(azalanlar!.Ogeler, p => p.Id == parca.Id);
        Assert.Equal(HttpStatusCode.OK, kullanma.StatusCode);

        using var silme = await admin.DeleteAsync($"api/parcalar/{parca.Id}");
        using var silinmis = await admin.GetAsync($"api/parcalar/{parca.Id}");
        Assert.Equal(HttpStatusCode.NoContent, silme.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, silinmis.StatusCode);
    }

    // --- Formlar ---

    [Fact]
    public async Task Kabul_formu_her_zaman_teslim_formu_is_kapaninca_alinir()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin, "FORM-1");

        using var kabul = await admin.GetAsync($"{Yol}/{kayit.Id}/kabul-formu");
        using var erkenTeslim = await admin.GetAsync($"{Yol}/{kayit.Id}/teslim-formu");
        await Durum(admin, kayit.Id, ServisDurumu.TeslimEdildi);
        using var teslim = await admin.GetAsync($"{Yol}/{kayit.Id}/teslim-formu");

        Assert.Equal(HttpStatusCode.OK, kabul.StatusCode);
        Assert.Equal("application/pdf", kabul.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await kabul.Content.ReadAsByteArrayAsync())[..4]));
        Assert.Equal(HttpStatusCode.Conflict, erkenTeslim.StatusCode);
        Assert.Equal(HttpStatusCode.OK, teslim.StatusCode);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await teslim.Content.ReadAsByteArrayAsync())[..4]));
    }

    // --- Rapor ---

    [Fact]
    public async Task Rapor_donemdeki_isleri_geliri_ve_teknisyenleri_ozetler()
    {
        // Sayımlar başka testlerin kayıtlarıyla karışmasın diye ayrı bir işletmede çalışır.
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = "rapor-" + Guid.NewGuid().ToString("N")[..8];
        await ApiFabrikasi.IsletmeAc(yonetici, kod, "Rapor İşletmesi");
        using var admin = await _api.YetkiliIstemci(isletmeKodu: kod);
        var teknisyen = await ApiFabrikasi.PersonelEkle(admin, "Rapor Ustası");
        using var usta = await _api.YetkiliIstemci(teknisyen.KullaniciAdi, ApiFabrikasi.PersonelSifresi, kod);
        var parca = await ParcaEkle(admin, "Rapor parçası", 100m, stok: 10);

        var biten = await KayitEkle(admin, teknisyenId: teknisyen.Id, garanti: true);
        using var parcaEkleme = await admin.PostAsJsonAsync($"{Yol}/{biten.Id}/parcalar", new ParcaKullanimIstegi(parca.Id, 3));
        parcaEkleme.EnsureSuccessStatusCode();
        await Durum(admin, biten.Id, ServisDurumu.Tamamlandi);
        using var fatura = await admin.PutAsJsonAsync($"{Yol}/{biten.Id}/fatura", new FaturaIstegi(750m, true));
        fatura.EnsureSuccessStatusCode();

        var odenmeyen = await KayitEkle(admin);
        await Durum(admin, odenmeyen.Id, ServisDurumu.Tamamlandi);
        using var odenmeyenFatura = await admin.PutAsJsonAsync($"{Yol}/{odenmeyen.Id}/fatura", new FaturaIstegi(250m, false));
        odenmeyenFatura.EnsureSuccessStatusCode();

        var iptal = await KayitEkle(admin);
        await Durum(admin, iptal.Id, ServisDurumu.IptalEdildi);
        await KayitEkle(admin);

        var simdi = DateTime.UtcNow;
        string Sorgu(DateTime ilk, DateTime son) =>
            $"api/raporlar?baslangic={Uri.EscapeDataString(ilk.ToString("o"))}&bitis={Uri.EscapeDataString(son.ToString("o"))}";

        var rapor = await admin.GetFromJsonAsync<RaporYaniti>(Sorgu(simdi.AddDays(-1), simdi.AddDays(1)));
        var gecmisDonem = await admin.GetFromJsonAsync<RaporYaniti>(Sorgu(simdi.AddDays(-60), simdi.AddDays(-30)));
        using var teknisyenIstegi = await usta.GetAsync(Sorgu(simdi.AddDays(-1), simdi.AddDays(1)));
        using var tersAralik = await admin.GetAsync(Sorgu(simdi, simdi.AddDays(-1)));

        Assert.Equal((4, 2, 1, 1), (rapor!.AcilanKayit, rapor.TamamlananKayit, rapor.IptalEdilenKayit, rapor.GarantiKapsamindaKayit));
        Assert.Equal((2, 1000m, 750m), (rapor.KesilenFatura, rapor.KesilenFaturaTutari, rapor.TahsilEdilen));
        Assert.NotNull(rapor.OrtalamaTamirSaati);

        var ustaSatiri = Assert.Single(rapor.Teknisyenler, t => t.Ad == "Rapor Ustası");
        Assert.Equal((1, 1, 750m), (ustaSatiri.AcilanKayit, ustaSatiri.TamamlananKayit, ustaSatiri.FaturaTutari));
        Assert.Contains(rapor.Teknisyenler, t => t.Ad == "Atanmamış" && t.AcilanKayit == 3);

        Assert.Equal((4, 2, 750m), (rapor.Aylar.Sum(a => a.AcilanKayit), rapor.Aylar.Sum(a => a.TamamlananKayit), rapor.Aylar.Sum(a => a.TahsilEdilen)));
        Assert.Equal(("Rapor parçası", 3, 300m), (rapor.Parcalar[0].Ad, rapor.Parcalar[0].Adet, rapor.Parcalar[0].Tutar));

        Assert.Equal(0, gecmisDonem!.AcilanKayit);
        Assert.Empty(gecmisDonem.Teknisyenler);
        Assert.Equal(HttpStatusCode.Forbidden, teknisyenIstegi.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tersAralik.StatusCode);

        // Özet de yeni durumları sayar.
        var gun = DateTime.UtcNow.Date;
        var ozet = await admin.GetFromJsonAsync<OzetYaniti>(
            $"api/ozet?gunBaslangici={Uri.EscapeDataString(gun.ToString("o"))}&ayBaslangici={Uri.EscapeDataString(gun.AddDays(-40).ToString("o"))}");
        Assert.Equal(2, ozet!.TeslimBekleyenKayit);
        Assert.Equal(0, ozet.OnayBekleyenKayit);
        Assert.Equal(0, ozet.AzalanParca);
    }

    // --- Takip sayfası ve bildirim ---

    [Fact]
    public async Task Takip_sayfasi_durumu_gosterir_ama_musteri_bilgisini_gostermez()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin);
        using var teklif = await admin.PutAsJsonAsync($"{Yol}/{kayit.Id}/teklif", new TeklifIstegi(1500m, null));
        teklif.EnsureSuccessStatusCode();
        using var anonim = _api.CreateClient();

        using var bos = await anonim.GetAsync($"takip/{ApiFabrikasi.IsletmeKodu}");
        using var bulunan = await anonim.GetAsync($"takip/{ApiFabrikasi.IsletmeKodu}?kod={kayit.TakipKodu.ToLowerInvariant()}");
        var sayfa = await bulunan.Content.ReadAsStringAsync();
        using var yanlisKod = await anonim.GetAsync($"takip/{ApiFabrikasi.IsletmeKodu}?kod=ABCDEFGH23");
        using var zararli = await anonim.GetAsync($"takip/{ApiFabrikasi.IsletmeKodu}?kod={Uri.EscapeDataString("<script>alert(1)</script>")}");
        using var olmayanIsletme = await anonim.GetAsync($"takip/olmayan-isletme?kod={kayit.TakipKodu}");

        Assert.Equal(HttpStatusCode.OK, bos.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bulunan.StatusCode);
        Assert.Equal("text/html", bulunan.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Onay bekliyor", sayfa);
        Assert.Contains(kayit.No, sayfa);
        Assert.Contains("1.500,00", sayfa);
        Assert.DoesNotContain("Gizli", sayfa);
        Assert.DoesNotContain("05551112233", sayfa);
        Assert.DoesNotContain("musteri@ornek.com", sayfa);
        Assert.Contains("frame-ancestors 'none'", bulunan.Headers.GetValues("Content-Security-Policy").Single());

        Assert.Equal(HttpStatusCode.NotFound, yanlisKod.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, zararli.StatusCode);
        Assert.DoesNotContain("<script>alert", await zararli.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, olmayanIsletme.StatusCode);
    }

    [Fact]
    public async Task Takip_kodu_baska_isletmenin_sayfasinda_calismaz()
    {
        using var yonetici = await _api.YoneticiIstemcisi();
        var kod = "takip-" + Guid.NewGuid().ToString("N")[..8];
        var diger = await ApiFabrikasi.IsletmeAc(yonetici, kod, "Diğer İşletme");
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin);
        using var anonim = _api.CreateClient();

        using var baskaIsletmede = await anonim.GetAsync($"takip/{kod}?kod={kayit.TakipKodu}");

        // Askıya alınan işletmenin sayfası kapanır.
        using var aski = await yonetici.PutAsJsonAsync($"api/platform/isletmeler/{diger.Id}",
            new IsletmeDurumIstegi(diger.Ad, false, diger.Adres!, diger.Telefon!, diger.Eposta!));
        aski.EnsureSuccessStatusCode();
        using var askidaki = await anonim.GetAsync($"takip/{kod}");

        Assert.Equal(HttpStatusCode.NotFound, baskaIsletmede.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, askidaki.StatusCode);
    }

    [Fact]
    public async Task Bildirim_metni_duruma_gore_yazilir_ve_gonderim_gecmise_islenir()
    {
        using var admin = await _api.YetkiliIstemci();
        var kayit = await KayitEkle(admin);
        await Durum(admin, kayit.Id, ServisDurumu.Tamamlandi);

        var bildirim = await admin.GetFromJsonAsync<BildirimYaniti>($"{Yol}/{kayit.Id}/bildirim");
        using var kayitYaniti = await admin.PostAsJsonAsync($"{Yol}/{kayit.Id}/bildirimler", new BildirimIstegi(BildirimKanali.WhatsApp));

        Assert.Contains("teslim alabilirsiniz", bildirim!.Metin);
        Assert.Contains(kayit.No, bildirim.Metin);
        Assert.EndsWith($"/takip/{ApiFabrikasi.IsletmeKodu}?kod={kayit.TakipKodu}", bildirim.TakipAdresi);
        Assert.Contains(bildirim.TakipAdresi, bildirim.Metin);
        Assert.Equal("905551112233", bildirim.WhatsAppNumarasi);
        Assert.Equal("musteri@ornek.com", bildirim.Eposta);
        Assert.Equal(HttpStatusCode.NoContent, kayitYaniti.StatusCode);

        var sonIslem = (await Gecmis(admin, kayit.Id))![0];
        Assert.Equal(IslemTuru.MusteriBilgilendirildi, sonIslem.Tur);
        Assert.Contains("WhatsApp", sonIslem.Aciklama);
    }

    // --- Veri geçişi ---

    [Fact]
    public async Task Gecis_eski_kayitlara_acilis_sirasina_gore_numara_ve_takip_kodu_verir()
    {
        var istemci = new MongoClient(ApiFabrikasi.MongoAdresi);
        var ad = "vulu_iys_test_" + Guid.NewGuid().ToString("N");
        try
        {
            var db = istemci.GetDatabase(ad);
            var kayitlar = db.GetCollection<BsonDocument>("servisKayitlari");
            await kayitlar.InsertManyAsync(
            [
                new BsonDocument { ["_id"] = 1, ["olusturmaTarihi"] = new DateTime(2025, 6, 2, 0, 0, 0, DateTimeKind.Utc) },
                new BsonDocument { ["_id"] = 2, ["olusturmaTarihi"] = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc) },
                // 31 Aralık 22:30 UTC, Türkiye saatiyle yeni yıldır.
                new BsonDocument { ["_id"] = 3, ["olusturmaTarihi"] = new DateTime(2025, 12, 31, 22, 30, 0, DateTimeKind.Utc) }
            ]);

            await Gecisler.CalistirAsync(db, NullLogger.Instance);
            var ilkSonuc = await kayitlar.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            await Gecisler.CalistirAsync(db, NullLogger.Instance);
            var sonuc = (await kayitlar.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync()).ToDictionary(k => k["_id"].AsInt32);

            Assert.Equal("SK-2025-00002", sonuc[1]["no"].AsString);
            Assert.Equal("SK-2025-00001", sonuc[2]["no"].AsString);
            Assert.Equal("SK-2026-00001", sonuc[3]["no"].AsString);
            Assert.Equal(3, sonuc.Values.Select(k => k["takipKodu"].AsString).Distinct().Count());
            Assert.Equal(ilkSonuc.Select(k => k["takipKodu"].AsString).Order(), sonuc.Values.Select(k => k["takipKodu"].AsString).Order());

            // Yeni kayıtlar numaralamayı kaldığı yerden sürdürür ve indexler kurulabilir.
            var baglam = new MongoBaglami(db);
            Assert.Equal(3, await baglam.SiradakiNumaraAsync(MongoBaglami.ServisSayaci(2025)));
            await baglam.IndexleriKurAsync();
        }
        finally
        {
            await istemci.DropDatabaseAsync(ad);
        }
    }
}
