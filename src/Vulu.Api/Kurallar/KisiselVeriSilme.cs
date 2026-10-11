using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Kurallar;

// Bir müşterinin kişisel verisinin geri dönüşsüz silinmesi (KVKK: silme / yok etme / anonim hale getirme).
//
// Müşteri kaydı silinir. Servis kayıtları ise silinmez, anonimleştirilir: işletmenin iş ve gelir geçmişi
// (raporlar, stok hareketleri) bozulmaz, ama kayıtlarda müşteriyi tanıtan hiçbir şey kalmaz.
public static class KisiselVeriSilme
{
    public const string SilinenMusteriAdi = "Silinen müşteri";

    // Silinebiliyorsa null, değilse kullanıcıya gösterilecek gerekçeyi döner.
    public static async Task<string?> EngeliBul(MongoBaglami db, string musteriId, int saklamaYili, DateTime simdi,
        CancellationToken iptal)
    {
        var acikIs = await db.ServisKayitlari
            .Find(s => s.MusteriId == musteriId && s.SilinmeTarihi == null && ServisKaydiKurallari.AcikDurumlar.Contains(s.Durum))
            .AnyAsync(iptal);
        if (acikIs) return "Müşterinin devam eden servis kaydı var; önce kaydı tamamlayın veya iptal edin.";

        var planliRandevu = await db.Randevular
            .Find(r => r.MusteriId == musteriId && r.Durum == RandevuDurumu.Planlandi).AnyAsync(iptal);
        if (planliRandevu) return "Müşterinin planlanmış randevusu var; önce randevuyu tamamlayın veya iptal edin.";

        // Fatura kesilmiş işlerin kaydı, yasal saklama süresi boyunca alıcının bilgisiyle birlikte tutulmalıdır.
        var esik = simdi.AddYears(-saklamaYili);
        var sonFatura = await db.ServisKayitlari
            .Find(s => s.MusteriId == musteriId && s.Fatura!.OlusturmaTarihi > esik)
            .SortByDescending(s => s.Fatura!.OlusturmaTarihi)
            .Project(s => s.Fatura!.OlusturmaTarihi)
            .FirstOrDefaultAsync(iptal);

        return sonFatura == default
            ? null
            : $"Müşteriye {Zaman.Tarih(sonFatura)} tarihinde fatura kesilmiş. Faturalı kayıtlar {saklamaYili} yıl saklanmak "
              + $"zorunda olduğu için kişisel veriler {Zaman.Tarih(sonFatura.AddYears(saklamaYili))} tarihinden önce silinemez.";
    }

    // Müşteriyi tanıtan her şeyi siler. Yarıda kesilirse yeniden çalıştırılabilir: her adım, zaten yapılmışsa etkisizdir.
    public static async Task SilAsync(MongoBaglami db, string musteriId, CancellationToken iptal)
    {
        var kayitlar = await db.ServisKayitlari.Find(s => s.MusteriId == musteriId).ToListAsync(iptal);
        var kayitIdleri = kayitlar.Select(k => k.Id).ToList();

        // Kayıtlardaki müşteri kopyası ve cihazın seri numarası / IMEI'si silinir; arama metni seri numarası
        // olmadan yeniden kurulur. Sürüm artar ki o sırada kaydı düzenleyen biri eski bilgileri geri yazamasın.
        foreach (var kayit in kayitlar)
        {
            await db.ServisKayitlari.UpdateOneAsync(s => s.Id == kayit.Id,
                Builders<ServisKaydi>.Update
                    .Set(s => s.Musteri, new MusteriOzeti { AdSoyad = SilinenMusteriAdi })
                    .Unset(s => s.SeriNo)
                    .Set(s => s.AramaMetni, AramaMetni.Olustur(kayit.No, kayit.CihazBilgisi, kayit.Marka, kayit.Model, kayit.Sorun))
                    .Inc(s => s.Surum, 1),
                cancellationToken: iptal);
        }

        // İşlem geçmişi serbest metin içerir (notlar, "Müşteri: A → B" satırları); kayıt başına tek satır bırakılır.
        if (kayitIdleri.Count > 0)
        {
            await db.IslemGecmisi.DeleteManyAsync(i => kayitIdleri.Contains(i.ServisKaydiId), iptal);
            await db.IslemGecmisi.InsertManyAsync(kayitIdleri.Select(id => new IslemKaydi
            {
                ServisKaydiId = id,
                KullaniciAdi = "Sistem",
                Tur = IslemTuru.KayitGuncellendi,
                Aciklama = "Müşterinin kişisel verileri silindi; önceki işlem geçmişi kaldırıldı."
            }), cancellationToken: iptal);
        }

        // Randevuların iş ya da gelir değeri yoktur; tamamen silinir.
        await db.Randevular.DeleteManyAsync(r => r.MusteriId == musteriId, iptal);
        await db.Musteriler.DeleteOneAsync(m => m.Id == musteriId, iptal);
    }
}
