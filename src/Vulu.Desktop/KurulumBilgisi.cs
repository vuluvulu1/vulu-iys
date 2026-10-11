using System.Runtime.Versioning;

namespace Vulu.Desktop;

// Kurulumun bilgisayarda bıraktığı izler: klasör, kısayollar ve "Program ekle/kaldır" kaydı. Kurulum programı
// (tools/Vulu.Kurulum) bunları oluşturur, uygulamanın kendisi kaldırırken siler; ikisi aynı tanımları kullansın
// diye bu dosya kurulum projesine de bağlanır.
[SupportedOSPlatform("windows")]
internal static class KurulumBilgisi
{
    public const string UygulamaExe = "Vulu.Desktop.exe";

    // Uygulama bu seçenekle çalıştırılınca kendini kaldırır.
    public const string KaldirmaSecenegi = "--kaldir";

    // Kurulum ve kaldırma bu seçenekle soru sormadan çalışır.
    public const string SessizSecenegi = "/sessiz";

    // Uygulama kendini güncellerken kurulumu bu seçenekle başlatır: kurulum, uygulamanın kapanmasını bekler,
    // soru sormadan kurar ve uygulamayı yeniden açar.
    public const string GuncellemeSecenegi = "/guncelle";

    // Kayıt yalnızca bu kullanıcı içindir (HKEY_CURRENT_USER); kurulum yönetici izni istemez.
    public const string KaldirmaAnahtari = UygulamalarAnahtari + "VuluIsletmeYonetim";

    // Ürünün adı "vulu Teknik Servis Yönetim Sistemi" iken (0.2 sürümleri) kurulumun bıraktığı izler. Yeni kurulum
    // bunları temizler; yoksa eski kısayol ve "Uygulamalar" listesindeki eski satır yenisinin yanında kalırdı.
    public const string EskiKaldirmaAnahtari = UygulamalarAnahtari + "VuluTeknikServis";
    private const string EskiKisayolAdi = "vulu Teknik Servis.lnk";

    private const string UygulamalarAnahtari = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
    private const string KisayolAdi = Urun.Ad + ".lnk";

    public static string Klasor =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Vulu");

    public static string UygulamaYolu => Path.Combine(Klasor, UygulamaExe);

    public static string[] Kisayollar => KisayolYollari(KisayolAdi);

    public static string[] EskiKisayollar => KisayolYollari(EskiKisayolAdi);

    // Başlat menüsü ve masaüstü.
    private static string[] KisayolYollari(string ad) =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ad),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ad)
    ];
}
