using System.Diagnostics;

namespace Vulu.Desktop.Servisler;

// İndirilen belgeleri geçici klasöre yazar ve bilgisayarın varsayılan uygulamasıyla açar.
public static class BelgeAcici
{
    private static readonly string Klasor = Path.Combine(Path.GetTempPath(), "vulu");

    // Başarılıysa null, değilse kullanıcıya gösterilecek hata mesajını döner.
    public static string? Ac(string dosyaAdi, byte[] icerik)
    {
        try
        {
            Directory.CreateDirectory(Klasor);

            // Dosya adı sunucudan gelen belge numarasından üretilir; yol ayracı içeremesin.
            var yol = Path.Combine(Klasor, Path.GetFileName(dosyaAdi));
            File.WriteAllBytes(yol, icerik);

            using var surec = Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return $"Belge açılamadı: {ex.Message}";
        }
    }

    // Kullanıcının saklamak üzere istediği dosyalar (Excel dökümleri) İndirilenler klasörüne kaydedilir ve açılır.
    // Kaydedilen dosyanın adını ya da hata mesajını döner; aynı adlı dosya başka bir programda açıksa yeni bir ad
    // denenir.
    public static (string? DosyaAdi, string? Hata) KaydetVeAc(string dosyaAdi, byte[] icerik)
    {
        const int EnFazlaDeneme = 20;

        try
        {
            var klasor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(klasor)) klasor = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            var ad = Path.GetFileNameWithoutExtension(dosyaAdi);
            var uzanti = Path.GetExtension(dosyaAdi);
            for (var deneme = 1; ; deneme++)
            {
                var yol = Path.Combine(klasor, deneme == 1 ? ad + uzanti : $"{ad} ({deneme}){uzanti}");
                try
                {
                    File.WriteAllBytes(yol, icerik);
                }
                catch (IOException) when (deneme < EnFazlaDeneme)
                {
                    continue;
                }

                using var surec = Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true });
                return (Path.GetFileName(yol), null);
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Dosya kaydedildi ama bu bilgisayarda onu açacak bir program yok.
            return (dosyaAdi, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"Dosya kaydedilemedi: {ex.Message}");
        }
    }

    // Açılan belgeler müşteri bilgisi içerir; bilgisayarda birikmesinler diye uygulama açılırken ve kapanırken
    // silinir. O an bir görüntüleyicide açık olan dosya silinemez; bir sonraki seferde silinir.
    public static void GeciciBelgeleriSil()
    {
        if (!Directory.Exists(Klasor)) return;

        foreach (var dosya in Directory.EnumerateFiles(Klasor))
        {
            try
            {
                File.Delete(dosya);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
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
