namespace Vulu.Api.Altyapi;

public static class Metin
{
    // İsteğe bağlı alanlar boş gelirse veritabanına hiç yazılmaz.
    public static string? BosIseNull(string? deger) => string.IsNullOrWhiteSpace(deger) ? null : deger.Trim();
}
