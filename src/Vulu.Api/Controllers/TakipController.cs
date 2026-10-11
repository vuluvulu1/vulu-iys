using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

// Müşterinin cihaz durumunu sorguladığı, oturum gerektirmeyen web sayfası. Kabul formundaki takip koduyla
// çalışır. İnternete açık olduğu için yalnızca cihazın adını ve durumunu gösterir: müşterinin adı, telefonu
// ya da adresi sayfada yer almaz ve deneme sayısı sınırlıdır.
[ApiController]
[AllowAnonymous]
[Route("takip")]
[EnableRateLimiting(SinirPolitikasi)]
[ApiExplorerSettings(IgnoreApi = true)]
public class TakipController : ControllerBase
{
    public const string SinirPolitikasi = "takip";

    private readonly PlatformBaglami _platform;

    public TakipController(PlatformBaglami platform) => _platform = platform;

    [HttpGet("{isletmeKodu}")]
    public async Task<ContentResult> Goster(string isletmeKodu, [FromQuery] string? kod, CancellationToken iptal)
    {
        // Sayfa kimlik bilgisi taşımaz ama yine de başka sitelere gömülmesin ve önbelleğe alınmasın.
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        var aranan = isletmeKodu.ToLowerInvariant();
        var isletme = await _platform.Isletmeler.Find(i => i.Kod == aranan && i.AktifMi).FirstOrDefaultAsync(iptal);
        if (isletme is null)
            return Sayfa(StatusCodes.Status404NotFound, "Cihaz durumu", "<p class=\"uyari\">Sayfa bulunamadı.</p>");

        var takipKodu = (kod ?? "").Trim().ToUpperInvariant();
        var govde = new StringBuilder();
        var durumKodu = StatusCodes.Status200OK;

        if (takipKodu.Length > 0)
        {
            // Koda benzemeyen girdiler veritabanına sorulmaz.
            var kayit = takipKodu.Length <= 20 && takipKodu.All(char.IsAsciiLetterOrDigit)
                ? await _platform.IsletmeBaglami(isletme).ServisKayitlari
                    .Find(s => s.TakipKodu == takipKodu && s.SilinmeTarihi == null).FirstOrDefaultAsync(iptal)
                : null;

            if (kayit is null)
            {
                durumKodu = StatusCodes.Status404NotFound;
                govde.Append("<p class=\"uyari\">Bu takip koduyla bir kayıt bulunamadı. Kodu kabul formunuzdan kontrol edin.</p>");
            }
            else
            {
                govde.Append(KayitOzeti(kayit));
            }
        }

        govde.Append(CultureInfo.InvariantCulture, $"""
            <form method="get">
              <label for="kod">Takip kodu</label>
              <input id="kod" name="kod" value="{Kodla(takipKodu)}" maxlength="20" autocomplete="off" required>
              <button type="submit">Sorgula</button>
            </form>
            """);

        var iletisim = string.Join(" · ", new[] { isletme.Telefon, isletme.Adres }.Where(b => !string.IsNullOrWhiteSpace(b)));
        if (iletisim.Length > 0)
            govde.Append(CultureInfo.InvariantCulture, $"<p class=\"iletisim\">{Kodla(iletisim)}</p>");

        return Sayfa(durumKodu, isletme.Ad, govde.ToString());
    }

