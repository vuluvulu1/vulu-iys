using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Altyapi;

// İsteğin ait olduğu işletme. Kimlik doğrulamasından sonra IsletmeCozucu tarafından doldurulur.
public class GecerliIsletme
{
    public Isletme? Isletme { get; set; }

    public Isletme Zorunlu => Isletme ?? throw new InvalidOperationException("Bu istek bir işletmeye bağlı değil.");
}

// Oturumdaki işletme kimliğinden işletmeyi bulur ve isteğe bağlar. İşletme askıya alınmışsa ya da silinmişse
// istek burada reddedilir; böylece açık oturumlar erişim tokenının süresi dolmadan da kapanmış olur.
public class IsletmeCozucu
{
    // İşletme kaydı her istekte veritabanından okunmasın diye kısa süre bellekte tutulur.
    private static readonly TimeSpan OnbellekSuresi = TimeSpan.FromSeconds(30);

    private readonly RequestDelegate _sonraki;

    public IsletmeCozucu(RequestDelegate sonraki) => _sonraki = sonraki;

    public async Task InvokeAsync(HttpContext baglam, PlatformBaglami platform, IMemoryCache onbellek,
        GecerliIsletme gecerli, IProblemDetailsService sorunlar)
    {
        var isletmeId = baglam.User.FindFirst(TokenServisi.IsletmeTalebi)?.Value;
        if (isletmeId is not null)
        {
            var isletme = await onbellek.GetOrCreateAsync(OnbellekAnahtari(isletmeId), kayit =>
            {
                kayit.AbsoluteExpirationRelativeToNow = OnbellekSuresi;
                return platform.Isletmeler.Find(i => i.Id == isletmeId).FirstOrDefaultAsync(baglam.RequestAborted)!;
            });

            if (isletme is not { AktifMi: true })
            {
                baglam.Response.StatusCode = StatusCodes.Status403Forbidden;
                await sorunlar.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = baglam,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Detail = "İşletme hesabı askıya alınmış. Lütfen vulu ile iletişime geçin."
                    }
                });
                return;
            }

            gecerli.Isletme = isletme;
        }

        await _sonraki(baglam);
    }

    // İşletme güncellendiğinde değişiklik hemen etkili olsun diye çağrılır.
    public static void OnbellektenCikar(IMemoryCache onbellek, string isletmeId) =>
        onbellek.Remove(OnbellekAnahtari(isletmeId));

    private static string OnbellekAnahtari(string isletmeId) => "isletme:" + isletmeId;
}
