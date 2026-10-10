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
        var ayarlar = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var apiAdresi = ayarlar["ApiAdresi"]
            ?? throw new InvalidOperationException("appsettings.json içinde ApiAdresi ayarlanmamış.");

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);

        builder.Services.AddLogging();
        builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(apiAdresi) });
        builder.Services.AddSingleton<OturumServisi>();
        builder.Services.AddSingleton<ApiIstemcisi>();

        builder.RootComponents.Add<App>("app");

        var app = builder.Build();

        app.MainWindow
            .SetTitle("vulu Teknik Servis Yönetim Sistemi")
            .SetIconFile(Path.Combine(AppContext.BaseDirectory, "logo.ico"))
            .SetSize(1280, 800)
            .Center();

        AppDomain.CurrentDomain.UnhandledException += (_, hata) =>
            app.MainWindow.ShowMessage("Beklenmeyen hata", hata.ExceptionObject.ToString());

        app.Run();
    }
}
