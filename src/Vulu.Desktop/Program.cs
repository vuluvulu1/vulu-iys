using Microsoft.Extensions.Configuration;
using Photino.NET;
using Vulu.Desktop.Servisler;

namespace Vulu.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Kaldırma, Windows'un "Uygulamalar" listesinden bu seçenekle başlatılır; pencere açılmaz.
        if (OperatingSystem.IsWindows() && args.Contains(KurulumBilgisi.KaldirmaSecenegi))
        {
            Kaldirici.Calistir(sessiz: args.Contains(KurulumBilgisi.SessizSecenegi, StringComparer.OrdinalIgnoreCase));
            return;
        }

        if (OperatingSystem.IsWindows() && !WindowsUyumlulugu.WebView2Kurulu())
        {
            Hata("Bu uygulama için Microsoft Edge WebView2 Runtime gerekli, ancak bu bilgisayarda kurulu değil.\n\n"
                + $"Şu adresten ücretsiz kurabilirsiniz:\n{WindowsUyumlulugu.WebView2IndirmeAdresi}");
            return;
        }

        var ayarlar = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        if (!Uri.TryCreate(ayarlar["ApiAdresi"], UriKind.Absolute, out var apiAdresi))
        {
            Hata("appsettings.json içinde geçerli bir ApiAdresi yok.");
            return;
        }

        // Şifre ve tokenlar bu bağlantıdan geçer; aynı makinedeki geliştirme sunucusu dışında şifresiz bağlantı kabul edilmez.
        if (apiAdresi.Scheme != Uri.UriSchemeHttps && !apiAdresi.IsLoopback)
        {
            Hata($"ApiAdresi https ile başlamalı: {apiAdresi}");
            return;
        }

        var pencere = new PhotinoWindow()
            .SetTitle(Urun.Ad)
            .SetIconFile(Path.Combine(AppContext.BaseDirectory, "logo.ico"))
            // Tarayıcı bileşeni önbelleğini varsayılan olarak bütün Photino uygulamalarının paylaştığı bir klasöre
            // yazar; uygulamanın kendi klasörüne alınır ki kaldırılınca geride bir şey kalmasın.
            .SetTemporaryFilesPath(YerelAyarlar.TarayiciVerisi)
            // Photino varsayılan olarak boyutu ve konumu Windows'a bırakır; aşağıdaki boyut ve ortalama ancak
            // bu kapatılınca uygulanır.
            .SetUseOsDefaultSize(false)
            .SetUseOsDefaultLocation(false)
            .SetSize(1280, 800)
            .SetMinSize(900, 600)
            .Center();

        pencere.RegisterWindowCreatedHandler((_, _) =>
        {
            if (OperatingSystem.IsWindows())
                WindowsUyumlulugu.BaslikCubugunuBoya(pencere.WindowHandle);
        });

#if !DEBUG
        // Dağıtılan sürümde tarayıcı geliştirici araçları ve sağ tık menüsü kapalıdır.
        pencere
            .SetDevToolsEnabled(false)
            .SetContextMenuEnabled(false);
#endif

        // Ekranlardan (JS) gelen bütün istekler köprüden geçer.
        Kopru.Baslat(pencere, apiAdresi);

        BelgeAcici.GeciciBelgeleriSil();
        Guncelleyici.ArtiklariSil();
        try
        {
            pencere.Load(Path.Combine(AppContext.BaseDirectory, "wwwroot", "giris.html"));
            pencere.WaitForClose();
        }
        finally
        {
            BelgeAcici.GeciciBelgeleriSil();
        }
    }

    private static void Hata(string mesaj)
    {
        if (OperatingSystem.IsWindows())
            WindowsUyumlulugu.HataGoster(mesaj);
        else
            Console.Error.WriteLine(mesaj);
    }
}
