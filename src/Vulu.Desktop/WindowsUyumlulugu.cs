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

    // Başlık çubuğunu arayüzün kahve paletine boyar (renkler wwwroot/css/app.css ile aynı).
    // Renk ayarı Windows 11'de çalışır; Windows 10 bunu desteklemediği için orada çubuk yalnızca koyu temaya alınır.
    public static void BaslikCubugunuBoya(IntPtr pencere)
    {
        const int KoyuTema = 20, KenarRengi = 34, BaslikRengi = 35, MetinRengi = 36;

        Ayarla(pencere, KoyuTema, 1);
        Ayarla(pencere, BaslikRengi, Renk(0x2b, 0x1a, 0x10));
        Ayarla(pencere, MetinRengi, Renk(0xf3, 0xe6, 0xd6));
        Ayarla(pencere, KenarRengi, Renk(0x4a, 0x2e, 0x1c));
    }

    // Windows renkleri 0x00BBGGRR sırasıyla bekler.
    private static int Renk(int kirmizi, int yesil, int mavi) => kirmizi | (yesil << 8) | (mavi << 16);

    // Desteklenmeyen ayarlar hata kodu döndürür ve yok sayılır; pencere varsayılan görünümüyle kalır.
    private static void Ayarla(IntPtr pencere, int ayar, int deger) =>
        DwmSetWindowAttribute(pencere, ayar, ref deger, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void HataGoster(string mesaj)
    {
        const uint HataSimgesi = 0x10;
        MessageBoxW(IntPtr.Zero, mesaj, "vulu", HataSimgesi);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
