using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Vulu.Api.Kimlik;

public class TokenServisi
{
    public const string KimlikTalebi = "sub";
    public const string AdTalebi = "name";
    public const string RolTalebi = "role";

    // Oturumun ait olduğu işletmenin kimliği. Vulu yöneticisinin tokenında bulunmaz.
    public const string IsletmeTalebi = "isletme";

    // Hesabın oturum damgası; hesaptaki güncel değerle eşleşmeyen token reddedilir.
    public const string DamgaTalebi = "damga";

    // Vulu yöneticisinin rolü. İşletme personelinin rolleri (Admin, Teknisyen) ile karışmaz.
    public const string PlatformRolu = "Platform";

    private readonly JwtAyarlari _ayarlar;

    public TokenServisi(IOptions<JwtAyarlari> ayarlar) => _ayarlar = ayarlar.Value;

    public (string Token, DateTime Bitis) ErisimTokeniUret(string kimlik, string ad, string rol, string damga, string? isletmeId)
    {
        var bitis = DateTime.UtcNow.AddMinutes(_ayarlar.ErisimDakika);

        var talepler = new List<Claim>
        {
            new(KimlikTalebi, kimlik),
            new(AdTalebi, ad),
            new(RolTalebi, rol),
            new(DamgaTalebi, damga)
        };
        if (isletmeId is not null)
            talepler.Add(new Claim(IsletmeTalebi, isletmeId));

        var tanim = new SecurityTokenDescriptor
        {
            Issuer = _ayarlar.Yayinci,
            Audience = _ayarlar.Hedef,
            Expires = bitis,
            Subject = new ClaimsIdentity(talepler),
            SigningCredentials = new SigningCredentials(ImzaAnahtari(_ayarlar), SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(tanim), bitis);
    }

    public DateTime YenilemeBitisi() => DateTime.UtcNow.AddDays(_ayarlar.YenilemeGun);

    public static SymmetricSecurityKey ImzaAnahtari(JwtAyarlari ayarlar) =>
        new(Encoding.UTF8.GetBytes(ayarlar.Anahtar));

    public static string YenilemeTokeniUret() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hashle(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
