using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Vulu.Api.Altyapi;

// Bir Excel sayfası: başlık satırı ve altındaki satırlar. Hücre değeri metin, sayı, para (decimal), tarih (UTC),
// evet/hayır ya da boş (null) olabilir.
public record ExcelSayfasi(string Ad, IReadOnlyList<string> Basliklar, IReadOnlyList<object?[]> Satirlar);

// Excel dosyası (.xlsx) üretir. Dosya biçimi, içinde birkaç XML bulunan bir zip'tir; ihtiyaç duyulan kadarı
// (sayfalar, kalın başlık satırı, tarih ve para biçimi) burada doğrudan yazılır, dışarıdan bir kitaplık gerekmez.
//
// Kullanıcının yazdığı her şey hücreye metin olarak konur. "=" ile başlayan bir müşteri adı ya da not, Excel'de
// formül olarak çalışmaz; dışa aktarılan dosyayı açan kişinin bilgisayarında komut çalıştırma yolu kapalıdır.
public static class ExcelYazici
{
    public const string IcerikTuru = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string AnaAd = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string IliskiAdi = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PaketIliskileri = "http://schemas.openxmlformats.org/package/2006/relationships";

    // styles.xml içindeki hücre biçimlerinin sıra numaraları.
    private const int BaslikBicimi = 1, TarihBicimi = 2, ParaBicimi = 3;

    private const int EnFazlaSayfaAdi = 31, EnAzSutunGenisligi = 10, EnFazlaSutunGenisligi = 60;

    public static byte[] Olustur(params ExcelSayfasi[] sayfalar)
    {
        if (sayfalar.Length == 0) throw new ArgumentException("En az bir sayfa gerekli.", nameof(sayfalar));

        using var bellek = new MemoryStream();
        using (var zip = new ZipArchive(bellek, ZipArchiveMode.Create, leaveOpen: true))
        {
            Yaz(zip, "[Content_Types].xml", x => IcerikTurleri(x, sayfalar.Length));
            Yaz(zip, "_rels/.rels", PaketIliskisi);
            Yaz(zip, "xl/workbook.xml", x => Kitap(x, sayfalar));
            Yaz(zip, "xl/_rels/workbook.xml.rels", x => KitapIliskileri(x, sayfalar.Length));
            Yaz(zip, "xl/styles.xml", Bicimler);
            for (var i = 0; i < sayfalar.Length; i++)
            {
                var sayfa = sayfalar[i];
                Yaz(zip, $"xl/worksheets/sheet{i + 1}.xml", x => Sayfa(x, sayfa));
            }
        }

        return bellek.ToArray();
    }

