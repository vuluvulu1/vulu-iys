using Microsoft.AspNetCore.Mvc;

namespace Vulu.Api.Altyapi;

// Her yanıta eklenen koruyucu başlıklar ve istek gövdesi sınırı.
public class GuvenlikOnlemleri
{
    // En büyük meşru istek, base64 kodlu logoyu taşıyan işletme profilidir (~2,7 MB).
    public const long EnFazlaGovdeBayt = 4 * 1024 * 1024;

    private readonly RequestDelegate _sonraki;

    public GuvenlikOnlemleri(RequestDelegate sonraki) => _sonraki = sonraki;

    public async Task InvokeAsync(HttpContext baglam, IProblemDetailsService sorunlar)
    {
        baglam.Response.OnStarting(() =>
        {
            var basliklar = baglam.Response.Headers;

            // Yanıtlar kişisel veri ve token taşır; tarayıcı ya da aradaki vekiller saklamasın.
            if (!basliklar.ContainsKey("Cache-Control")) basliklar.CacheControl = "no-store";
            basliklar.XContentTypeOptions = "nosniff";
            basliklar.XFrameOptions = "DENY";
            basliklar["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });

        // Sunucunun kendi sınırı (bkz. Program.cs) gövde okunurken devreye girer; uzunluğunu bildiren büyük
        // istekler burada, hiç okunmadan reddedilir.
        if (baglam.Request.ContentLength > EnFazlaGovdeBayt)
        {
            baglam.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await sorunlar.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = baglam,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status413PayloadTooLarge,
                    Detail = "İstek çok büyük."
                }
            });
            return;
        }

        await _sonraki(baglam);
    }
}
