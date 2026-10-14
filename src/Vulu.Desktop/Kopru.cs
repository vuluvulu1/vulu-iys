using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Photino.NET;
using Vulu.Desktop.Servisler;
using Vulu.Shared;

namespace Vulu.Desktop;

// Ekranlar (JS) ile C# arasındaki tek bağlantı.
//
// JS bir istek yollar:  { "no": 7, "islem": "giris_yap", "veri": { ... } }
// C# işi yapar ve aynı numarayla cevap verir:
//     { "no": 7, "basarili": true, "veri": { ... } }   ya da   { "no": 7, "basarili": false, "hata": "..." }
// C# kendiliğinden de mesaj yollayabilir (örneğin indirme yüzdesi):  { "olay": "indirme", "veri": 42 }
//
// API'yi her zaman C# çağırır; token ve oturum bilgisi JS tarafına hiç gelmez.
public static class Kopru
{
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

    // JS'e giden cevap.
    private sealed class Cevap
    {
        public int No { get; set; }
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
    }

    // Photino'nun olay işleyicisi Task döndüremediği için "async void"; bu yüzden hiçbir hata dışarı kaçmamalı.
    private static async void MesajGeldi(object? gonderen, string metin)
    {
        var no = 0;
        Cevap cevap;
        try
        {
            using var belge = JsonDocument.Parse(metin);
            no = belge.RootElement.GetProperty("no").GetInt32();
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

    // Her istek adının ne yaptığı burada okunur.
    private static async Task<Cevap> IstegiIsle(string islem, JsonElement veri)
    {
        switch (islem)
        {
            // --- Giriş ---
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

            default:
                return Basarisiz("Bilinmeyen işlem: " + islem);
        }
    }

    // Giriş formu; uygulamanın güncel olduğu, sunucuya ulaşıldığı ve sürümün uyumlu olduğu görülmeden açılmaz.
    private static async Task<Cevap> BaslangicKontrolu()
    {
        if (_oturum.GirisYapildi)
            return Tamam(new { durum = "girisYapildi" });

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
            var sonuc = await _api.Getir<IsletmeYaniti>("api/isletme");
            if (sonuc is { Basarili: true, Deger: not null })
                _oturum.IsletmeyiAyarla(sonuc.Deger);
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

    // --- Yardımcılar ---

    private static Cevap Tamam(object? veri) => new() { Basarili = true, Veri = veri };

    private static Cevap Basarisiz(string hata) => new() { Basarili = false, Hata = hata };

    private static Cevap OturumYok() => new() { Basarili = false, Hata = "Oturum kapandı.", OturumKapandi = true };

    // JS'ten gelen verideki bir metin alanı; yoksa boş metin.
    private static string Yazi(JsonElement veri, string ad) =>
        veri.ValueKind == JsonValueKind.Object && veri.TryGetProperty(ad, out var deger) && deger.ValueKind == JsonValueKind.String
            ? deger.GetString() ?? ""
            : "";
}
