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
    DevamEdiyor,
    Tamamlandi,
    IptalEdildi
}

[JsonConverter(typeof(JsonStringEnumConverter<RandevuDurumu>))]
public enum RandevuDurumu
{
    Planlandi,
    Tamamlandi,
    IptalEdildi
}
