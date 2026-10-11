using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vulu.Desktop.Servisler;

// Yayımlanmış daha yeni bir sürüm: numarası, kurulum dosyasının adresi, boyutu ve SHA-256 özeti.
public record YeniSurum(Version Surum, Uri Indirme, long Boyut, string Sha256);

// Uygulamanın kendini güncellemesi. Sürümler GitHub'da yayımlanır; "en güncel sürüm hangisi" bilgisi oradan
// okunur, başka bir yerde (veritabanı, ayar) sürüm numarası tutulmaz.
//
// İnternetten indirilen bir program çalıştırıldığı için kurallar dardır: dosya yalnızca bu deponun sürüm
// sayfasından, şifreli bağlantıyla indirilir ve GitHub'ın bildirdiği boyut ve özetle birebir eşleşmeden
// çalıştırılmaz. Güven GitHub hesabına dayanır; hesap ele geçirilirse bu denetimler koruma sağlamaz.
public static class Guncelleyici
{
    private const string KurulumDosyasi = "Vulu-Kurulum.exe";
    private const string OzetOnEki = "sha256:";

    private static readonly Uri SonSurumAdresi = new($"https://api.github.com/repos/{Urun.Depo}/releases/latest");
    private static readonly string IndirmeOnEki = $"https://github.com/{Urun.Depo}/releases/download/";
    private static readonly string Klasor = Path.Combine(YerelAyarlar.Klasor, "guncelleme");

    // API'ye giden istemciden ayrıdır: oturum bilgisi taşımaz, GitHub'a yalnızca bu iki istek gider.
    private static readonly HttpClient Http = Olustur();

