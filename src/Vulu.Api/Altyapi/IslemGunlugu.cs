using System.Security.Claims;
using MongoDB.Driver;
using Vulu.Api.Kimlik;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Altyapi;

// İsteği yapan personel adına işlem geçmişi ve stok hareketi yazar.
//
// MongoDB tek sunucuda çok belgeli işlem (transaction) sunmadığı için geçmiş, asıl yazma başarılı olduktan
// sonra ayrı bir yazmayla eklenir. Arada sunucu çökerse değişiklik kalır, geçmiş satırı eksik olur; tersi
// (yapılmamış bir işlemin geçmişte görünmesi) olmaz.
public class IslemGunlugu
{
    private readonly MongoBaglami _db;
    private readonly IHttpContextAccessor _erisim;
    private (string? Id, string Ad)? _yapan;

    public IslemGunlugu(MongoBaglami db, IHttpContextAccessor erisim)
    {
        _db = db;
        _erisim = erisim;
    }

    public async Task YazAsync(string servisKaydiId, IslemTuru tur, string aciklama, CancellationToken iptal = default)
    {
        var (id, ad) = await YapanAsync(iptal);
        await _db.IslemGecmisi.InsertOneAsync(new IslemKaydi
        {
            ServisKaydiId = servisKaydiId,
            KullaniciId = id,
            KullaniciAdi = ad,
            Tur = tur,
            Aciklama = aciklama
        }, cancellationToken: iptal);
    }

    public async Task StokHareketiYazAsync(string parcaId, int adet, string? aciklama, ServisKaydi? kayit = null,
        CancellationToken iptal = default)
    {
        var (id, ad) = await YapanAsync(iptal);
        await _db.StokHareketleri.InsertOneAsync(new StokHareketi
        {
            ParcaId = parcaId,
            Adet = adet,
            Aciklama = aciklama,
            KullaniciId = id,
            KullaniciAdi = ad,
            ServisKaydiId = kayit?.Id,
            ServisKaydiNo = kayit?.No
        }, cancellationToken: iptal);
    }

    // Tokendaki ad kullanıcı adıdır; geçmişte personelin adı soyadı görünsün diye hesaptan okunur.
    private async Task<(string? Id, string Ad)> YapanAsync(CancellationToken iptal)
    {
        if (_yapan is { } bilinen) return bilinen;

        var kullanici = _erisim.HttpContext?.User;
        var id = kullanici?.FindFirstValue(TokenServisi.KimlikTalebi);
        var ad = id is null
            ? null
            : await _db.Kullanicilar.Find(k => k.Id == id).Project(k => k.AdSoyad).FirstOrDefaultAsync(iptal);

        _yapan = (id, ad ?? kullanici?.Identity?.Name ?? "Bilinmiyor");
        return _yapan.Value;
    }
}
