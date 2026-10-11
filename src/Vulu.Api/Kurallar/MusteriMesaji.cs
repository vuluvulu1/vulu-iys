using Vulu.Api.Altyapi;
using Vulu.Api.Pdf;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Kurallar;

// Müşteriye gönderilecek bilgilendirme mesajının metni. Kaydın o andaki durumuna göre yazılır.
public static class MusteriMesaji
{
    public static string Olustur(ServisKaydi kayit, Isletme isletme, string takipAdresi)
    {
        var cihaz = ServisFormu.CihazAdi(kayit);
        var telefon = string.IsNullOrWhiteSpace(isletme.Telefon) ? "" : $" Tel: {isletme.Telefon}";

        var govde = kayit.Durum switch
        {
            ServisDurumu.OnayBekliyor when kayit.Teklif is { } teklif =>
                $"{cihaz} cihazınızın onarımı için tahmini ücret {Zaman.Para(teklif.Tutar)} (KDV dahil). " +
                "İşleme başlayabilmemiz için onayınızı bekliyoruz.",
            ServisDurumu.DevamEdiyor => $"{cihaz} cihazınızın onarımına başlandı.",
            ServisDurumu.Tamamlandi => kayit.Fatura is { } fatura
                ? $"{cihaz} cihazınızın işlemi tamamlandı, teslim alabilirsiniz. Ücret: {Zaman.Para(fatura.Tutar)} (KDV dahil)."
                : $"{cihaz} cihazınızın işlemi tamamlandı, teslim alabilirsiniz.",
            ServisDurumu.TeslimEdildi => $"{cihaz} cihazınız teslim edildi. Bizi tercih ettiğiniz için teşekkür ederiz.",
            ServisDurumu.IptalEdildi => $"{cihaz} cihazınız için işlem yapılmayacak; cihazınızı teslim alabilirsiniz.",
            _ => $"{cihaz} cihazınız servisimize kabul edildi."
        };

        return $"Merhaba {kayit.Musteri.AdSoyad}, {govde} Kayıt no: {kayit.No}. " +
               $"Cihazınızın durumunu buradan izleyebilirsiniz: {takipAdresi} – {isletme.Ad}.{telefon}";
    }

    // "05551234567" -> "905551234567". Kayıtlı numara beklenen biçimde değilse WhatsApp bağlantısı verilmez.
    public static string? WhatsAppNumarasi(string? telefon) =>
        telefon is { Length: 11 } && telefon[0] == '0' && telefon.All(char.IsAsciiDigit) ? "9" + telefon : null;
}