    private static void Yaz(ZipArchive zip, string yol, Action<XmlWriter> icerik)
    {
        using var akis = zip.CreateEntry(yol, CompressionLevel.Optimal).Open();
        using var yazici = XmlWriter.Create(akis, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        yazici.WriteStartDocument(standalone: true);
        icerik(yazici);
        yazici.WriteEndDocument();
    }

    private static void IcerikTurleri(XmlWriter x, int sayfaSayisi)
    {
        const string ad = "http://schemas.openxmlformats.org/package/2006/content-types";
        x.WriteStartElement("Types", ad);
        Varsayilan(x, ad, "rels", "application/vnd.openxmlformats-package.relationships+xml");
        Varsayilan(x, ad, "xml", "application/xml");
        Gecersiz(x, ad, "/xl/workbook.xml", IcerikTuru + ".main+xml");
        Gecersiz(x, ad, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        for (var i = 1; i <= sayfaSayisi; i++)
            Gecersiz(x, ad, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        x.WriteEndElement();

        static void Varsayilan(XmlWriter x, string ad, string uzanti, string tur)
        {
            x.WriteStartElement("Default", ad);
            x.WriteAttributeString("Extension", uzanti);
            x.WriteAttributeString("ContentType", tur);
            x.WriteEndElement();
        }

        static void Gecersiz(XmlWriter x, string ad, string parca, string tur)
        {
            x.WriteStartElement("Override", ad);
            x.WriteAttributeString("PartName", parca);
            x.WriteAttributeString("ContentType", tur);
            x.WriteEndElement();
        }
    }

    private static void PaketIliskisi(XmlWriter x)
    {
        x.WriteStartElement("Relationships", PaketIliskileri);
        Iliski(x, "rId1", IliskiAdi + "/officeDocument", "xl/workbook.xml");
        x.WriteEndElement();
    }

    private static void KitapIliskileri(XmlWriter x, int sayfaSayisi)
    {
        x.WriteStartElement("Relationships", PaketIliskileri);
        for (var i = 1; i <= sayfaSayisi; i++)
            Iliski(x, $"rId{i}", IliskiAdi + "/worksheet", $"worksheets/sheet{i}.xml");
        Iliski(x, $"rId{sayfaSayisi + 1}", IliskiAdi + "/styles", "styles.xml");
        x.WriteEndElement();
    }

    private static void Iliski(XmlWriter x, string kimlik, string tur, string hedef)
    {
        x.WriteStartElement("Relationship", PaketIliskileri);
        x.WriteAttributeString("Id", kimlik);
        x.WriteAttributeString("Type", tur);
        x.WriteAttributeString("Target", hedef);
        x.WriteEndElement();
    }

    private static void Kitap(XmlWriter x, ExcelSayfasi[] sayfalar)
    {
        x.WriteStartElement("workbook", AnaAd);
        x.WriteAttributeString("xmlns", "r", null, IliskiAdi);
        x.WriteStartElement("sheets", AnaAd);

        var kullanilanAdlar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sayfalar.Length; i++)
        {
            x.WriteStartElement("sheet", AnaAd);
            x.WriteAttributeString("name", SayfaAdi(sayfalar[i].Ad, i, kullanilanAdlar));
            x.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("id", IliskiAdi, $"rId{i + 1}");
            x.WriteEndElement();
        }

        x.WriteEndElement();
        x.WriteEndElement();
    }

    // Excel sayfa adında bazı karakterleri ve 31 karakterden uzun adları kabul etmez; adlar da tekil olmalıdır.
    private static string SayfaAdi(string ad, int sira, HashSet<string> kullanilanlar)
    {
        var temiz = new string(Temizle(ad).Where(harf => !@"[]:*?/\".Contains(harf)).ToArray()).Trim().Trim('\'');
        if (temiz.Length > EnFazlaSayfaAdi) temiz = temiz[..EnFazlaSayfaAdi];
        if (temiz.Length == 0 || !kullanilanlar.Add(temiz))
        {
            temiz = $"Sayfa{sira + 1}";
            kullanilanlar.Add(temiz);
        }

        return temiz;
    }

    private static void Bicimler(XmlWriter x)
    {
        // Yazı tipleri: 0 olağan, 1 kalın. Hücre biçimleri: 0 olağan, 1 başlık, 2 tarih-saat, 3 para.
        // İçerik sabittir; kök öğenin altına olduğu gibi yazılır.
        x.WriteStartElement("styleSheet", AnaAd);
        x.WriteRaw("""
              <numFmts count="1"><numFmt numFmtId="164" formatCode="dd.mm.yyyy hh:mm"/></numFmts>
              <fonts count="2">
                <font><sz val="11"/><name val="Calibri"/></font>
                <font><b/><sz val="11"/><name val="Calibri"/></font>
              </fonts>
              <fills count="3">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFF6EFE6"/><bgColor indexed="64"/></patternFill></fill>
              </fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="4">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/>
                <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                <xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
              </cellXfs>
            """);
        x.WriteEndElement();
    }

    private static void Sayfa(XmlWriter x, ExcelSayfasi sayfa)
    {
        x.WriteStartElement("worksheet", AnaAd);

        // Başlık satırı, liste aşağı kaydırılırken yerinde kalır.
        x.WriteRaw($"""<sheetViews xmlns="{AnaAd}"><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");

        x.WriteStartElement("cols", AnaAd);
        for (var s = 0; s < sayfa.Basliklar.Count; s++)
        {
            x.WriteStartElement("col", AnaAd);
            var sira = (s + 1).ToString(CultureInfo.InvariantCulture);
            x.WriteAttributeString("min", sira);
            x.WriteAttributeString("max", sira);
            x.WriteAttributeString("width", SutunGenisligi(sayfa, s).ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("customWidth", "1");
            x.WriteEndElement();
        }
        x.WriteEndElement();

        x.WriteStartElement("sheetData", AnaAd);
        Satir(x, 1, sayfa.Basliklar.Cast<object?>().ToArray(), BaslikBicimi);
        for (var i = 0; i < sayfa.Satirlar.Count; i++)
            Satir(x, i + 2, sayfa.Satirlar[i], bicim: null);
        x.WriteEndElement();

        x.WriteEndElement();
    }

    private static void Satir(XmlWriter x, int satirNo, object?[] hucreler, int? bicim)
    {
        x.WriteStartElement("row", AnaAd);
        x.WriteAttributeString("r", satirNo.ToString(CultureInfo.InvariantCulture));

        for (var s = 0; s < hucreler.Length; s++)
        {
            if (hucreler[s] is not { } deger) continue;

            x.WriteStartElement("c", AnaAd);
            x.WriteAttributeString("r", SutunHarfi(s) + satirNo.ToString(CultureInfo.InvariantCulture));

            switch (deger)
            {
                case DateTime tarih:
                    Sayi(x, Zaman.TurkiyeSaati(tarih).ToOADate(), bicim ?? TarihBicimi);
                    break;
                case decimal para:
                    Sayi(x, (double)para, bicim ?? ParaBicimi);
                    break;
                case int or long or double:
                    Sayi(x, Convert.ToDouble(deger, CultureInfo.InvariantCulture), bicim);
                    break;
                case bool evet:
                    MetinHucresi(x, evet ? "Evet" : "Hayır", bicim);
                    break;
                default:
                    MetinHucresi(x, Convert.ToString(deger, CultureInfo.InvariantCulture) ?? "", bicim);
                    break;
            }

            x.WriteEndElement();
        }

        x.WriteEndElement();
    }

    private static void Sayi(XmlWriter x, double deger, int? bicim)
    {
        if (bicim is { } b) x.WriteAttributeString("s", b.ToString(CultureInfo.InvariantCulture));
        x.WriteElementString("v", AnaAd, deger.ToString("R", CultureInfo.InvariantCulture));
    }

    // Metin "inlineStr" türünde yazılır: Excel içeriği ne olursa olsun düz metin olarak gösterir, formül saymaz.
    private static void MetinHucresi(XmlWriter x, string metin, int? bicim)
    {
        x.WriteAttributeString("t", "inlineStr");
        if (bicim is { } b) x.WriteAttributeString("s", b.ToString(CultureInfo.InvariantCulture));
        x.WriteStartElement("is", AnaAd);
        x.WriteStartElement("t", AnaAd);
        x.WriteAttributeString("xml", "space", null, "preserve");
        x.WriteString(Temizle(metin));
        x.WriteEndElement();
        x.WriteEndElement();
    }

    // XML'de yer alamayan denetim karakterleri atılır; aksi halde dosya açılmaz.
    private static string Temizle(string metin) =>
        metin.All(XmlConvert.IsXmlChar) ? metin : new string(metin.Where(XmlConvert.IsXmlChar).ToArray());

    // Sütun, en uzun içeriğine göre genişler (makul sınırlar içinde).
    private static int SutunGenisligi(ExcelSayfasi sayfa, int sutun)
    {
        var enUzun = sayfa.Basliklar[sutun].Length;
        foreach (var satir in sayfa.Satirlar)
        {
            if (sutun >= satir.Length) continue;
            var uzunluk = satir[sutun] switch
            {
                null => 0,
                DateTime => 16,
                decimal => 12,
                string metin => metin.Length,
                var diger => Convert.ToString(diger, CultureInfo.InvariantCulture)?.Length ?? 0
            };
            if (uzunluk > enUzun) enUzun = uzunluk;
        }

        return Math.Clamp(enUzun + 2, EnAzSutunGenisligi, EnFazlaSutunGenisligi);
    }

    // 0 -> A, 25 -> Z, 26 -> AA.
    private static string SutunHarfi(int sira)
    {
        var harfler = new StringBuilder();
        for (var kalan = sira; kalan >= 0; kalan = kalan / 26 - 1)
            harfler.Insert(0, (char)('A' + kalan % 26));
        return harfler.ToString();
    }
}
