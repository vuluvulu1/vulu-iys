using System.Globalization;
using System.Text.RegularExpressions;
using MongoDB.Bson;

namespace Vulu.Api.Altyapi;

// Aramalar Türkçe harf kurallarıyla küçültülmüş bir alan üzerinden yapılır; böylece "İSTANBUL" ile "istanbul",
// "IŞIK" ile "ışık" eşleşir (MongoDB'nin büyük/küçük harf duyarsız araması bunu doğru yapmaz).
public static class AramaMetni
{
    private static readonly CultureInfo Turkce = CultureInfo.GetCultureInfo("tr-TR");

    public static string Olustur(params string?[] parcalar) =>
        string.Join(' ', parcalar.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()))
            .ToLower(Turkce);

    public static BsonRegularExpression Desen(string arama) =>
        new(Regex.Escape(arama.Trim().ToLower(Turkce)));
}
