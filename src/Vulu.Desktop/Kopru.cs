using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Photino.NET;
using Vulu.Desktop.Servisler;
using Vulu.Shared;

namespace Vulu.Desktop;

// Ekranlar (JS) ile C# arasındaki tek bağlantı.
//
// JS bir istek yollar:  { "no": "k3f9x-7", "islem": "giris_yap", "veri": { ... } }
// C# işi yapar ve aynı numarayla cevap verir:
//     { "no": "k3f9x-7", "basarili": true, "veri": { ... } }   ya da   { "no": "k3f9x-7", "basarili": false, "hata": "..." }
// Numarayı JS verir ve C# yalnızca geri yollar (bkz. kopru.js).
// C# kendiliğinden de mesaj yollayabilir (örneğin indirme yüzdesi):  { "olay": "indirme", "veri": 42 }
//
// API'yi her zaman C# çağırır; token ve oturum bilgisi JS tarafına hiç gelmez.
public static class Kopru
{
    // JS'e giden JSON: alan adları küçük harfle başlar (adSoyad), boş alanlar yazılmaz.
    private static readonly JsonSerializerOptions JsonAyarlari = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static PhotinoWindow _pencere = null!;
    private static HttpClient _http = null!;
    private static OturumServisi _oturum = null!;
    private static ApiIstemcisi _api = null!;

    // Açılışta bulunan yeni sürüm; "guncelle" isteği bunu indirip kurar.
    private static YeniSurum? _yeniSurum;

    // Sunucuyu uyanık tutan zamanlayıcı (bkz. Baslat).
    private static Timer? _uyanikTutucu;

    // JS'e giden cevap.
    private sealed class Cevap
    {
        public string No { get; set; } = "";
        public bool Basarili { get; set; }
        public object? Veri { get; set; }
        public string? Hata { get; set; }

        // Oturum bitti (süresi doldu ya da yönetici kapattı); JS giriş ekranına döner.
        public bool OturumKapandi { get; set; }
    }

