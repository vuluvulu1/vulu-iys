namespace Vulu.Desktop;

public static class Metin
{
    // Formlardaki isteğe bağlı alanlar boş bırakıldıysa API'ye hiç gönderilmez.
    public static string? BosIseNull(string? deger) => string.IsNullOrWhiteSpace(deger) ? null : deger.Trim();
}
