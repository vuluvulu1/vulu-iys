using System.Text.Json;

namespace Vulu.Desktop.Servisler;

// Bu bilgisayara özgü küçük tercihler (gizli bilgi içermez). Okunamaz ya da yazılamazsa sessizce yok sayılır;
// uygulama tercihsiz de çalışır.
public class YerelAyarlar
{
    private static readonly string Dosya = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vulu", "ayarlar.json");

    private record Icerik(string? SonIsletmeKodu);

    // Giriş ekranında işletme kodunun her seferinde yeniden yazılmaması için hatırlanır.
    public string SonIsletmeKodu
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