    public static Version MevcutSurum { get; } = UcHaneli(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    // Yalnızca kurulum programının yerleştirdiği kopya kendini günceller; geliştirme sırasında çalıştırılan ya da
    // elle kopyalanmış bir kopya güncelleme aramaz.
    public static bool KuruluKopyaMi =>
        OperatingSystem.IsWindows()
        && string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), KurulumBilgisi.Klasor, StringComparison.OrdinalIgnoreCase);

    // Daha yeni bir sürüm varsa onu, yoksa null döner. Sürüm bilgisine ulaşılamazsa da null döner: GitHub'a
    // erişilemiyor diye uygulama kullanılamaz hale gelmemeli.
    public static async Task<YeniSurum?> KontrolEt()
    {
        if (!KuruluKopyaMi) return null;

        try
        {
            using var zamanAsimi = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var yanit = await Http.GetAsync(SonSurumAdresi, zamanAsimi.Token);
            if (!yanit.IsSuccessStatusCode) return null;

            using var belge = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync(zamanAsimi.Token));
            return SurumuOku(belge.RootElement) is { } yeni && yeni.Surum > MevcutSurum ? yeni : null;
        }
        catch (Exception hata) when (hata is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    // Kurulum dosyasını indirir, doğrular, güncelleme kipinde başlatır ve uygulamayı kapatır. Başarılıysa geri
    // dönmez; başarısız olursa kullanıcıya gösterilecek mesajı döner.
    public static async Task<string> IndirVeKur(YeniSurum yeni, Action<int> yuzdeBildir)
    {
        if (!OperatingSystem.IsWindows()) return "Güncelleme yalnızca Windows'ta desteklenir.";

        var dosya = Path.Combine(Klasor, KurulumDosyasi);

        try
        {
            ArtiklariSil();
            Directory.CreateDirectory(Klasor);

            var ozet = await Indir(yeni, dosya, yuzdeBildir);
            if (!string.Equals(ozet, yeni.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(dosya);
                return "İndirilen dosya doğrulanamadı (özeti yayımlanan sürümle uyuşmuyor). Yeniden deneyin.";
            }

            // Kurulum, bu süreç kapanana kadar bekler, dosyaları değiştirir ve uygulamayı yeniden açar.
            using var kurulum = Process.Start(new ProcessStartInfo(dosya)
            {
                Arguments = KurulumBilgisi.GuncellemeSecenegi,
                WorkingDirectory = Klasor,
                UseShellExecute = false
            });
        }
        catch (Exception hata) when (hata is HttpRequestException or OperationCanceledException or IOException
            or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return $"Güncelleme indirilemedi: {hata.Message}";
        }

        Environment.Exit(0);
        return "";
    }

    // Önceki güncellemeden kalan kurulum dosyası; uygulama açılırken silinir.
    public static void ArtiklariSil()
    {
        try
        {
            if (Directory.Exists(Klasor)) Directory.Delete(Klasor, recursive: true);
        }
        catch (Exception hata) when (hata is IOException or UnauthorizedAccessException)
        {
            // Dosya hâlâ kullanımdaysa (kurulum yeni bitmişse) bir sonraki açılışta silinir.
        }
    }

    // Dosyayı diske yazarken özetini de hesaplar; böylece dosya doğrulama için ikinci kez okunmaz.
    private static async Task<string> Indir(YeniSurum yeni, string dosya, Action<int> yuzdeBildir)
    {
        using var yanit = await Http.GetAsync(yeni.Indirme, HttpCompletionOption.ResponseHeadersRead);
        yanit.EnsureSuccessStatusCode();

        await using var kaynak = await yanit.Content.ReadAsStreamAsync();
        await using var hedef = File.Create(dosya);
        using var ozet = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var tampon = new byte[128 * 1024];
        long toplam = 0;
        var sonYuzde = -1;
        int okunan;
        while ((okunan = await kaynak.ReadAsync(tampon)) > 0)
        {
            toplam += okunan;
            // Bildirilenden büyük bir dosya beklenen dosya değildir; diski doldurmasına izin verilmez.
            if (toplam > yeni.Boyut) throw new IOException("İndirilen dosya beklenenden büyük.");

            await hedef.WriteAsync(tampon.AsMemory(0, okunan));
            ozet.AppendData(tampon, 0, okunan);

            var yuzde = (int)(toplam * 100 / yeni.Boyut);
            if (yuzde != sonYuzde) yuzdeBildir(sonYuzde = yuzde);
        }

        if (toplam != yeni.Boyut) throw new IOException("İndirme yarıda kaldı.");
        return Convert.ToHexString(ozet.GetHashAndReset());
    }

    // GitHub'ın "en son sürüm" yanıtından sürüm numarasını ve kurulum dosyasını çıkarır. Beklenen biçimde olmayan,
    // dosyası bu deponun sürüm sayfasını göstermeyen ya da özeti bildirilmemiş bir sürüm yok sayılır.
    private static YeniSurum? SurumuOku(JsonElement surum)
    {
        if (!surum.TryGetProperty("tag_name", out var etiket)
            || !Version.TryParse(etiket.GetString()?.TrimStart('v'), out var numara)
            || !surum.TryGetProperty("assets", out var dosyalar) || dosyalar.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var dosya in dosyalar.EnumerateArray())
        {
            if (dosya.TryGetProperty("name", out var ad) && ad.GetString() == KurulumDosyasi
                && dosya.TryGetProperty("browser_download_url", out var adres) && adres.GetString() is { } indirme
                && indirme.StartsWith(IndirmeOnEki, StringComparison.Ordinal)
                && dosya.TryGetProperty("size", out var boyut) && boyut.TryGetInt64(out var bayt) && bayt > 0
                && dosya.TryGetProperty("digest", out var ozet) && ozet.GetString() is { } deger
                && deger.StartsWith(OzetOnEki, StringComparison.OrdinalIgnoreCase))
            {
                return new YeniSurum(UcHaneli(numara), new Uri(indirme), bayt, deger[OzetOnEki.Length..]);
            }
        }

        return null;
    }

    // "1.2.3" ile "1.2.3.0" aynı sürümdür; karşılaştırma dördüncü haneye takılmasın.
    private static Version UcHaneli(Version surum) => new(surum.Major, surum.Minor, Math.Max(surum.Build, 0));

    private static HttpClient Olustur()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub, kendini tanıtmayan istekleri reddeder.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("vulu-iys", MevcutSurumMetni()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    private static string MevcutSurumMetni() =>
        (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString(3);
}
