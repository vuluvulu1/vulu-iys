using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;
using Vulu.Desktop;

namespace Vulu.Kurulum;

// Uygulamanın kurulum programı: içine gömülü uygulamayı kullanıcının program klasörüne açar, Başlat menüsü ve
// masaüstü kısayollarını oluşturur ve "Program ekle/kaldır" listesine kaydeder. Her şey o kullanıcıya özeldir;
// yönetici izni gerekmez. Yeniden çalıştırılırsa kurulu sürümün üzerine yazar (güncelleme).
//
// "/sessiz" ile çalıştırılırsa soru sormaz ve uygulamayı açmaz.
internal static class Program
{
    private const string GomuluUygulama = "uygulama.zip";

    [STAThread]
    private static int Main(string[] args)
    {
        var sessiz = args.Contains(KurulumBilgisi.SessizSecenegi, StringComparer.OrdinalIgnoreCase);
        var surum = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        try
        {
            if (!sessiz && !WindowsUyumlulugu.Sor(
                    $"{Urun.Ad} {surum} bu bilgisayara kurulacak.\n\nKlasör: {KurulumBilgisi.Klasor}\n\nDevam edilsin mi?"))
                return 1;

            if (UygulamaAcik())
                return Hata("Uygulama şu an açık. Kapatıp kurulumu yeniden çalıştırın.");

            // Dosyaların açılması birkaç saniye sürer; bu sırada bekleme penceresi gösterilir.
            if (sessiz) DosyalariYerlestir();
            else IletiKutusu.Calistir("Kuruluyor…\n\nDosyalar yerleştiriliyor, lütfen bekleyin.", DosyalariYerlestir);

            foreach (var kisayol in KurulumBilgisi.Kisayollar)
                Kisayol.Olustur(kisayol, KurulumBilgisi.UygulamaYolu, Urun.Ad);
            KaldirmaKaydiniYaz(surum);
            EskiAdinIzleriniSil();

            if (sessiz) return 0;

            if (!WindowsUyumlulugu.WebView2Kurulu())
            {
                WindowsUyumlulugu.BilgiGoster("Kurulum tamamlandı.\n\nUygulamanın çalışması için Microsoft Edge WebView2 Runtime "
                    + $"gerekli, ancak bu bilgisayarda kurulu değil. Şu adresten ücretsiz kurabilirsiniz:\n{WindowsUyumlulugu.WebView2IndirmeAdresi}");
                return 0;
            }

            if (WindowsUyumlulugu.Sor("Kurulum tamamlandı. Başlat menüsüne ve masaüstüne kısayol eklendi.\n\nUygulama şimdi açılsın mı?"))
                using (Process.Start(new ProcessStartInfo(KurulumBilgisi.UygulamaYolu) { WorkingDirectory = KurulumBilgisi.Klasor })) { }

            return 0;
        }
        catch (Exception hata) when (hata is IOException or UnauthorizedAccessException or InvalidDataException
            or System.Runtime.InteropServices.COMException or ArgumentException or System.Security.SecurityException)
        {
            return Hata($"Kurulum tamamlanamadı:\n{hata.Message}");
        }
    }

    // Dosyalar önce yan klasöre açılır, sonra eski kurulumla yer değiştirilir: açma yarıda kalırsa çalışan
    // kurulum bozulmaz.
    private static void DosyalariYerlestir()
    {
        using var gomulu = Assembly.GetExecutingAssembly().GetManifestResourceStream(GomuluUygulama)
            ?? throw new InvalidDataException("Bu kurulum dosyası uygulamayı içermiyor; yeniden indirin.");

        var hedef = KurulumBilgisi.Klasor;
        var gecici = hedef + ".yeni";

        if (Directory.Exists(gecici)) Directory.Delete(gecici, recursive: true);
        Directory.CreateDirectory(gecici);

        // ExtractToDirectory, klasörün dışına çıkmaya çalışan girdileri kendisi reddeder.
        using (var arsiv = new ZipArchive(gomulu, ZipArchiveMode.Read))
            arsiv.ExtractToDirectory(gecici);

        if (!File.Exists(Path.Combine(gecici, KurulumBilgisi.UygulamaExe)))
            throw new InvalidDataException("Kurulum dosyası bozuk; yeniden indirin.");

        if (Directory.Exists(hedef)) Directory.Delete(hedef, recursive: true);
        Directory.Move(gecici, hedef);
    }

    private static void KaldirmaKaydiniYaz(string surum)
    {
        using var anahtar = Registry.CurrentUser.CreateSubKey(KurulumBilgisi.KaldirmaAnahtari);
        anahtar.SetValue("DisplayName", Urun.Ad);
        anahtar.SetValue("DisplayVersion", surum);
        anahtar.SetValue("Publisher", Urun.Yayinci);
        anahtar.SetValue("InstallLocation", KurulumBilgisi.Klasor);
        anahtar.SetValue("DisplayIcon", KurulumBilgisi.UygulamaYolu);
        anahtar.SetValue("UninstallString", $"\"{KurulumBilgisi.UygulamaYolu}\" {KurulumBilgisi.KaldirmaSecenegi}");
        anahtar.SetValue("NoModify", 1, RegistryValueKind.DWord);
        anahtar.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        anahtar.SetValue("EstimatedSize", KlasorBoyutuKb(KurulumBilgisi.Klasor), RegistryValueKind.DWord);
    }

    private static void EskiAdinIzleriniSil()
    {
        foreach (var kisayol in KurulumBilgisi.EskiKisayollar)
            File.Delete(kisayol);
        Registry.CurrentUser.DeleteSubKeyTree(KurulumBilgisi.EskiKaldirmaAnahtari, throwOnMissingSubKey: false);
    }

    private static int KlasorBoyutuKb(string klasor) =>
        (int)(new DirectoryInfo(klasor).EnumerateFiles("*", SearchOption.AllDirectories).Sum(dosya => dosya.Length) / 1024);

    // Yalnızca kurulum klasöründen çalışan kopya engeldir; başka yerden açılmış bir kopya dosyaları kilitlemez.
    private static bool UygulamaAcik() =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(KurulumBilgisi.UygulamaExe)).Any(surec =>
        {
            try
            {
                return string.Equals(surec.MainModule?.FileName, KurulumBilgisi.UygulamaYolu, StringComparison.OrdinalIgnoreCase);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        });

    private static int Hata(string mesaj)
    {
        WindowsUyumlulugu.HataGoster(mesaj);
        return 1;
    }
}
