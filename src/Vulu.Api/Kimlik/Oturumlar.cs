using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Kimlik;

// Bir hesabın, erişim tokenıyla karşılaştırılan güncel durumu.
public record HesapDurumu(bool AktifMi, string Rol, string Damga);

// Açık oturumların geçerliliği: işletme ve hesap durumunun kısa süreli önbelleği ile oturum kapatma.
//
// İşletme ve hesap durumu her istekte veritabanından okunmasın diye kısa süre bellekte tutulur. Durumu
// değiştiren her işlem ilgili kaydı buradan çıkarır, böylece değişiklik aynı API örneğinde hemen etkili olur;
// birden fazla örnek çalışıyorsa diğerleri en geç OnbellekSuresi sonra görür.
public class Oturumlar
{
    private static readonly TimeSpan OnbellekSuresi = TimeSpan.FromSeconds(30);

    private readonly PlatformBaglami _platform;
    private readonly IMemoryCache _onbellek;

    public Oturumlar(PlatformBaglami platform, IMemoryCache onbellek)
    {
        _platform = platform;
        _onbellek = onbellek;
    }

    public Task<Isletme?> IsletmeyiGetir(string isletmeId, CancellationToken iptal) =>
        Getir(IsletmeAnahtari(isletmeId),
            () => _platform.Isletmeler.Find(i => i.Id == isletmeId).FirstOrDefaultAsync(iptal)!);

    public Task<HesapDurumu?> PersonelDurumu(Isletme isletme, string kullaniciId, CancellationToken iptal) =>
        Getir(HesapAnahtari(isletme.Id, kullaniciId), async () =>
        {
            var hesap = await _platform.IsletmeBaglami(isletme).Kullanicilar.Find(k => k.Id == kullaniciId).FirstOrDefaultAsync(iptal);
            return hesap is null ? null : new HesapDurumu(hesap.AktifMi, hesap.Rol.ToString(), hesap.OturumDamgasi);
        });

    public Task<HesapDurumu?> YoneticiDurumu(string yoneticiId, CancellationToken iptal) =>
        Getir(HesapAnahtari(null, yoneticiId), async () =>
        {
            var hesap = await _platform.Yoneticiler.Find(y => y.Id == yoneticiId).FirstOrDefaultAsync(iptal);
            return hesap is null ? null : new HesapDurumu(hesap.AktifMi, TokenServisi.PlatformRolu, hesap.OturumDamgasi);
        });

    // İşletme kaydı değişti (profil, askı durumu, silme): bir sonraki istek güncel halini okur.
    public void IsletmeDegisti(string isletmeId) => _onbellek.Remove(IsletmeAnahtari(isletmeId));

    // Hesap değişti ama oturumları açık kalabilir (örn. yalnızca adı değişti).
    public void HesapDegisti(string? isletmeId, string kullaniciId) => _onbellek.Remove(HesapAnahtari(isletmeId, kullaniciId));

    // Hesabın yenileme tokenlarını siler. Açık erişim tokenlarının da reddedilmesi için çağıran, hesabın oturum
    // damgasını değiştirmiş ya da hesabı devre dışı bırakmış / silmiş olmalıdır. isletmeId boşsa hesap bir vulu
    // yöneticisidir.
    public async Task KapatAsync(string? isletmeId, string kullaniciId, CancellationToken iptal = default)
    {
        HesapDegisti(isletmeId, kullaniciId);
        await _platform.YenilemeTokenlari.DeleteManyAsync(
            t => t.KullaniciId == kullaniciId && t.IsletmeId == isletmeId, iptal);
    }

    // İşletmenin bütün personelinin yenileme tokenlarını siler (askıya alma, silme).
    public async Task IsletmeninkileriKapatAsync(string isletmeId, CancellationToken iptal = default)
    {
        IsletmeDegisti(isletmeId);
        await _platform.YenilemeTokenlari.DeleteManyAsync(t => t.IsletmeId == isletmeId, iptal);
    }

    private Task<T?> Getir<T>(string anahtar, Func<Task<T?>> oku) where T : class =>
        _onbellek.GetOrCreateAsync(anahtar, kayit =>
        {
            kayit.AbsoluteExpirationRelativeToNow = OnbellekSuresi;
            return oku();
        });

    private static string IsletmeAnahtari(string isletmeId) => "isletme:" + isletmeId;

    private static string HesapAnahtari(string? isletmeId, string kullaniciId) => $"hesap:{isletmeId ?? "vulu"}:{kullaniciId}";
}
