using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Vulu.Desktop.Servisler;

namespace Vulu.Desktop;

// "Program ekle/kaldır" listesinden çağrılır: kurulumun bıraktığı kısayolları, kaydı, program klasörünü ve
// uygulamanın bu kullanıcı için tuttuğu veriyi (tercihler, tarayıcı önbelleği) siler. Geride bir şey kalmaz.
[SupportedOSPlatform("windows")]
internal static class Kaldirici
{
    // sessiz: soru sormaz ve sonuç iletisi göstermez (otomatik kaldırma için).
    public static void Calistir(bool sessiz)
    {
        var klasor = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        // Klasör silineceği için yalnızca kurulumun yerleştirdiği kopya kendini kaldırabilir. Geliştirme klasöründen
        // ya da elle kopyalanmış bir yerden çalıştırılırsa oradaki dosyalara dokunulmaz.
        if (!string.Equals(klasor, KurulumBilgisi.Klasor, StringComparison.OrdinalIgnoreCase))
        {
            WindowsUyumlulugu.HataGoster("Bu kopya kurulum programıyla yüklenmemiş; kaldırılacak bir kurulum yok.");
            return;
        }

        if (!sessiz && !WindowsUyumlulugu.Sor($"{KurulumBilgisi.UrunAdi} bu bilgisayardan kaldırılsın mı?"))
            return;

        if (BaskaKopyaCalisiyor())
        {
            WindowsUyumlulugu.HataGoster("Uygulama açık. Kapatıp yeniden deneyin.");
            return;
        }

        foreach (var kisayol in KurulumBilgisi.Kisayollar)
            File.Delete(kisayol);
        Registry.CurrentUser.DeleteSubKeyTree(KurulumBilgisi.KaldirmaAnahtari, throwOnMissingSubKey: false);

        // Çalışan bir program kendi dosyalarını silemez; klasörü, bu süreç kapandıktan sonra komut satırı siler.
        using var silici = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{klasor}\" & rmdir /s /q \"{YerelAyarlar.Klasor}\"",
            WorkingDirectory = Path.GetTempPath(),
            CreateNoWindow = true,
            UseShellExecute = false
        });

        if (!sessiz)
            WindowsUyumlulugu.BilgiGoster($"{KurulumBilgisi.UrunAdi} kaldırıldı.");
    }

    private static bool BaskaKopyaCalisiyor()
    {
        using var bu = Process.GetCurrentProcess();
        return Process.GetProcessesByName(bu.ProcessName).Any(surec => surec.Id != bu.Id);
    }
}
