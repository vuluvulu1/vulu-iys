using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;

namespace Vulu.Api.Kurallar;

// Servis kaydı ve randevuya personel atama kuralı.
public static class PersonelAtamasi
{
    // istenenId: istekte gelen personel; mevcutId: kayıtta şu an atanmış olan (yeni kayıtta null).
    // Atama yoksa (null, null), geçerliyse (personel, null), geçersizse (null, hata mesajı) döner.
    public static async Task<(Kullanici? Personel, string? Hata)> Coz(
        MongoBaglami db, string? istenenId, string? mevcutId, CancellationToken iptal)
    {
        var id = Metin.BosIseNull(istenenId);
        if (id is null) return (null, null);

        var personel = await db.Kullanicilar.Find(k => k.Id == id).FirstOrDefaultAsync(iptal);
        if (personel is null) return (null, "Seçilen personel bulunamadı.");

        // Devre dışı bırakılan personele yeni iş atanamaz; ama zaten onda olan bir kayıt düzenlenirken ataması korunabilir.
        if (!personel.AktifMi && personel.Id != mevcutId)
            return (null, "Seçilen personelin hesabı devre dışı; iş atanamaz.");

        return (personel, null);
    }
}
