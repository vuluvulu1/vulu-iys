namespace Vulu.Api.Kimlik;

// Şifrelerin saklanması ve doğrulanması tek yerden yapılır; hiçbir yerde düz şifre saklanmaz ya da günlüğe yazılmaz.
public static class Sifreler
{
    // bcrypt maliyeti: her artış doğrulama süresini ikiye katlar. 11, bir doğrulamayı ~0,1 saniyede tutar.
    private const int IsYuku = 11;

    // Hesap bulunamadığında da bir doğrulama yapılır ki yanıt süresi hesabın varlığını ele vermesin.
    private static readonly string SahteHash = Hashle("var-olmayan-hesap-icin-sahte-sifre");

    public static string Hashle(string sifre) => BCrypt.Net.BCrypt.HashPassword(sifre, IsYuku);

    // hash boşsa (hesap yoksa) sahte hash doğrulanır ve sonuç her zaman false olur.
    public static bool Dogrula(string sifre, string? hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(sifre, hash ?? SahteHash) && hash is not null;
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Veritabanındaki hash bozuksa giriş reddedilir; hata olarak yüzeye çıkmaz.
            return false;
        }
    }

    // Hesabın açık oturumlarını tanımlayan rastgele değer; değişince eski erişim tokenları geçersiz kalır.
    public static string YeniOturumDamgasi() => TokenServisi.YenilemeTokeniUret()[..22];
}
