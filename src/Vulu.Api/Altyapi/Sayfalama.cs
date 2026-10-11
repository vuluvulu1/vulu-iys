namespace Vulu.Api.Altyapi;

// Liste uçlarının sayfa parametreleri. İstemciden gelen değerler olduğu gibi kullanılmaz: sınırsız bir sayfa
// boyutu sunucuyu yorar, çok büyük bir sayfa numarası da atlanacak kayıt sayısını taşırır.
public readonly record struct Sayfalama(int Sayfa, int Boyut)
{
    private const int EnFazlaSayfa = 100_000;

    public int Atla => (Sayfa - 1) * Boyut;

    public static Sayfalama Olustur(int sayfa, int boyut, int enFazlaBoyut) =>
        new(Math.Clamp(sayfa, 1, EnFazlaSayfa), Math.Clamp(boyut, 1, enFazlaBoyut));
}
