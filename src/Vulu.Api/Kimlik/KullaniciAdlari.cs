namespace Vulu.Api.Kimlik;

public static class KullaniciAdlari
{
    // Kullanıcı adları büyük/küçük harf ayrımı olmadan tekildir; saklarken ve ararken aynı biçime getirilir.
    public static string Normalle(string kullaniciAdi) => kullaniciAdi.Trim().ToLowerInvariant();
}
