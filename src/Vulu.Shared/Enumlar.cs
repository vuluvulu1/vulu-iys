using System.Text.Json.Serialization;

namespace Vulu.Shared;

// Enum'lar JSON'da ad olarak taşınır; dönüştürücü burada tanımlı olduğu için API ve istemci ayrıca ayar gerektirmez.

[JsonConverter(typeof(JsonStringEnumConverter<KullaniciRol>))]
public enum KullaniciRol
{
    Admin,
    Teknisyen
}

[JsonConverter(typeof(JsonStringEnumConverter<ServisDurumu>))]
public enum ServisDurumu
{
    Beklemede,
    // Müşteriye fiyat teklifi verildi, yanıtı bekleniyor.
    OnayBekliyor,
    DevamEdiyor,
    // İş bitti, cihaz müşterinin almasını bekliyor.
    Tamamlandi,
    TeslimEdildi,
    IptalEdildi
}

[JsonConverter(typeof(JsonStringEnumConverter<RandevuDurumu>))]
public enum RandevuDurumu
{
    Planlandi,
    Tamamlandi,
    IptalEdildi
}

// Servis kaydının işlem geçmişindeki satırın türü.
[JsonConverter(typeof(JsonStringEnumConverter<IslemTuru>))]
public enum IslemTuru
{
    KayitAcildi,
    KayitGuncellendi,
    DurumDegisti,
    Not,
    FaturaKesildi,
    FaturaGuncellendi,
    FaturaSilindi,
    KayitSilindi,
    TeklifVerildi,
    TeklifYanitlandi,
    ParcaEklendi,
    ParcaCikarildi,
    MusteriBilgilendirildi
}

[JsonConverter(typeof(JsonStringEnumConverter<BildirimKanali>))]
public enum BildirimKanali
{
    WhatsApp,
    Eposta,
    Telefon,
    Sms
}

[JsonConverter(typeof(JsonStringEnumConverter<TeklifDurumu>))]
public enum TeklifDurumu
{
    Bekliyor,
    Onaylandi,
    Reddedildi
}

// Durumların kullanıcıya gösterilen adları; arayüz, belgeler ve işlem geçmişi aynı metni kullanır.
public static class DurumAdlari
{
    public static string Ad(this ServisDurumu durum) => durum switch
    {
        ServisDurumu.Beklemede => "Beklemede",
        ServisDurumu.OnayBekliyor => "Onay bekliyor",
        ServisDurumu.DevamEdiyor => "Devam ediyor",
        ServisDurumu.Tamamlandi => "Tamamlandı",
        ServisDurumu.TeslimEdildi => "Teslim edildi",
        ServisDurumu.IptalEdildi => "İptal edildi",
        _ => durum.ToString()
    };

    public static string Ad(this TeklifDurumu durum) => durum switch
    {
        TeklifDurumu.Bekliyor => "Yanıt bekleniyor",
        TeklifDurumu.Onaylandi => "Onaylandı",
        TeklifDurumu.Reddedildi => "Reddedildi",
        _ => durum.ToString()
    };

    // İş henüz bitmemiş ve iptal edilmemiş.
    public static bool AcikMi(this ServisDurumu durum) =>
        durum is ServisDurumu.Beklemede or ServisDurumu.OnayBekliyor or ServisDurumu.DevamEdiyor;

    // İş bitmiş; cihaz teslim edilmiş ya da teslim edilmeyi bekliyor.
    public static bool BittiMi(this ServisDurumu durum) =>
        durum is ServisDurumu.Tamamlandi or ServisDurumu.TeslimEdildi;
}
