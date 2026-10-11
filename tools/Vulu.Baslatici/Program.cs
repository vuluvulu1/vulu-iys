using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Vulu.Baslatici;

// Test paketinin giriş noktası: API'yi arka planda başlatır, hazır olunca masaüstü uygulamasını açar,
// pencere kapanınca API'yi de kapatır. Klasör düzeni paketle.ps1 tarafından kurulur.
internal static class Program
{
    private static readonly TimeSpan ApiBeklemeSuresi = TimeSpan.FromSeconds(30);

    private static async Task<int> Main()
    {
        var kok = AppContext.BaseDirectory;
        var apiExe = Path.Combine(kok, "api", "Vulu.Api.exe");
        var masaustuExe = Path.Combine(kok, "desktop", "Vulu.Desktop.exe");
        var gunluk = Path.Combine(kok, "api.log");

        if (!File.Exists(apiExe) || !File.Exists(masaustuExe))
            return Hata($"Paket eksik. Beklenen dosyalar:\n{apiExe}\n{masaustuExe}");

        var apiAdresi = ApiAdresiniOku(Path.Combine(kok, "desktop", "appsettings.json"));
        if (apiAdresi is null)
            return Hata("desktop\\appsettings.json içinde ApiAdresi bulunamadı.");

        Process? api = null;
        try
        {
            // Önceki çalıştırmadan kalan ya da elle başlatılmış bir API varsa o kullanılır.
            if (!await ApiYanitVeriyorMu(apiAdresi))
            {
                api = ApiyiBaslat(apiExe, apiAdresi, gunluk);

                var bitis = DateTime.UtcNow + ApiBeklemeSuresi;
                while (!await ApiYanitVeriyorMu(apiAdresi))
                {
                    if (api.HasExited)
                        return Hata($"API başlatılamadı (çıkış kodu {api.ExitCode}).\nAyrıntı: {gunluk}");
                    if (DateTime.UtcNow > bitis)
                        return Hata($"API {ApiBeklemeSuresi.TotalSeconds} saniyede yanıt vermedi.\nAyrıntı: {gunluk}");
                    await Task.Delay(300);
                }
            }

            using var masaustu = Process.Start(new ProcessStartInfo(masaustuExe)
            {
                WorkingDirectory = Path.GetDirectoryName(masaustuExe)!,
                UseShellExecute = false
            })!;
            await masaustu.WaitForExitAsync();
            return 0;
        }
        finally
        {
            if (api is { HasExited: false })
                api.Kill(entireProcessTree: true);
            api?.Dispose();
        }
    }

    private static Process ApiyiBaslat(string apiExe, Uri apiAdresi, string gunluk)
    {
        var baslangic = new ProcessStartInfo(apiExe)
        {
            WorkingDirectory = Path.GetDirectoryName(apiExe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Test paketi geliştirme ayarlarıyla çalışır: yerel MongoDB ve pakete kopyalanan appsettings.Local.json.
        baslangic.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        baslangic.Environment["ASPNETCORE_URLS"] = apiAdresi.GetLeftPart(UriPartial.Authority);

        var yazici = new StreamWriter(gunluk, append: false) { AutoFlush = true };
        var api = new Process { StartInfo = baslangic };
        api.OutputDataReceived += (_, e) => Yaz(yazici, e.Data);
        api.ErrorDataReceived += (_, e) => Yaz(yazici, e.Data);
        api.Start();
        api.BeginOutputReadLine();
        api.BeginErrorReadLine();
        return api;
    }

    private static void Yaz(StreamWriter yazici, string? satir)
    {
        if (satir is null) return;
        lock (yazici) yazici.WriteLine(satir);
    }

    private static async Task<bool> ApiYanitVeriyorMu(Uri apiAdresi)
    {
        try
        {
            using var istemci = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            // Durum kodu önemli değil: veritabanı kapalıyken 503 dönse de API ayaktadır.
            using var yanit = await istemci.GetAsync(new Uri(apiAdresi, "api/saglik"));
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static Uri? ApiAdresiniOku(string ayarDosyasi)
    {
        using var belge = JsonDocument.Parse(File.ReadAllText(ayarDosyasi));
        return belge.RootElement.TryGetProperty("ApiAdresi", out var deger)
            && Uri.TryCreate(deger.GetString(), UriKind.Absolute, out var adres)
            ? adres
            : null;
    }

    private static int Hata(string mesaj)
    {
        const uint HataSimgesi = 0x10;
        _ = MessageBoxW(IntPtr.Zero, mesaj, "vulu", HataSimgesi);
        return 1;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