    private static string KayitOzeti(ServisKaydi kayit)
    {
        var aciklama = kayit.Durum switch
        {
            ServisDurumu.Beklemede => "Cihazınız teslim alındı, sırasını bekliyor.",
            ServisDurumu.OnayBekliyor => kayit.Teklif is { } teklif
                ? $"Tahmini ücret {Zaman.Para(teklif.Tutar)} (KDV dahil). İşleme başlamak için onayınızı bekliyoruz; lütfen bizi arayın."
                : "İşleme başlamak için onayınızı bekliyoruz; lütfen bizi arayın.",
            ServisDurumu.DevamEdiyor => "Cihazınızın onarımı sürüyor.",
            ServisDurumu.Tamamlandi => "İşlem tamamlandı. Cihazınızı teslim alabilirsiniz.",
            ServisDurumu.TeslimEdildi => "Cihazınız teslim edildi.",
            _ => "İşlem yapılmadı. Cihazınızı teslim alabilirsiniz."
        };

        return $"""
            <section class="kayit">
              <p class="durum">{Kodla(kayit.Durum.Ad())}</p>
              <p>{Kodla(aciklama)}</p>
              <dl>
                <dt>Cihaz</dt><dd>{Kodla(kayit.CihazAdi)}</dd>
                <dt>Kayıt no</dt><dd>{Kodla(kayit.No)}</dd>
                <dt>Kabul tarihi</dt><dd>{Kodla(Zaman.Tarih(kayit.OlusturmaTarihi))}</dd>
              </dl>
            </section>
            """;
    }

    private static ContentResult Sayfa(int durumKodu, string baslik, string govde) => new()
    {
        StatusCode = durumKodu,
        ContentType = "text/html; charset=utf-8",
        Content = $$"""
            <!doctype html>
            <html lang="tr">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>{{Kodla(baslik)}} – cihaz durumu</title>
            <style>
              body { margin: 0; padding: 24px 16px; background: #1a110a; color: #f3e6d6; font: 16px/1.5 system-ui, sans-serif; }
              main { max-width: 440px; margin: 0 auto; }
              h1 { font-size: 22px; margin: 0 0 4px; }
              .alt { color: #b89a7c; margin: 0 0 20px; }
              .kayit { background: #2b1a10; border: 1px solid #4a2e1c; border-radius: 3px; padding: 16px; margin-bottom: 20px; }
              .durum { font-size: 20px; font-weight: 600; color: #e9c699; margin: 0 0 4px; }
              dl { display: grid; grid-template-columns: 110px 1fr; gap: 6px 12px; margin: 14px 0 0; }
              dt { color: #b89a7c; } dd { margin: 0; overflow-wrap: anywhere; }
              label { display: block; color: #b89a7c; margin-bottom: 6px; }
              input { width: 100%; box-sizing: border-box; padding: 10px; font: inherit; letter-spacing: 0.08em; text-transform: uppercase;
                      background: #1a110a; color: inherit; border: 1px solid #4a2e1c; border-radius: 3px; }
              button { margin-top: 10px; width: 100%; padding: 10px; font: inherit; font-weight: 600; cursor: pointer;
                       background: #b5804f; color: #1a110a; border: 0; border-radius: 3px; }
              .uyari { color: #e5604d; }
              .iletisim { color: #b89a7c; font-size: 14px; margin-top: 24px; }
            </style>
            </head>
            <body><main>
            <h1>{{Kodla(baslik)}}</h1>
            <p class="alt">Cihaz durumu sorgulama</p>
            {{govde}}
            </main></body>
            </html>
            """
    };

    // Sayfaya yazılan her değer (işletme adı, cihaz adı, kullanıcının yazdığı kod) HTML olarak kodlanır.
    private static string Kodla(string metin) => HtmlEncoder.Default.Encode(metin);
}

public static class TakipSayfasi
{
    // Müşteriye verilecek adres. Takip:Adres ayarlıysa o kullanılır (API farklı bir alan adının arkasındaysa);
    // değilse isteğin geldiği adresten türetilir. Canlıda bağlantı her zaman https'tir.
    public static string Adres(HttpRequest istek, IConfiguration ayarlar, Isletme isletme, string? takipKodu = null)
    {
        var kok = ayarlar["Takip:Adres"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(kok))
        {
            var yerel = istek.Host.Host is "localhost" or "127.0.0.1" or "[::1]";
            kok = $"{(yerel ? istek.Scheme : "https")}://{istek.Host}";
        }

        var adres = $"{kok}/takip/{isletme.Kod}";
        return takipKodu is null ? adres : $"{adres}?kod={takipKodu}";
    }
}
