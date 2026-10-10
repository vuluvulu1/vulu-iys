using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Veri;

// Veritabanı açılışta ulaşılamaz olabilir; API yine de ayağa kalkar, indexler ve ilk yönetici bağlantı gelince kurulur.
public class VeritabaniHazirlamaServisi : BackgroundService
{
    private static readonly TimeSpan DenemeZamanAsimi = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DenemeAraligi = TimeSpan.FromSeconds(15);

    private readonly MongoBaglami _baglam;
    private readonly IlkAdminAyarlari _ilkAdmin;
    private readonly ILogger<VeritabaniHazirlamaServisi> _log;

    public VeritabaniHazirlamaServisi(MongoBaglami baglam, IOptions<IlkAdminAyarlari> ilkAdmin,
        ILogger<VeritabaniHazirlamaServisi> log)
    {
        _baglam = baglam;
        _ilkAdmin = ilkAdmin.Value;
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
                await _baglam.IndexleriKurAsync(sure.Token);
                _log.LogInformation("MongoDB indexleri hazır.");
                await IlkAdminiOlusturAsync(sure.Token);
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

    private async Task IlkAdminiOlusturAsync(CancellationToken iptal)
    {
        if (await _baglam.Kullanicilar.Find(FilterDefinition<Kullanici>.Empty).AnyAsync(iptal))
            return;

        if (string.IsNullOrWhiteSpace(_ilkAdmin.KullaniciAdi)
            || (_ilkAdmin.Sifre?.Length ?? 0) < KullaniciKurallari.EnAzSifreUzunlugu)
        {
            _log.LogWarning("Veritabanında kullanıcı yok ve IlkAdmin ayarları eksik (şifre en az {Uzunluk} karakter olmalı); kimse giriş yapamaz.",
                KullaniciKurallari.EnAzSifreUzunlugu);
            return;
        }

        try
        {
            await _baglam.Kullanicilar.InsertOneAsync(new Kullanici
            {
                KullaniciAdi = KullaniciAdlari.Normalle(_ilkAdmin.KullaniciAdi),
                SifreHash = BCrypt.Net.BCrypt.HashPassword(_ilkAdmin.Sifre),
                Rol = KullaniciRol.Admin
            }, cancellationToken: iptal);
            _log.LogInformation("İlk yönetici hesabı oluşturuldu: {KullaniciAdi}", _ilkAdmin.KullaniciAdi);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Aynı anda açılan başka bir API örneği hesabı zaten oluşturmuş.
        }
    }
}
