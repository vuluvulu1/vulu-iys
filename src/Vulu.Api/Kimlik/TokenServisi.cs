using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Kimlik;

public class TokenServisi
{
    public const string KimlikTalebi = "sub";
    public const string AdTalebi = "name";
    public const string RolTalebi = "role";

    private readonly JwtAyarlari _ayarlar;

    public TokenServisi(IOptions<JwtAyarlari> ayarlar) => _ayarlar = ayarlar.Value;

    public (string Token, DateTime Bitis) ErisimTokeniUret(Kullanici kullanici)
    {
        var bitis = DateTime.UtcNow.AddMinutes(_ayarlar.ErisimDakika);

        var tanim = new SecurityTokenDescriptor
        {
            Issuer = _ayarlar.Yayinci,
            Audience = _ayarlar.Hedef,
            Expires = bitis,
            Subject = new ClaimsIdentity(
            [
                new Claim(KimlikTalebi, kullanici.Id),
                new Claim(AdTalebi, kullanici.KullaniciAdi),
                new Claim(RolTalebi, kullanici.Rol.ToString())
            ]),
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
