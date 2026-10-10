using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Vulu.Desktop;

// Pencere açılmadan önce yapılan Windows'a özgü kontroller ve hata bildirimi.
[SupportedOSPlatform("windows")]
internal static class WindowsUyumlulugu
{
    public const string WebView2IndirmeAdresi = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private const string WebView2Anahtari = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    // Arayüz WebView2 Runtime ile çizilir. Windows 11'de hazır gelir; Windows 10'da çoğunlukla Edge ile birlikte
    // kuruludur ama garanti değildir. Kurulum makine ya da kullanıcı düzeyinde olabilir.
    public static bool WebView2Kurulu() =>
        SurumVar(Registry.LocalMachine, $@"SOFTWARE\WOW6432Node\{WebView2Anahtari}")
        || SurumVar(Registry.LocalMachine, $@"SOFTWARE\{WebView2Anahtari}")
        || SurumVar(Registry.CurrentUser, $@"Software\{WebView2Anahtari}");

    private static bool SurumVar(RegistryKey kok, string yol)
    {
        using var anahtar = kok.OpenSubKey(yol);
        return anahtar?.GetValue("pv") is string surum && surum.Length > 0 && surum != "0.0.0.0";
    }

    public static void HataGoster(string mesaj)
    {
        const uint HataSimgesi = 0x10;
        MessageBoxW(IntPtr.Zero, mesaj, "vulu", HataSimgesi);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
