using Vulu.Shared;

namespace Vulu.Desktop.Servisler;

// Açık servis kaydı penceresinin sekmelerinin paylaştığı durum: hangi kayıt üzerinde çalışıldığı, o an bir
// isteğin sürüp sürmediği ve son hata. Sekmeler isteklerini buradan geçirir; böylece meşgul durumu ve hata
// mesajı tek yerde tutulur ve pencerenin tamamı aynı anda güncellenir.
public class KayitIslemleri
{
    private readonly ApiIstemcisi _api;
    private readonly Func<ServisKaydiYaniti, Task> _kayitDegisti;
    private readonly Action _yenidenCiz;

    public KayitIslemleri(ApiIstemcisi api, Func<ServisKaydiYaniti, Task> kayitDegisti, Action yenidenCiz)
    {
        _api = api;
        _kayitDegisti = kayitDegisti;
        _yenidenCiz = yenidenCiz;
    }

    // Pencerenin meşgul durumunu ve hata mesajını etkilememesi gereken arka plan yüklemeleri için.
    public ApiIstemcisi Api => _api;

    public string KayitId { get; set; } = "";
    public bool Mesgul { get; private set; }
    public string? Hata { get; private set; }

    public string Yol => $"api/servis-kayitlari/{KayitId}";

    public void HatayiGoster(string? hata)
    {
        Hata = hata;
        _yenidenCiz();
    }

    // Kaydı değiştiren isteği çalıştırır; başarılıysa güncel kaydı pencereye bildirir ve true döner.
    public async Task<bool> Degistir(Func<ApiIstemcisi, Task<ApiSonucu<ServisKaydiYaniti>>> istek)
    {
        var sonuc = await Yap(istek);
        if (sonuc is not { Basarili: true, Deger: not null }) return false;

        await _kayitDegisti(sonuc.Deger);
        return true;
    }

    // Kaydın kendisini döndürmeyen istekler (geçmiş, bildirim metni, belge) için: meşgul durumunu ve hatayı yönetir.
    public async Task<ApiSonucu<T>> Yap<T>(Func<ApiIstemcisi, Task<ApiSonucu<T>>> istek)
    {
        Mesgul = true;
        Hata = null;
        _yenidenCiz();

        var sonuc = await istek(_api);

        Mesgul = false;
        Hata = sonuc.Basarili ? null : sonuc.Hata;
        _yenidenCiz();
        return sonuc;
    }

    // Belgeyi sunucudan indirir ve bilgisayarın PDF görüntüleyicisinde açar.
    public async Task BelgeyiAc(string uc, string dosyaAdi)
    {
        var sonuc = await Yap(api => api.Indir($"{Yol}/{uc}"));
        if (sonuc is { Basarili: true, Deger: not null })
            HatayiGoster(BelgeAcici.Ac(dosyaAdi, sonuc.Deger));
    }
}
