namespace Vulu.Api.Altyapi;

public static class Metin
{
    // İsteğe bağlı alanlar boş gelirse veritabanına hiç yazılmaz.
    public static string? BosIseNull(string? deger) => string.IsNullOrWhiteSpace(deger) ? null : deger.Trim();

    // Kullanıcının yazdığı metin günlüğe yazılırken satır sonları ve diğer denetim karakterleri atılır;
    // aksi halde araya sahte günlük satırları eklenebilir.
    public static string GunlukIcin(string deger) => string.Concat(deger.Where(harf => !char.IsControl(harf)));
}
