using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Kurallar;

// Servis kaydının durum ve fatura kuralları. Her yöntem kaydı yerinde değiştirir;
// kural ihlalinde kayda dokunmadan kullanıcıya gösterilecek mesajı, aksi halde null döner.
public static class ServisKaydiKurallari
{
    public static string? DurumuDegistir(ServisKaydi kayit, ServisDurumu yeniDurum, DateTime simdi)
    {
        if (kayit.Durum == yeniDurum) return null;

        // Fatura tamamlanmış işe kesilir; durum geri alınacaksa önce fatura kaldırılmalıdır.
        if (kayit.Fatura is not null)
            return "Faturası olan kaydın durumu değiştirilemez; önce faturayı silin.";

        kayit.Durum = yeniDurum;
        kayit.TamamlanmaTarihi = yeniDurum == ServisDurumu.Tamamlandi ? simdi : null;
        return null;
    }

    public static string? FaturayiKaydet(ServisKaydi kayit, decimal tutar, bool odendiMi, DateTime simdi)
    {
        if (kayit.Durum != ServisDurumu.Tamamlandi)
            return "Fatura yalnızca tamamlanmış servis kaydına kesilebilir.";

        if (tutar <= 0 || decimal.Round(tutar, 2) != tutar)
            return "Tutar sıfırdan büyük olmalı ve en fazla iki ondalık hane içermeli.";

        var fatura = kayit.Fatura ??= new Fatura { OlusturmaTarihi = simdi };
        fatura.Tutar = tutar;

        // Ödeme tarihi ödendi işaretlendiği an yazılır; tutar düzeltmesi tarihi değiştirmez.
        if (odendiMi && !fatura.OdendiMi)
            fatura.OdemeTarihi = simdi;
        else if (!odendiMi)
            fatura.OdemeTarihi = null;

        fatura.OdendiMi = odendiMi;
        return null;
    }

    public static string? Sil(ServisKaydi kayit, DateTime simdi)
    {
        if (kayit.Fatura is not null)
            return "Faturası olan servis kaydı silinemez; önce faturayı silin.";

        kayit.SilinmeTarihi = simdi;
        return null;
    }
}
