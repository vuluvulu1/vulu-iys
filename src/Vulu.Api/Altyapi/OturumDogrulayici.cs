using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Altyapi;

// İsteğin ait olduğu işletme. Kimlik doğrulamasından sonra OturumDogrulayici tarafından doldurulur.
public class GecerliIsletme
{
    public Isletme? Isletme { get; set; }

    public Isletme Zorunlu => Isletme ?? throw new InvalidOperationException("Bu istek bir işletmeye bağlı değil.");
}

// Erişim tokenı imzalı olduğu için tek başına geçerlidir ve süresi dolana kadar geri alınamaz. Bu sınıf her
// istekte tokenı veritabanındaki güncel durumla karşılaştırır: işletme askıya alınmışsa, hesap silinmiş ya da
// devre dışı bırakılmışsa, rolü ya da şifresi değişmişse istek reddedilir. Böylece yetkisi alınan biri, tokenının
// süresi dolana kadar çalışmaya devam edemez; imza anahtarı sızsa bile var olmayan bir hesap adına işlem yapılamaz.
public class OturumDogrulayici
{
    private readonly RequestDelegate _sonraki;

    public OturumDogrulayici(RequestDelegate sonraki) => _sonraki = sonraki;

    public async Task InvokeAsync(HttpContext baglam, Oturumlar oturumlar, GecerliIsletme gecerli,
        IProblemDetailsService sorunlar)
    {
        // Oturum gerektirmeyen uçlar (giriş, yenileme, takip sayfası) eski bir token gönderilse de çalışır.
        var oturumGerekli = baglam.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null;

        if (oturumGerekli && baglam.User.Identity?.IsAuthenticated == true)
        {
            var (durum, mesaj) = await Dogrula(baglam.User, oturumlar, gecerli, baglam.RequestAborted);
            if (mesaj is not null)
            {
                baglam.Response.StatusCode = durum;
                await sorunlar.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = baglam,
                    ProblemDetails = new ProblemDetails { Status = durum, Detail = mesaj }
                });
                return;
            }
        }

        await _sonraki(baglam);
    }

    // Token geçerliyse (0, null), değilse yanıtın durum kodunu ve mesajını döner.
    private static async Task<(int Durum, string? Mesaj)> Dogrula(ClaimsPrincipal kullanici, Oturumlar oturumlar,
        GecerliIsletme gecerli, CancellationToken iptal)
    {
        var kimlik = kullanici.FindFirstValue(TokenServisi.KimlikTalebi);
        var isletmeId = kullanici.FindFirstValue(TokenServisi.IsletmeTalebi);

        HesapDurumu? hesap = null;
        if (isletmeId is null)
        {
            if (ObjectId.TryParse(kimlik, out _))
                hesap = await oturumlar.YoneticiDurumu(kimlik!, iptal);
        }
        else
        {
            var isletme = ObjectId.TryParse(isletmeId, out _) ? await oturumlar.IsletmeyiGetir(isletmeId, iptal) : null;
            if (isletme is not { AktifMi: true })
                return (StatusCodes.Status403Forbidden, "İşletme hesabı askıya alınmış. Lütfen vulu ile iletişime geçin.");

            gecerli.Isletme = isletme;
            if (ObjectId.TryParse(kimlik, out _))
                hesap = await oturumlar.PersonelDurumu(isletme, kimlik!, iptal);
        }

        var gecerliToken = hesap is { AktifMi: true }
            && hesap.Rol == kullanici.FindFirstValue(TokenServisi.RolTalebi)
            && hesap.Damga == (kullanici.FindFirstValue(TokenServisi.DamgaTalebi) ?? "");

        return gecerliToken ? (0, null) : (StatusCodes.Status401Unauthorized, "Oturum geçersiz, yeniden giriş yapın.");
    }
}
