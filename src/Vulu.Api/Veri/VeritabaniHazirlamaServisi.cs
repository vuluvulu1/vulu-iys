using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Veri;

// Açılışta platform veritabanını ve her işletmenin veritabanını kullanıma hazırlar. Veritabanı o an ulaşılamaz
// olabilir; API yine de ayağa kalkar ve hazırlık bağlantı gelince yapılır.
public class VeritabaniHazirlamaServisi : BackgroundService
{
    private static readonly TimeSpan DenemeZamanAsimi = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DenemeAraligi = TimeSpan.FromSeconds(15);

    private readonly PlatformBaglami _platform;
    private readonly IlkAdminAyarlari _ilkYonetici;
    private readonly ILogger<VeritabaniHazirlamaServisi> _log;

    public VeritabaniHazirlamaServisi(PlatformBaglami platform, IOptions<IlkAdminAyarlari> ilkYonetici,
        ILogger<VeritabaniHazirlamaServisi> log)
    {
        _platform = platform;
        _ilkYonetici = ilkYonetici.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken durdurma)
    {
        while (!durdurma.IsCancellationRequested)
        {
            using var sure = CancellationTokenSource.CreateLinkedTokenSource(durdurma);
            sure.CancelAfter(DenemeZamanAsimi);

            try
            {
                await _platform.IndexleriKurAsync(sure.Token);
                await IlkYoneticiyiOlusturAsync(sure.Token);

                // Her işletme kendi veritabanında bekleyen veri geçişlerini ve indexlerini alır.
                var isletmeler = await _platform.Isletmeler.Find(FilterDefinition<Isletme>.Empty).ToListAsync(sure.Token);
                foreach (var isletme in isletmeler)
                    await _platform.IsletmeyiHazirlaAsync(isletme, _log, sure.Token);

                _log.LogInformation("Veritabanı hazır: {Sayi} işletme.", isletmeler.Count);
                return;
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException or OperationCanceledException)
            {
                if (durdurma.IsCancellationRequested) return;
                _log.LogWarning("Veritabanı hazırlanamadı, {Saniye} sn sonra yeniden denenecek: {Hata}",
                    DenemeAraligi.TotalSeconds, ex.Message);
            }

            try { await Task.Delay(DenemeAraligi, durdurma); }
            catch (OperationCanceledException) { return; }
        }
    }

    // Hiç vulu yöneticisi yoksa ayarlardaki hesapla ilkini oluşturur; işletmeleri o açar.
    private async Task IlkYoneticiyiOlusturAsync(CancellationToken iptal)
    {
        if (await _platform.Yoneticiler.Find(FilterDefinition<PlatformYoneticisi>.Empty).AnyAsync(iptal))
            return;

        if (string.IsNullOrWhiteSpace(_ilkYonetici.KullaniciAdi)
            || (_ilkYonetici.Sifre?.Length ?? 0) < KullaniciKurallari.EnAzSifreUzunlugu)
        {
            _log.LogWarning("Vulu yöneticisi yok ve IlkAdmin ayarları eksik (şifre en az {Uzunluk} karakter olmalı); kimse işletme açamaz.",
                KullaniciKurallari.EnAzSifreUzunlugu);
            return;
        }

        try
        {
            await _platform.Yoneticiler.InsertOneAsync(new PlatformYoneticisi
            {
                KullaniciAdi = KullaniciAdlari.Normalle(_ilkYonetici.KullaniciAdi),
                SifreHash = Sifreler.Hashle(_ilkYonetici.Sifre!),
                OturumDamgasi = Sifreler.YeniOturumDamgasi(),
                AdSoyad = "Vulu Yöneticisi"
            }, cancellationToken: iptal);
            _log.LogInformation("İlk vulu yöneticisi oluşturuldu: {KullaniciAdi}", _ilkYonetici.KullaniciAdi);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Aynı anda açılan başka bir API örneği hesabı zaten oluşturmuş.
        }
    }
}
