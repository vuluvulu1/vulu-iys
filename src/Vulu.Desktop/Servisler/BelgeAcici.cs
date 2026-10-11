using System.Diagnostics;

namespace Vulu.Desktop.Servisler;

// İndirilen belgeleri geçici klasöre yazar ve bilgisayarın varsayılan uygulamasıyla açar.
public static class BelgeAcici
{
    // Başarılıysa null, değilse kullanıcıya gösterilecek hata mesajını döner.
    public static string? Ac(string dosyaAdi, byte[] icerik)
    {
        try
        {
            var klasor = Path.Combine(Path.GetTempPath(), "vulu");
            Directory.CreateDirectory(klasor);

            // Dosya adı sunucudan gelen belge numarasından üretilir; yol ayracı içeremesin.
            var yol = Path.Combine(klasor, Path.GetFileName(dosyaAdi));
            File.WriteAllBytes(yol, icerik);

            using var surec = Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return $"Belge açılamadı: {ex.Message}";
        }
    }

    // Bir web ya da e-posta bağlantısını bilgisayarın varsayılan uygulamasında açar. Kabuğa verilen metin bir
    // program yolu da olabileceği için yalnızca https ve mailto adresleri kabul edilir.
    public static string? BaglantiAc(string adres)
    {
        if (!Uri.TryCreate(adres, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeMailto))
            return "Bağlantı açılamadı: geçersiz adres.";

        try
        {
            using var surec = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return $"Bağlantı açılamadı: {ex.Message}";
        }
    }
}
