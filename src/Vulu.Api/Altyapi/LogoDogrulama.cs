using System.Buffers.Binary;
using Vulu.Shared;

namespace Vulu.Api.Altyapi;

// Yüklenen logonun gerçekten bildirilen türde, açılabilir ve makul boyutta bir resim olduğunu doğrular.
// Logo her belgede yeniden çizilir; bozuk ya da devasa bir resim bütün belgeleri üretilemez hale getirirdi.
public static class LogoDogrulama
{
    // Masaüstü uygulaması logoyu IsletmeKurallari.LogoKenari piksele küçültür; bu sınır, uygulamayı atlayıp
    // doğrudan API'ye gönderilen, sıkıştırılmış hali küçük ama açılınca çok bellek isteyen resimler içindir.
    private const int EnFazlaKenar = 2048;

    private static ReadOnlySpan<byte> PngImzasi => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegImzasi => [0xFF, 0xD8, 0xFF];

    // Başarılıysa null döner ve logo doldurulur; değilse kullanıcıya gösterilecek mesajı döner.
    public static string? Coz(string base64, string? tur, out byte[] logo)
    {
        logo = [];

        if (tur is null || !IsletmeKurallari.LogoTurleri.Contains(tur))
            return "Logo PNG ya da JPEG olmalı.";

        // Çözmeden önce uzunluk denetlenir: base64 metni, çözülmüş halinin 4/3 katıdır.
        if (base64.Length > IsletmeKurallari.EnFazlaLogoBayt / 3 * 4 + 4)
            return $"Logo en fazla {IsletmeKurallari.EnFazlaLogoBayt / 1024} KB olabilir.";

        try
        {
            logo = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return "Logo verisi okunamadı.";
        }

        if (logo.Length > IsletmeKurallari.EnFazlaLogoBayt)
            return $"Logo en fazla {IsletmeKurallari.EnFazlaLogoBayt / 1024} KB olabilir.";

        var boyut = tur == "image/png" ? PngBoyutu(logo) : JpegBoyutu(logo);
        if (boyut is not var (genislik, yukseklik))
            return "Logo dosyası bildirilen türde bir resim değil.";
        if (genislik > EnFazlaKenar || yukseklik > EnFazlaKenar)
            return $"Logo en fazla {EnFazlaKenar}×{EnFazlaKenar} piksel olabilir.";

        return Acilabiliyor(logo) ? null : "Logo dosyası bozuk; başka bir resim deneyin.";
    }

    // PNG: imzadan sonraki ilk blok IHDR'dir; genişlik ve yükseklik onun ilk sekiz baytıdır.
    private static (int Genislik, int Yukseklik)? PngBoyutu(ReadOnlySpan<byte> veri)
    {
        const int BoyutKonumu = 16, BaslikUzunlugu = 24;
        if (veri.Length < BaslikUzunlugu || !veri.StartsWith(PngImzasi) || !veri.Slice(12, 4).SequenceEqual("IHDR"u8))
            return null;

        var genislik = BinaryPrimitives.ReadUInt32BigEndian(veri[BoyutKonumu..]);
        var yukseklik = BinaryPrimitives.ReadUInt32BigEndian(veri[(BoyutKonumu + 4)..]);
        return genislik is 0 or > int.MaxValue || yukseklik is 0 or > int.MaxValue
            ? null
            : ((int)genislik, (int)yukseklik);
    }

    // JPEG: bölümler sırayla gezilir; boyut "çerçeve başlangıcı" (SOF) bölümündedir.
    private static (int Genislik, int Yukseklik)? JpegBoyutu(ReadOnlySpan<byte> veri)
    {
        if (!veri.StartsWith(JpegImzasi)) return null;

        var konum = 2;
        while (konum + 9 <= veri.Length && veri[konum] == 0xFF)
        {
            var isaret = veri[konum + 1];
            var uzunluk = BinaryPrimitives.ReadUInt16BigEndian(veri[(konum + 2)..]);

            // SOF0-SOF15; arada kalan C4, C8 ve CC başka tablolardır.
            if (isaret is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                var yukseklik = BinaryPrimitives.ReadUInt16BigEndian(veri[(konum + 5)..]);
                var genislik = BinaryPrimitives.ReadUInt16BigEndian(veri[(konum + 7)..]);
                return genislik == 0 || yukseklik == 0 ? null : (genislik, yukseklik);
            }

            if (uzunluk < 2) return null;
            konum += 2 + uzunluk;
        }

        return null;
    }

    // Belgeleri çizen kütüphane resmi açabiliyor mu? Başlığı düzgün ama içeriği bozuk dosyalar burada elenir.
    private static bool Acilabiliyor(byte[] logo)
    {
        try
        {
            using var resim = QuestPDF.Infrastructure.Image.FromBinaryData(logo);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