    public static void Baslat(PhotinoWindow pencere, Uri apiAdresi)
    {
        _pencere = pencere;
        _http = new HttpClient { BaseAddress = apiAdresi };
        _oturum = new OturumServisi(_http);
        _api = new ApiIstemcisi(_http, _oturum);

        pencere.RegisterWebMessageReceivedHandler(MesajGeldi);

        // Ücretsiz barındırma, 15 dakika istek gelmeyen sunucuyu uyutur; uyanması bir dakika kadar sürer ve o sırada
        // ekranlar boş kalır. Uygulama açık olduğu sürece 10 dakikada bir kısa bir istek gönderilir. Sunucuya
        // ulaşılamazsa sessizce geçilir; bir sonraki denemede yine gönderilir.
        _uyanikTutucu = new Timer(async _ =>
        {
            try
            {
                using var yanit = await _http.GetAsync("api/saglik");
            }
            catch (Exception hata) when (hata is HttpRequestException or TaskCanceledException)
            {
            }
        }, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    // Photino'nun olay işleyicisi Task döndüremediği için "async void"; bu yüzden hiçbir hata dışarı kaçmamalı.
    private static async void MesajGeldi(object? gonderen, string metin)
    {
        var no = "";
        Cevap cevap;
        try
        {
            using var belge = JsonDocument.Parse(metin);
            no = belge.RootElement.GetProperty("no").GetString() ?? "";
            var islem = belge.RootElement.GetProperty("islem").GetString() ?? "";
            var veri = belge.RootElement.TryGetProperty("veri", out var v) ? v.Clone() : default;

            cevap = await IstegiIsle(islem, veri);
        }
        catch (Exception hata)
        {
            cevap = Basarisiz("Beklenmeyen hata: " + hata.Message);
        }

        cevap.No = no;
        _pencere.SendWebMessage(JsonSerializer.Serialize(cevap, JsonAyarlari));
    }

    // C#'ın kendiliğinden JS'e haber vermesi (cevap beklenmez).
    private static void OlayYolla(string olay, object veri) =>
        _pencere.SendWebMessage(JsonSerializer.Serialize(new { olay, veri }, JsonAyarlari));

    // Her istek adının ne yaptığı burada okunur. Çoğu, API'deki bir adrese olduğu gibi gider; JS'ten gelen veri
    // isteğin gövdesi olur ve alanların doğruluğunu sunucu denetler.
    private static async Task<Cevap> IstegiIsle(string islem, JsonElement veri)
    {
        switch (islem)
        {
            // --- Giriş ve oturum ---
            case "baslangic_kontrolu":
                return await BaslangicKontrolu();
            case "guncelle":
                return await Guncelle();
            case "giris_yap":
                return await GirisYap(veri);
            case "cikis_yap":
                await _oturum.CikisYap();
                return Tamam(null);
            case "oturum_bilgisi":
                return await OturumBilgisi();
            case "sifre_degistir":
                return await SifreDegistir(veri);

            // --- Ana sayfa ---
            case "ozet":
                return await Getir("api/ozet?" + Sorgu(veri, "gunBaslangici", "ayBaslangici"));

            // --- Müşteriler ---
            case "musteri_listele":
                return await Getir("api/musteriler?" + Sorgu(veri, "arama", "sayfa", "boyut"));
            case "musteri_kaydet":
                return await Kaydet("api/musteriler", veri);
            case "musteri_sil":
                return await Sil($"api/musteriler/{Kimlik(veri)}");

            // --- Servis kayıtları ---
            case "servis_listele":
                return await Getir("api/servis-kayitlari?" + Sorgu(veri, "arama", "durum", "fatura", "seriNo", "musteriId", "sayfa", "boyut"));
            case "servis_kaydet":
                return await Kaydet("api/servis-kayitlari", veri);
            case "servis_sil":
                return await Sil($"api/servis-kayitlari/{Kimlik(veri)}");
            case "servis_durum":
                return await Guncelle($"api/servis-kayitlari/{Kimlik(veri)}/durum", veri);
            case "servis_yapilan_islem":
                return await Guncelle($"api/servis-kayitlari/{Kimlik(veri)}/yapilan-islem", veri);
            case "servis_teklif":
                return await Guncelle($"api/servis-kayitlari/{Kimlik(veri)}/teklif", veri);
            case "servis_teklif_yanit":
                return await Guncelle($"api/servis-kayitlari/{Kimlik(veri)}/teklif/yanit", veri);
            case "servis_parca_ekle":
                return await Ekle($"api/servis-kayitlari/{Kimlik(veri)}/parcalar", veri);
            case "servis_parca_cikar":
                return await Sil($"api/servis-kayitlari/{Kimlik(veri)}/parcalar/{Kimlik(veri, "satirId")}");
            case "servis_fatura":
                return await Guncelle($"api/servis-kayitlari/{Kimlik(veri)}/fatura", veri);
            case "servis_fatura_sil":
                return await Sil($"api/servis-kayitlari/{Kimlik(veri)}/fatura");
            case "servis_gecmis":
                return await Getir($"api/servis-kayitlari/{Kimlik(veri)}/gecmis");
            case "servis_not":
                return await Ekle($"api/servis-kayitlari/{Kimlik(veri)}/notlar", veri);
            case "servis_belge":
                return await BelgeAc(veri);
            case "servis_bilgilendir":
                return await MusteriyiBilgilendir(veri);

            // Servis kaydı ve randevu formlarındaki teknisyen seçimi.
            case "personel_ozet":
                return await Getir("api/personel");

            // --- Stok ---
            case "parca_listele":
                return await Getir("api/parcalar?" + Sorgu(veri, "arama", "azalan", "sayfa", "boyut"));
            case "parca_kaydet":
                return await Kaydet("api/parcalar", veri);
            case "parca_sil":
                return await Sil($"api/parcalar/{Kimlik(veri)}");
            case "parca_stok":
                return await Ekle($"api/parcalar/{Kimlik(veri)}/stok", veri);
            case "parca_hareketleri":
                return await Getir($"api/parcalar/{Kimlik(veri)}/hareketler");

            // --- Randevular ---
            case "randevu_listele":
                return await Getir("api/randevular?" + Sorgu(veri, "arama", "durum", "baslangic", "bitis", "sayfa", "boyut"));
            case "randevu_kaydet":
                return await Kaydet("api/randevular", veri);
            case "randevu_durum":
                return await Guncelle($"api/randevular/{Kimlik(veri)}/durum", veri);
            case "randevu_sil":
                return await Sil($"api/randevular/{Kimlik(veri)}");

            // --- Raporlar ---
            case "rapor":
                return await Getir("api/raporlar?" + Sorgu(veri, "baslangic", "bitis"));

            // --- Personel ---
            case "personel_listele":
                return await Getir("api/kullanicilar");
            case "personel_kaydet":
                return await Kaydet("api/kullanicilar", veri);
            case "personel_sil":
                return await Sil($"api/kullanicilar/{Kimlik(veri)}");

            // --- İşletme ayarları ---
            case "isletme_getir":
                return await Getir("api/isletme");
            case "isletme_kaydet":
                return await IsletmeKaydet(veri);

            // --- Excel ---
            case "excel":
                return await ExceleAktar(veri);

            // --- Vulu yöneticisi ---
            case "vulu_isletmeler":
                return await Getir("api/platform/isletmeler");
            case "vulu_isletme_logosu":
                return await IsletmeLogosu(veri);
            case "vulu_isletme_ac":
                return await Ekle("api/platform/isletmeler", veri);
            case "vulu_isletme_guncelle":
                return await Guncelle($"api/platform/isletmeler/{Kimlik(veri)}", veri);
            case "vulu_isletme_yoneticileri":
                return await Getir($"api/platform/isletmeler/{Kimlik(veri)}/yoneticiler");
            case "vulu_sifre_sifirla":
                return await Guncelle($"api/platform/isletmeler/{Kimlik(veri)}/yoneticiler/{Kimlik(veri, "yoneticiId")}/sifre", veri);
            case "vulu_isletme_sil":
                return await Sil($"api/platform/isletmeler/{Kimlik(veri)}?" + Sorgu(veri, "kod"));
            case "vulu_yoneticiler":
                return await Getir("api/platform/yoneticiler");
            case "vulu_yonetici_kaydet":
                return await Kaydet("api/platform/yoneticiler", veri);
            case "vulu_yonetici_sil":
                return await Sil($"api/platform/yoneticiler/{Kimlik(veri)}");
            case "vulu_baglanti":
                return await Getir("api/saglik/baglanti");

            default:
                return Basarisiz("Bilinmeyen işlem: " + islem);
        }
    }

    // Giriş formu; uygulamanın güncel olduğu, sunucuya ulaşıldığı ve sürümün uyumlu olduğu görülmeden açılmaz.
    private static async Task<Cevap> BaslangicKontrolu()
    {
        if (_oturum.GirisYapildi)
            return Tamam(new { durum = "girisYapildi", vuluYoneticisi = _oturum.VuluYoneticisiMi });

        // Güncelleme zorunludur. Sürüm bilgisine ulaşılamazsa (internet yok, GitHub yanıt vermiyor) kontrol
        // atlanır; uygulama bu yüzden kullanılamaz kalmaz.
        _yeniSurum = await Guncelleyici.KontrolEt();
        if (_yeniSurum is not null)
        {
            return Tamam(new
            {
                durum = "guncelleme",
                yeniSurum = _yeniSurum.Surum.ToString(),
                mevcutSurum = Guncelleyici.MevcutSurum.ToString()
            });
        }

        string durum;
        try
        {
            // Veritabanı kapalıyken dönen 503 de sürüm bilgisini taşır; durum koduna bakılmaz.
            using var yanit = await _http.GetAsync("api/saglik");
            var saglik = await yanit.Content.ReadFromJsonAsync<SaglikYaniti>();
            durum = saglik?.SozlesmeSurumu == ApiSozlesmesi.Surum ? "hazir" : "uyumsuz";
        }
        catch (Exception hata) when (hata is HttpRequestException or TaskCanceledException or JsonException)
        {
            durum = "ulasilamiyor";
        }

        return Tamam(new { durum, sonIsletmeKodu = YerelAyarlar.SonIsletmeKodu });
    }

    private static async Task<Cevap> Guncelle()
    {
        if (_yeniSurum is null)
            return Basarisiz("Yüklenecek bir güncelleme yok.");

        // Başarılıysa uygulama kapanır ve kurulum onu yeni sürümle yeniden açar; buraya yalnızca hata dönerse gelinir.
        var hata = await Guncelleyici.IndirVeKur(_yeniSurum, yuzde => OlayYolla("indirme", yuzde));
        return Basarisiz(hata);
    }

    private static async Task<Cevap> GirisYap(JsonElement veri)
    {
        var isletmeKodu = Yazi(veri, "isletmeKodu");
        var hata = await _oturum.GirisYap(isletmeKodu, Yazi(veri, "kullaniciAdi"), Yazi(veri, "sifre"));
        if (hata is not null)
            return Basarisiz(hata);

        YerelAyarlar.SonIsletmeKodu = isletmeKodu.Trim().ToLowerInvariant();
        return Tamam(new { vuluYoneticisi = _oturum.VuluYoneticisiMi });
    }

    // Menünün ve sayfaların ihtiyaç duyduğu bilgiler: kim giriş yaptı, neleri görebilir, işletmenin adı ve logosu.
    private static async Task<Cevap> OturumBilgisi()
    {
        if (_oturum.Oturum is not { } oturum)
            return OturumYok();

        // İşletmenin logosu ve tam bilgileri girişten sonra bir kez yüklenir.
        if (!_oturum.VuluYoneticisiMi && _oturum.Isletme is null)
        {
            var sonuc = await _api.Getir("api/isletme");
            if (sonuc.Basarili)
                _oturum.IsletmeyiAyarla(JsonSerializer.Deserialize<IsletmeYaniti>(sonuc.Veri, JsonAyarlari)!);
            else if (!_oturum.GirisYapildi)
                return OturumYok();
        }

        var isletme = _oturum.Isletme;
        string rol;
        if (_oturum.VuluYoneticisiMi) rol = "Vulu yöneticisi";
        else if (_oturum.AdminMi) rol = "Yönetici";
        else rol = "Teknisyen";

        return Tamam(new
        {
            kimlik = oturum.Kullanici.Id,
            adSoyad = oturum.Kullanici.AdSoyad,
            rol,
            adminMi = _oturum.AdminMi,
            stokYonetebilir = _oturum.StokYonetebilir,
            vuluYoneticisi = _oturum.VuluYoneticisiMi,
            isletmeAdi = isletme?.Ad ?? oturum.Isletme?.Ad,
            // Logo yoksa sayfa vulu logosunu gösterir.
            logo = isletme is { LogoVerisi.Length: > 0, LogoTuru: not null }
                ? $"data:{isletme.LogoTuru};base64,{isletme.LogoVerisi}"
                : null
        });
    }

    private static async Task<Cevap> SifreDegistir(JsonElement veri)
    {
        var sonuc = await _api.Ekle("api/kimlik/sifre",
            new SifreDegistirmeIstegi(Yazi(veri, "mevcutSifre"), Yazi(veri, "yeniSifre")));
        if (!sonuc.Basarili)
            return ApiHatasi(sonuc.Hata);

        // Eski tokenlar artık geçersiz; sunucunun verdiği yeni oturumla devam edilir.
        _oturum.OturumuYenile(JsonSerializer.Deserialize<OturumYaniti>(sonuc.Veri, JsonAyarlari)!);
        return Tamam(null);
    }

    // Kayıt belgeleri (PDF) indirilir ve bilgisayarın PDF görüntüleyicisinde açılır.
    private static async Task<Cevap> BelgeAc(JsonElement veri)
    {
        // JS yalnızca belgenin türünü söyler; adres burada, bu üç seçenekten biri olarak kurulur.
        string uc;
        string dosyaAdi;
        switch (Yazi(veri, "belge"))
        {
            case "kabul":
                uc = "kabul-formu";
                dosyaAdi = $"Kabul-{Yazi(veri, "no")}.pdf";
                break;
            case "teslim":
                uc = "teslim-formu";
                dosyaAdi = $"Teslim-{Yazi(veri, "no")}.pdf";
                break;
            case "fatura":
                uc = "fatura/pdf";
                dosyaAdi = $"Fatura-{Yazi(veri, "no")}.pdf";
                break;
            default:
                return Basarisiz("Bilinmeyen belge.");
        }

        var sonuc = await _api.Indir($"api/servis-kayitlari/{Kimlik(veri)}/{uc}");
        if (!sonuc.Basarili || sonuc.Dosya is null)
            return ApiHatasi(sonuc.Hata);

        // BelgeAcici dosya adındaki klasör ayraçlarını atar; ad yalnızca geçici klasörde kullanılır.
        var hata = BelgeAcici.Ac(dosyaAdi, sonuc.Dosya);
        return hata is null ? Tamam(null) : Basarisiz(hata);
    }

    // Kaydın durumuna göre hazırlanan mesajı WhatsApp ya da e-posta programında açar ve bilgilendirmeyi kaydın
    // geçmişine yazar. "Telefon" seçilirse hiçbir şey açılmaz, yalnızca geçmişe not düşülür.
    private static async Task<Cevap> MusteriyiBilgilendir(JsonElement veri)
    {
        var yol = $"api/servis-kayitlari/{Kimlik(veri)}";
        var kanal = Yazi(veri, "kanal");

        var mesaj = await _api.Getir($"{yol}/bildirim");
        if (!mesaj.Basarili)
            return ApiHatasi(mesaj.Hata);

        // Sunucu mesajın metnini ve müşterinin WhatsApp numarası ile e-postasını (varsa) verir.
        var bildirimMetni = Yazi(mesaj.Veri, "metin");
        var whatsAppNumarasi = Yazi(mesaj.Veri, "whatsAppNumarasi");
        var eposta = Yazi(mesaj.Veri, "eposta");
        var metin = Uri.EscapeDataString(bildirimMetni);
        string? hata = null;

        if (kanal == "WhatsApp")
        {
            if (whatsAppNumarasi == "")
                return Basarisiz("Müşterinin telefonu WhatsApp için uygun biçimde değil.");
            hata = BelgeAcici.BaglantiAc($"https://wa.me/{whatsAppNumarasi}?text={metin}");
        }
        else if (kanal == "Eposta")
        {
            if (eposta == "")
                return Basarisiz("Müşterinin e-posta adresi kayıtlı değil.");
            var konu = Uri.EscapeDataString("Servis kaydı " + Yazi(veri, "no"));
            hata = BelgeAcici.BaglantiAc($"mailto:{Uri.EscapeDataString(eposta)}?subject={konu}&body={metin}");
        }
        else if (kanal != "Telefon")
        {
            return Basarisiz("Bilinmeyen bildirim yolu.");
        }

        if (hata is not null)
            return Basarisiz(hata);

        var kayit = await _api.Ekle($"{yol}/bildirimler", new { kanal });
        if (!kayit.Basarili)
            return ApiHatasi(kayit.Hata);

        return Tamam(new { metin = bildirimMetni });
    }

    private static async Task<Cevap> IsletmeKaydet(JsonElement veri)
    {
        var sonuc = await _api.Guncelle("api/isletme", veri);
        if (!sonuc.Basarili)
            return ApiHatasi(sonuc.Hata);

        // Yan menüdeki ad ve logo da yenilensin.
        _oturum.IsletmeyiAyarla(JsonSerializer.Deserialize<IsletmeYaniti>(sonuc.Veri, JsonAyarlari)!);
        return Tamam(sonuc.Veri);
    }

    // Vulu yöneticisinin listesindeki işletme logosu; sayfaya gömülecek "data:" adresi olarak döner.
    private static async Task<Cevap> IsletmeLogosu(JsonElement veri)
    {
        var tur = Yazi(veri, "tur");
        if (!tur.StartsWith("image/", StringComparison.Ordinal))
            return Basarisiz("Geçersiz logo türü.");

        var sonuc = await _api.Indir($"api/platform/isletmeler/{Kimlik(veri)}/logo");
        if (!sonuc.Basarili || sonuc.Dosya is null)
            return ApiHatasi(sonuc.Hata);

        return Tamam(new { adres = $"data:{tur};base64,{Convert.ToBase64String(sonuc.Dosya)}" });
    }

    // Listeyi Excel dosyası olarak indirir, İndirilenler klasörüne kaydeder ve açar.
    private static async Task<Cevap> ExceleAktar(JsonElement veri)
    {
        string yol;
        string dosyaAdi;
        switch (Yazi(veri, "liste"))
        {
            case "musteriler":
                yol = "api/musteriler/disa-aktar";
                dosyaAdi = "Musteriler";
                break;
            case "servis":
                // Liste ekranda hangi süzgeçle görünüyorsa o haliyle aktarılır.
                yol = "api/servis-kayitlari/disa-aktar?" + Sorgu(veri, "arama", "durum", "fatura");
                dosyaAdi = "Servis-kayitlari";
                break;
            case "stok":
                yol = "api/parcalar/disa-aktar";
                dosyaAdi = "Stok";
                break;
            case "rapor":
                yol = "api/raporlar/disa-aktar?" + Sorgu(veri, "baslangic", "bitis");
                dosyaAdi = "Rapor";
                break;
            default:
                return Basarisiz("Bilinmeyen liste.");
        }

        var sonuc = await _api.Indir(yol);
        if (!sonuc.Basarili || sonuc.Dosya is null)
            return ApiHatasi(sonuc.Hata);

        var (kaydedilen, hata) = BelgeAcici.KaydetVeAc($"{dosyaAdi}-{DateTime.Today:yyyy-MM-dd}.xlsx", sonuc.Dosya);
        if (hata is not null)
            return Basarisiz(hata);
        return Tamam(new { dosyaAdi = kaydedilen });
    }

    // --- Yardımcılar ---

    // API'ye giden dört istek türü; cevap JS'e olduğu gibi iletilir.
    private static async Task<Cevap> Getir(string yol) => ApiCevabi(await _api.Getir(yol));

    private static async Task<Cevap> Ekle(string yol, JsonElement govde) => ApiCevabi(await _api.Ekle(yol, govde));

    private static async Task<Cevap> Guncelle(string yol, JsonElement govde) => ApiCevabi(await _api.Guncelle(yol, govde));

    private static async Task<Cevap> Sil(string yol) => ApiCevabi(await _api.Sil(yol));

    // Formlar: veride kimlik varsa kayıt güncellenir, yoksa yenisi eklenir.
    private static Task<Cevap> Kaydet(string yol, JsonElement veri)
    {
        var id = Kimlik(veri);
        if (id == "")
            return Ekle(yol, veri);
        return Guncelle($"{yol}/{id}", veri);
    }

    private static Cevap ApiCevabi(ApiSonucu sonuc)
    {
        if (sonuc.Basarili)
            return Tamam(sonuc.Veri.ValueKind == JsonValueKind.Undefined ? null : sonuc.Veri);
        return ApiHatasi(sonuc.Hata);
    }

    // İstek, oturum düştüğü için başarısız olduysa JS giriş ekranına döner; değilse hata mesajını gösterir.
    private static Cevap ApiHatasi(string? hata)
    {
        if (!_oturum.GirisYapildi)
            return OturumYok();
        return Basarisiz(hata ?? "İşlem başarısız oldu.");
    }

    private static Cevap Tamam(object? veri) => new() { Basarili = true, Veri = veri };

    private static Cevap Basarisiz(string hata) => new() { Basarili = false, Hata = hata };

    private static Cevap OturumYok() => new() { Basarili = false, Hata = "Oturum kapandı.", OturumKapandi = true };

    // Kaydın kimliği adrese yazılacağı için kodlanır; geçersizse sunucu "bulunamadı" der.
    private static string Kimlik(JsonElement veri, string ad = "id") => Uri.EscapeDataString(Yazi(veri, ad));

    // Verideki adı verilen alanlardan sorgu dizgisi kurar: "arama=ali&sayfa=2". Boş alanlar yazılmaz.
    private static string Sorgu(JsonElement veri, params string[] adlar)
    {
        var parcalar = new List<string>();
        foreach (var ad in adlar)
        {
            if (veri.ValueKind != JsonValueKind.Object || !veri.TryGetProperty(ad, out var deger))
                continue;

            string metin;
            if (deger.ValueKind == JsonValueKind.String) metin = deger.GetString() ?? "";
            else if (deger.ValueKind == JsonValueKind.Number) metin = deger.GetRawText();
            else if (deger.ValueKind == JsonValueKind.True) metin = "true";
            else if (deger.ValueKind == JsonValueKind.False) metin = "false";
            else continue;

            if (metin != "")
                parcalar.Add(ad + "=" + Uri.EscapeDataString(metin));
        }
        return string.Join("&", parcalar);
    }

    // JS'ten gelen verideki bir metin alanı; yoksa boş metin.
    private static string Yazi(JsonElement veri, string ad) =>
        veri.ValueKind == JsonValueKind.Object && veri.TryGetProperty(ad, out var deger) && deger.ValueKind == JsonValueKind.String
            ? deger.GetString() ?? ""
            : "";
}
