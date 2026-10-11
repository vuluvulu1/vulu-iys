using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Vulu.Api.Kimlik;

public class GirisKilidiAyarlari
{
    public const string Bolum = "GirisKilidi";

    // Bu kadar art arda yanlış denemeden sonra hesap KilitDakika boyunca giriş kabul etmez.
    public int Deneme { get; set; } = 10;
    public int KilitDakika { get; set; } = 10;
}

// Hesap bazında şifre deneme sınırı. Adres bazındaki sınır (bkz. Program.cs) tek bir bilgisayardan gelen
// denemeleri durdurur; bu sınıf denemeler çok sayıda adrese dağıtılsa da tek bir hesabın şifresinin
// denenmesini yavaşlatır.
//
// Bedeli: hesabın adını bilen biri, sahibini kısa süreliğine dışarıda bırakabilir. Bu yüzden kilit kısadır ve
// kendiliğinden açılır. Sayaçlar bellekte tutulur; API yeniden başlayınca sıfırlanır ve birden fazla API
// örneği çalışırsa her örnek kendi sayacını tutar.
public class GirisKilidi
{
    private readonly IMemoryCache _onbellek;
    private readonly GirisKilidiAyarlari _ayarlar;

    public GirisKilidi(IMemoryCache onbellek, IOptions<GirisKilidiAyarlari> ayarlar)
    {
        _onbellek = onbellek;
        _ayarlar = ayarlar.Value;
    }

    private TimeSpan KilitSuresi => TimeSpan.FromMinutes(_ayarlar.KilitDakika);

    private sealed class Sayac
    {
        public int Deneme;
        public DateTime? KilitBitisi;
    }

    // Hesap kilitliyse kalan süreyi, değilse null döner. Hesabın var olup olmadığına bakılmaz: yalnızca var
    // olan hesaplar kilitlenseydi kilit yanıtı hesabın varlığını ele verirdi.
    public TimeSpan? KalanKilit(string isletmeKodu, string kullaniciAdi)
    {
        if (!_onbellek.TryGetValue(Anahtar(isletmeKodu, kullaniciAdi), out Sayac? sayac) || sayac is null)
            return null;

        lock (sayac)
        {
            var kalan = sayac.KilitBitisi - DateTime.UtcNow;
            return kalan > TimeSpan.Zero ? kalan : null;
        }
    }

    // Yanlış denemeyi sayar; sınır dolduysa hesabı kilitler ve true döner.
    public bool YanlisDeneme(string isletmeKodu, string kullaniciAdi)
    {
        var sayac = _onbellek.GetOrCreate(Anahtar(isletmeKodu, kullaniciAdi), kayit =>
        {
            // Son denemeden bu yana kilit süresi kadar sessizlik sayacı sıfırlar.
            kayit.SlidingExpiration = KilitSuresi;
            return new Sayac();
        })!;

        lock (sayac)
        {
            if (++sayac.Deneme < _ayarlar.Deneme) return false;

            sayac.Deneme = 0;
            sayac.KilitBitisi = DateTime.UtcNow + KilitSuresi;
            return true;
        }
    }

    public void Sifirla(string isletmeKodu, string kullaniciAdi) =>
        _onbellek.Remove(Anahtar(isletmeKodu, kullaniciAdi));

    private static string Anahtar(string isletmeKodu, string kullaniciAdi) => $"giris:{isletmeKodu}:{kullaniciAdi}";
}
