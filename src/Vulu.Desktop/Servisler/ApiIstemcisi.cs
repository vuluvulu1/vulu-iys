using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Vulu.Desktop.Servisler;

// Bir API isteğinin sonucu. Başarılıysa Veri (JSON) ya da Dosya (PDF, Excel) doludur, değilse Hata.
public class ApiSonucu
{
    public bool Basarili { get; set; }
    public JsonElement Veri { get; set; }
    public byte[]? Dosya { get; set; }
    public string? Hata { get; set; }

    public static ApiSonucu Basarisiz(string hata) => new() { Basarili = false, Hata = hata };
}

// Oturum gerektiren tüm API çağrıları buradan geçer: tokenı ekler, süresi dolduysa yeniler.
public class ApiIstemcisi
{
    private static readonly TimeSpan YenilemePayi = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly OturumServisi _oturum;

    public ApiIstemcisi(HttpClient http, OturumServisi oturum)
    {
        _http = http;
        _oturum = oturum;
    }

    public Task<ApiSonucu> Getir(string yol) => Calistir(HttpMethod.Get, yol, null, dosyaMi: false);
    public Task<ApiSonucu> Ekle(string yol, object govde) => Calistir(HttpMethod.Post, yol, govde, dosyaMi: false);
    public Task<ApiSonucu> Guncelle(string yol, object govde) => Calistir(HttpMethod.Put, yol, govde, dosyaMi: false);
    public Task<ApiSonucu> Sil(string yol) => Calistir(HttpMethod.Delete, yol, null, dosyaMi: false);

    // Dosya yanıtları (PDF, Excel, logo) için: gövde Dosya alanına olduğu gibi konur.
    public Task<ApiSonucu> Indir(string yol) => Calistir(HttpMethod.Get, yol, null, dosyaMi: true);

    private async Task<ApiSonucu> Calistir(HttpMethod yontem, string yol, object? govde, bool dosyaMi)
    {
        try
        {
            using var yanit = await Yolla(yontem, yol, govde);

            if (!yanit.IsSuccessStatusCode)
                return ApiSonucu.Basarisiz(await ApiHatasi.Oku(yanit));

            var sonuc = new ApiSonucu { Basarili = true };
            if (dosyaMi)
            {
                sonuc.Dosya = await yanit.Content.ReadAsByteArrayAsync();
            }
            else if (yanit.StatusCode != HttpStatusCode.NoContent && yanit.Content.Headers.ContentLength != 0)
            {
                // Gövde JSON olarak okunur; hangi alanların kullanılacağına çağıran karar verir.
                using var belge = JsonDocument.Parse(await yanit.Content.ReadAsStringAsync());
                sonuc.Veri = belge.RootElement.Clone();
            }
            return sonuc;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiSonucu.Basarisiz(ApiHatasi.SunucuyaUlasilamadi);
        }
        catch (JsonException)
        {
            return ApiSonucu.Basarisiz("Sunucudan geçersiz yanıt alındı.");
        }
    }

    private async Task<HttpResponseMessage> Yolla(HttpMethod yontem, string yol, object? govde)
    {
        if (_oturum.Oturum is { } oturum && oturum.ErisimBitis <= DateTime.UtcNow + YenilemePayi)
            await _oturum.Yenile(oturum.ErisimTokeni);

        var token = _oturum.Oturum?.ErisimTokeni;
        var yanit = await TekIstek(yontem, yol, govde, token);

        if (yanit.StatusCode == HttpStatusCode.Unauthorized && token is not null && await _oturum.Yenile(token))
        {
            yanit.Dispose();
            yanit = await TekIstek(yontem, yol, govde, _oturum.Oturum?.ErisimTokeni);
        }

        // Yenileme de işe yaramadıysa oturum bitmiştir; giriş ekranına dönülür.
        if (yanit.StatusCode == HttpStatusCode.Unauthorized)
            _oturum.Kapat();

        return yanit;
    }

    private async Task<HttpResponseMessage> TekIstek(HttpMethod yontem, string yol, object? govde, string? token)
    {
        using var istek = new HttpRequestMessage(yontem, yol);
        if (govde is not null)
            istek.Content = JsonContent.Create(govde, govde.GetType());
        if (token is not null)
            istek.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _http.SendAsync(istek);
    }
}

public static class ApiHatasi
{
    public const string SunucuyaUlasilamadi = "Sunucuya ulaşılamadı. Bağlantıyı kontrol edip yeniden deneyin.";

    // API hataları ProblemDetails biçimindedir: doğrulama hataları "errors", diğerleri "detail" alanında gelir.
    public static async Task<string> Oku(HttpResponseMessage yanit)
    {
        try
        {
            var govde = await yanit.Content.ReadFromJsonAsync<JsonElement>();

            if (govde.TryGetProperty("errors", out var hatalar) && hatalar.ValueKind == JsonValueKind.Object)
            {
                var mesajlar = new List<string>();
                foreach (var alan in hatalar.EnumerateObject())
                {
                    foreach (var mesaj in alan.Value.EnumerateArray())
                    {
                        if (!string.IsNullOrWhiteSpace(mesaj.GetString()))
                            mesajlar.Add(mesaj.GetString()!);
                    }
                }
                if (mesajlar.Count > 0) return string.Join(" ", mesajlar);
            }

            if (govde.TryGetProperty("detail", out var ayrinti) && ayrinti.GetString() is { Length: > 0 } metin)
                return metin;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            // Gövde yok ya da JSON değil; durum koduna göre genel mesaj verilir.
        }

        return yanit.StatusCode switch
        {
            HttpStatusCode.Forbidden => "Bu işlem için yetkiniz yok.",
            HttpStatusCode.NotFound => "Kayıt bulunamadı.",
            HttpStatusCode.TooManyRequests => "Çok fazla deneme yapıldı. Bir dakika sonra yeniden deneyin.",
            HttpStatusCode.Unauthorized => "Oturum geçersiz, yeniden giriş yapın.",
            _ => $"İşlem başarısız oldu ({(int)yanit.StatusCode})."
        };
    }
}
