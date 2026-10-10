using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Vulu.Desktop.Servisler;

public record ApiSonucu<T>(bool Basarili, T? Deger, string? Hata)
{
    public static ApiSonucu<T> Tamam(T? deger) => new(true, deger, null);
    public static ApiSonucu<T> Basarisiz(string hata) => new(false, default, hata);
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

    public Task<ApiSonucu<T>> Getir<T>(string yol) => Calistir<T>(HttpMethod.Get, yol, null);
    public Task<ApiSonucu<T>> Ekle<T>(string yol, object govde) => Calistir<T>(HttpMethod.Post, yol, govde);
    public Task<ApiSonucu<T>> Guncelle<T>(string yol, object govde) => Calistir<T>(HttpMethod.Put, yol, govde);
    public Task<ApiSonucu<object>> Sil(string yol) => Calistir<object>(HttpMethod.Delete, yol, null);
    public Task<ApiSonucu<T>> Sil<T>(string yol) => Calistir<T>(HttpMethod.Delete, yol, null);

    // Dosya yanıtları (örn. fatura PDF'i) için: gövdeyi olduğu gibi döner.
    public async Task<ApiSonucu<byte[]>> Indir(string yol)
    {
        try
        {
            using var yanit = await Yolla(HttpMethod.Get, yol, null);
            return yanit.IsSuccessStatusCode
                ? ApiSonucu<byte[]>.Tamam(await yanit.Content.ReadAsByteArrayAsync())
                : ApiSonucu<byte[]>.Basarisiz(await ApiHatasi.Oku(yanit));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiSonucu<byte[]>.Basarisiz(ApiHatasi.SunucuyaUlasilamadi);
        }
    }

    private async Task<ApiSonucu<T>> Calistir<T>(HttpMethod yontem, string yol, object? govde)
    {
        try
        {
            using var yanit = await Yolla(yontem, yol, govde);

            if (!yanit.IsSuccessStatusCode)
                return ApiSonucu<T>.Basarisiz(await ApiHatasi.Oku(yanit));

            if (yanit.StatusCode == HttpStatusCode.NoContent || yanit.Content.Headers.ContentLength == 0)
                return ApiSonucu<T>.Tamam(default);

            return ApiSonucu<T>.Tamam(await yanit.Content.ReadFromJsonAsync<T>());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiSonucu<T>.Basarisiz(ApiHatasi.SunucuyaUlasilamadi);
        }
        catch (JsonException)
        {
            return ApiSonucu<T>.Basarisiz("Sunucudan geçersiz yanıt alındı.");
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
                var mesajlar = hatalar.EnumerateObject()
                    .SelectMany(alan => alan.Value.EnumerateArray())
                    .Select(m => m.GetString())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();
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
