using System.Runtime.Versioning;

namespace Vulu.Desktop;

// Kurulumun bilgisayarda bıraktığı izler: klasör, kısayollar ve "Program ekle/kaldır" kaydı. Kurulum programı
// (tools/Vulu.Kurulum) bunları oluşturur, uygulamanın kendisi kaldırırken siler; ikisi aynı tanımları kullansın
// diye bu dosya kurulum projesine de bağlanır.
[SupportedOSPlatform("windows")]
internal static class KurulumBilgisi
{
    public const string UrunAdi = "vulu Teknik Servis Yönetim Sistemi";
    public const string Yayinci = "vulu";
    public const string UygulamaExe = "Vulu.Desktop.exe";

    // Uygulama bu seçenekle çalıştırılınca kendini kaldırır.
    public const string KaldirmaSecenegi = "--kaldir";

    // Kurulum ve kaldırma bu seçenekle soru sormadan çalışır.
    public const string SessizSecenegi = "/sessiz";

    // Kayıt yalnızca bu kullanıcı içindir (HKEY_CURRENT_USER); kurulum yönetici izni istemez.
    public const string KaldirmaAnahtari = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VuluTeknikServis";

    private const string KisayolAdi = "vulu Teknik Servis.lnk";

    public static string Klasor =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Vulu");

    public static string UygulamaYolu => Path.Combine(Klasor, UygulamaExe);

    public static string[] Kisayollar =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), KisayolAdi),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), KisayolAdi)
    ];
}
