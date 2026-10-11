using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using Vulu.Desktop.Servisler;

namespace Vulu.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);

        builder.Services.AddLogging();
        builder.Services.AddSingleton(new HttpClient { BaseAddress = apiAdresi });
        builder.Services.AddSingleton<OturumServisi>();
        builder.Services.AddSingleton<ApiIstemcisi>();

        builder.RootComponents.Add<App>("app");

        var app = builder.Build();

        app.MainWindow
            .SetTitle(Urun.Ad)
            .SetIconFile(Path.Combine(AppContext.BaseDirectory, "logo.ico"))
            // Tarayıcı bileşeni önbelleğini varsayılan olarak bütün Photino uygulamalarının paylaştığı bir klasöre
            // yazar; uygulamanın kendi klasörüne alınır ki kaldırılınca geride bir şey kalmasın.
            .SetTemporaryFilesPath(YerelAyarlar.TarayiciVerisi)
            .SetSize(1280, 800)
            .SetMinSize(900, 600)
            .Center();

        app.MainWindow.WindowCreated += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
                WindowsUyumlulugu.BaslikCubugunuBoya(app.MainWindow.WindowHandle);
        };

#if !DEBUG
        // Dağıtılan sürümde tarayıcı geliştirici araçları ve sağ tık menüsü kapalıdır.
        app.MainWindow
            .SetDevToolsEnabled(false)
            .SetContextMenuEnabled(false);
#endif

        // Kullanıcıya iç ayrıntı (çağrı yığını) değil, yalnızca hatanın kendisi gösterilir.
        AppDomain.CurrentDomain.UnhandledException += (_, hata) =>
            app.MainWindow.ShowMessage("Beklenmeyen hata",
                (hata.ExceptionObject as Exception)?.Message ?? "Uygulama beklenmeyen bir hatayla karşılaştı.");

        BelgeAcici.GeciciBelgeleriSil();
        try
        {
            app.Run();
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
