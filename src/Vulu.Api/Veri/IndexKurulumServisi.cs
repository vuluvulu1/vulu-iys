using MongoDB.Driver;

namespace Vulu.Api.Veri;

// Veritabanı açılışta ulaşılamaz olabilir; API yine de ayağa kalkar ve indexler bağlantı gelince kurulur.
public class IndexKurulumServisi : BackgroundService
{
    private static readonly TimeSpan DenemeZamanAsimi = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DenemeAraligi = TimeSpan.FromSeconds(15);

    private readonly MongoBaglami _baglam;
    private readonly ILogger<IndexKurulumServisi> _log;

    public IndexKurulumServisi(MongoBaglami baglam, ILogger<IndexKurulumServisi> log)
    {
        _baglam = baglam;
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
                return;
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException or OperationCanceledException)
            {
                if (durdurma.IsCancellationRequested) return;
                _log.LogWarning("MongoDB indexleri kurulamadı, {Saniye} sn sonra yeniden denenecek: {Hata}",
                    DenemeAraligi.TotalSeconds, ex.Message);
            }

            try { await Task.Delay(DenemeAraligi, durdurma); }
            catch (OperationCanceledException) { return; }
        }
    }
}
