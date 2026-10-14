using System.Net.Http.Json;
using Vulu.Shared;

namespace Vulu.Desktop.Servisler;

// Açık oturumu bellekte tutar; uygulama kapanınca oturum da biter.
public sealed class OturumServisi : IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _yenilemeKilidi = new(1, 1);

    public OturumServisi(HttpClient http) => _http = http;

    public OturumYaniti? Oturum { get; private set; }
    public KullaniciOzeti? Kullanici => Oturum?.Kullanici;
    public bool GirisYapildi => Oturum is not null;

    // Vulu yöneticisinin oturumu bir işletmeye bağlı değildir; servis ekranları yerine işletme yönetimini görür.
    public bool VuluYoneticisiMi => Oturum is { Isletme: null };

    // İşletmenin yöneticisi.
    public bool AdminMi => Oturum is { Isletme: not null, Kullanici.Rol: KullaniciRol.Admin };

    // Stok kartlarını ve stok girişlerini yönetebilir: yöneticiler ve stok yetkisi verilmiş personel.
    public bool StokYonetebilir => AdminMi || Oturum is { Isletme: not null, Kullanici.StokYetkisi: true };

    // İşletmenin adı, logosu ve iletişim bilgileri; girişten sonra yüklenir, ayarlar değişince yenilenir.
    public IsletmeYaniti? Isletme { get; private set; }

    // Başarılıysa null, değilse kullanıcıya gösterilecek hata mesajını döner.
    public async Task<string?> GirisYap(string isletmeKodu, string kullaniciAdi, string sifre)
    {
        try
        {
            using var yanit = await _http.PostAsJsonAsync("api/kimlik/giris",
                new GirisIstegi(isletmeKodu.Trim(), kullaniciAdi, sifre));
            if (!yanit.IsSuccessStatusCode)
                return await ApiHatasi.Oku(yanit);

            Ayarla(await yanit.Content.ReadFromJsonAsync<OturumYaniti>());
            return Oturum is null ? "Sunucudan geçersiz yanıt alındı." : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiHatasi.SunucuyaUlasilamadi;
        }
    }

    public async Task CikisYap()
    {
        var oturum = Oturum;
        Ayarla(null);
        if (oturum is null) return;

        try
        {
            using var yanit = await _http.PostAsJsonAsync("api/kimlik/cikis", new YenilemeIstegi(oturum.YenilemeTokeni));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Sunucuya ulaşılamasa da yerel oturum kapanmış olur; token süresi dolunca geçersizleşir.
        }
    }

    // kullanilanToken: çağıranın elindeki erişim tokenı. Bu arada başka bir istek oturumu yenilediyse tekrar yenilenmez.
    public async Task<bool> Yenile(string kullanilanToken)
    {
        await _yenilemeKilidi.WaitAsync();
        try
        {
            if (Oturum is null) return false;
            if (Oturum.ErisimTokeni != kullanilanToken) return true;

            using var yanit = await _http.PostAsJsonAsync("api/kimlik/yenile", new YenilemeIstegi(Oturum.YenilemeTokeni));
            if (!yanit.IsSuccessStatusCode)
            {
                Ayarla(null);
                return false;
            }

            var yeni = await yanit.Content.ReadFromJsonAsync<OturumYaniti>();
            if (yeni is null)
            {
                Ayarla(null);
                return false;
            }

            OturumuYenile(yeni);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Bağlantı sorunu oturumu düşürmez; istek hata verir, kullanıcı yeniden dener.
            return false;
        }
        finally
        {
            _yenilemeKilidi.Release();
        }
    }

    public void Kapat() => Ayarla(null);

    public void Dispose() => _yenilemeKilidi.Dispose();

    // Sunucu açık oturumun yerine yenisini verdi (şifre değişikliği); yüklenmiş işletme bilgisi korunur.
    public void OturumuYenile(OturumYaniti yeni)
    {
        Oturum = yeni;
    }

    public void IsletmeyiAyarla(IsletmeYaniti isletme)
    {
        Isletme = isletme;
    }

    private void Ayarla(OturumYaniti? oturum)
    {
        Oturum = oturum;
        Isletme = null;
    }
}
