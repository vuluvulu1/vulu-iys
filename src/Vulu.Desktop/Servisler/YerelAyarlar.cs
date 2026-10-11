using System.Text.Json;

namespace Vulu.Desktop.Servisler;

// Bu bilgisayara özgü küçük tercihler (gizli bilgi içermez). Okunamaz ya da yazılamazsa sessizce yok sayılır;
// uygulama tercihsiz de çalışır.
public static class YerelAyarlar
{
    // Uygulamanın bu kullanıcı için tuttuğu her şey (tercihler ve tarayıcı bileşeninin önbelleği) bu klasördedir;
    // uygulama kaldırılırken klasör de silinir.
    public static readonly string Klasor = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vulu");

    public static readonly string TarayiciVerisi = Path.Combine(Klasor, "tarayici");

    private static readonly string Dosya = Path.Combine(Klasor, "ayarlar.json");

    private sealed record Icerik(string? SonIsletmeKodu);

    // Giriş ekranında işletme kodunun her seferinde yeniden yazılmaması için hatırlanır.
    public static string SonIsletmeKodu
    {
        get
        {
            try
            {
                return File.Exists(Dosya)
                    ? JsonSerializer.Deserialize<Icerik>(File.ReadAllText(Dosya))?.SonIsletmeKodu ?? ""
                    : "";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return "";
            }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Dosya)!);
                File.WriteAllText(Dosya, JsonSerializer.Serialize(new Icerik(value)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
