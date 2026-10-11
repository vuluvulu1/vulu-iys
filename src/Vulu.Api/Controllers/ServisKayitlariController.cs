using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using Vulu.Api.Altyapi;
using Vulu.Api.Kurallar;
using Vulu.Api.Pdf;
using Vulu.Api.Veri;
using Vulu.Api.Veri.Belgeler;
using Vulu.Shared;

namespace Vulu.Api.Controllers;

[ApiController]
[Route("api/servis-kayitlari")]
public class ServisKayitlariController : ControllerBase
{
    private const int EnFazlaSayfaBoyutu = 100;
    private const int EnFazlaGecmisSatiri = 300;

    private readonly MongoBaglami _db;
    private readonly GecerliIsletme _isletme;
    private readonly IslemGunlugu _gunluk;
    private readonly IConfiguration _ayarlar;

    public ServisKayitlariController(MongoBaglami db, GecerliIsletme isletme, IslemGunlugu gunluk, IConfiguration ayarlar)
    {
        _db = db;
        _isletme = isletme;
        _gunluk = gunluk;
        _ayarlar = ayarlar;
    }

    [HttpGet]
    public async Task<ActionResult<Sayfa<ServisKaydiYaniti>>> Listele(
        [FromQuery] string? arama, [FromQuery] ServisDurumu? durum,
        [FromQuery] FaturaSuzgeci fatura = FaturaSuzgeci.Hepsi,
        [FromQuery] string? musteriId = null, [FromQuery] string? teknisyenId = null,
        [FromQuery] string? seriNo = null,
        [FromQuery] int sayfa = 1, [FromQuery] int boyut = 25,
        CancellationToken iptal = default)
    {
        if (!KimlikGecerli(musteriId) || !KimlikGecerli(teknisyenId))
            return Problem(detail: "Geçersiz süzgeç.", statusCode: StatusCodes.Status400BadRequest);

        sayfa = Math.Max(1, sayfa);
        boyut = Math.Clamp(boyut, 1, EnFazlaSayfaBoyutu);

        var f = Builders<ServisKaydi>.Filter;
        var filtre = f.Eq(s => s.SilinmeTarihi, null);

        if (durum is not null) filtre &= f.Eq(s => s.Durum, durum.Value);
        if (musteriId is not null) filtre &= f.Eq(s => s.MusteriId, musteriId);
        if (teknisyenId is not null) filtre &= f.Eq(s => s.TeknisyenId, teknisyenId);

        // Aynı cihazın bütün kayıtları: seri no birebir eşleşir.
        if (SeriNoyuNormalle(seriNo) is { } seri) filtre &= f.Eq(s => s.SeriNo, seri);

        filtre &= fatura switch
        {
            FaturaSuzgeci.Faturali => f.Ne(s => s.Fatura, null),
            FaturaSuzgeci.Odenmemis => f.Eq(s => s.Fatura!.OdendiMi, false),
            FaturaSuzgeci.Odenmis => f.Eq(s => s.Fatura!.OdendiMi, true),
            _ => f.Empty
        };

        if (!string.IsNullOrWhiteSpace(arama))
        {
            var desen = AramaMetni.Desen(arama);
            filtre &= f.Or(f.Regex(s => s.AramaMetni, desen), f.Regex(s => s.Musteri.AramaMetni, desen));
        }

        var toplam = await _db.ServisKayitlari.CountDocumentsAsync(filtre, cancellationToken: iptal);
        var kayitlar = await _db.ServisKayitlari.Find(filtre)
            .SortByDescending(s => s.OlusturmaTarihi)
            .Skip((sayfa - 1) * boyut)
            .Limit(boyut)
            .ToListAsync(iptal);

        return new Sayfa<ServisKaydiYaniti>(kayitlar.Select(k => k.YanitaCevir()).ToList(), toplam, sayfa, boyut);
    }

    [HttpGet("{id:oid}")]
    public async Task<ActionResult<ServisKaydiYaniti>> Getir(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        return kayit is null ? Bulunamadi() : kayit.YanitaCevir();
    }

    [HttpPost]
    public async Task<ActionResult<ServisKaydiYaniti>> Ekle(ServisKaydiIstegi istek, CancellationToken iptal)
    {
        var kayit = new ServisKaydi();
        if (await Uygula(kayit, istek, iptal) is { } hata) return hata;

        // Numara doğrulamadan sonra alınır ki reddedilen istekler sırada boşluk bırakmasın.
        var yil = Zaman.TurkiyeSaati(kayit.OlusturmaTarihi).Year;
        kayit.No = ServisKaydiKurallari.KayitNo(yil, await _db.SiradakiNumaraAsync(MongoBaglami.ServisSayaci(yil), iptal));
        kayit.TakipKodu = ServisKaydiKurallari.YeniTakipKodu();
        AramaMetniniYenile(kayit);

        await _db.ServisKayitlari.InsertOneAsync(kayit, cancellationToken: iptal);
        await _gunluk.YazAsync(kayit.Id, IslemTuru.KayitAcildi, ServisFormu.CihazAdi(kayit), iptal);

        return CreatedAtAction(nameof(Getir), new { id = kayit.Id }, kayit.YanitaCevir());
    }

    [HttpPut("{id:oid}")]
    public async Task<ActionResult<ServisKaydiYaniti>> Guncelle(string id, ServisKaydiIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        var once = Alanlar(kayit);
        if (await Uygula(kayit, istek, iptal) is { } hata) return hata;

        return await Kaydet(kayit, IslemTuru.KayitGuncellendi, DegisiklikOzeti(once, Alanlar(kayit)), iptal);
    }

    [HttpPut("{id:oid}/durum")]
    public async Task<ActionResult<ServisKaydiYaniti>> DurumuDegistir(string id, ServisDurumuIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        var onceki = kayit.Durum;
        if (ServisKaydiKurallari.DurumuDegistir(kayit, istek.Durum, DateTime.UtcNow) is { } hata)
            return KuralIhlali(hata);

        return await Kaydet(kayit, IslemTuru.DurumDegisti,
            onceki == kayit.Durum ? null : $"{onceki.Ad()} → {kayit.Durum.Ad()}", iptal);
    }

    // Teknisyenin cihazda ne yaptığı; teslim formuna ve faturaya yazılır.
    [HttpPut("{id:oid}/yapilan-islem")]
    public async Task<ActionResult<ServisKaydiYaniti>> YapilanIslemiKaydet(string id, YapilanIslemIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        var yeni = Metin.BosIseNull(istek.Metin);
        var degisti = yeni != kayit.YapilanIslem;
        kayit.YapilanIslem = yeni;

        return await Kaydet(kayit, IslemTuru.KayitGuncellendi, degisti ? "Yapılan işlem yazıldı" : null, iptal);
    }

    // --- Teklif ---

    [HttpPut("{id:oid}/teklif")]
    public async Task<ActionResult<ServisKaydiYaniti>> TeklifVer(string id, TeklifIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        if (ServisKaydiKurallari.TeklifVer(kayit, istek.Tutar, Metin.BosIseNull(istek.Aciklama), DateTime.UtcNow) is { } hata)
            return KuralIhlali(hata);

        return await Kaydet(kayit, IslemTuru.TeklifVerildi, $"{Zaman.Para(istek.Tutar)} teklif verildi", iptal);
    }

    [HttpPut("{id:oid}/teklif/yanit")]
    public async Task<ActionResult<ServisKaydiYaniti>> TeklifiYanitla(string id, TeklifYanitIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        var not = Metin.BosIseNull(istek.Not);
        if (ServisKaydiKurallari.TeklifiYanitla(kayit, istek.Onaylandi, not, DateTime.UtcNow) is { } hata)
            return KuralIhlali(hata);

        var aciklama = $"Müşteri {Zaman.Para(kayit.Teklif!.Tutar)} teklifi {(istek.Onaylandi ? "onayladı" : "reddetti")}";
        return await Kaydet(kayit, IslemTuru.TeklifYanitlandi, not is null ? aciklama : $"{aciklama} – {not}", iptal);
    }

    // --- Kullanılan parçalar ---

    // Parçayı stoktan düşer ve kayda işler. Stok tek bir atomik işlemle azaltılır: aynı anda gelen iki istek
    // stokta olmayan parçayı kullanamaz.
    [HttpPost("{id:oid}/parcalar")]
    public async Task<ActionResult<ServisKaydiYaniti>> ParcaEkle(string id, ParcaKullanimIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();
        if (ServisKaydiKurallari.ParcaEklenebilirMi(kayit) is { } hata) return KuralIhlali(hata);

        var parca = await _db.Parcalar.FindOneAndUpdateAsync<Parca>(
            p => p.Id == istek.ParcaId && p.SilinmeTarihi == null && p.StokAdedi >= istek.Adet,
            Builders<Parca>.Update.Inc(p => p.StokAdedi, -istek.Adet), cancellationToken: iptal);

        if (parca is null)
        {
            var mevcut = await _db.Parcalar.Find(p => p.Id == istek.ParcaId && p.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
            return mevcut is null
                ? Problem(detail: "Seçilen parça bulunamadı.", statusCode: StatusCodes.Status400BadRequest)
                : KuralIhlali($"Stokta yeterli “{mevcut.Ad}” yok (mevcut: {mevcut.StokAdedi}).");
        }

        kayit.Parcalar.Add(new KullanilanParca
        {
            ParcaId = parca.Id, Ad = parca.Ad, Adet = istek.Adet, BirimFiyat = parca.BirimFiyat
        });

        if (!await Yaz(kayit, iptal))
        {
            // Kayıt bu sırada değişmiş; düşülen stok geri konur.
            await _db.Parcalar.UpdateOneAsync(p => p.Id == parca.Id,
                Builders<Parca>.Update.Inc(p => p.StokAdedi, istek.Adet), cancellationToken: CancellationToken.None);
            return Cakisma();
        }

        await _gunluk.StokHareketiYazAsync(parca.Id, -istek.Adet, "Servis kaydında kullanıldı", kayit, iptal);
        await _gunluk.YazAsync(kayit.Id, IslemTuru.ParcaEklendi, $"{istek.Adet} × {parca.Ad}", iptal);
        return kayit.YanitaCevir();
    }

    // Parçayı kayıttan çıkarır ve stoğa geri koyar.
    [HttpDelete("{id:oid}/parcalar/{satirId:oid}")]
    public async Task<ActionResult<ServisKaydiYaniti>> ParcaCikar(string id, string satirId, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();
        if (ServisKaydiKurallari.ParcaCikarilabilirMi(kayit) is { } hata) return KuralIhlali(hata);

        var satir = kayit.Parcalar.Find(p => p.Id == satirId);
        if (satir is null)
            return Problem(detail: "Parça satırı bulunamadı.", statusCode: StatusCodes.Status404NotFound);

        kayit.Parcalar.Remove(satir);
        if (!await Yaz(kayit, iptal)) return Cakisma();

        // Stok kartı sonradan silinmiş olsa da adet geri yazılır; kart geri alınırsa sayı doğru olur.
        await _db.Parcalar.UpdateOneAsync(p => p.Id == satir.ParcaId,
            Builders<Parca>.Update.Inc(p => p.StokAdedi, satir.Adet), cancellationToken: iptal);
        await _gunluk.StokHareketiYazAsync(satir.ParcaId, satir.Adet, "Servis kaydından çıkarıldı", kayit, iptal);
        await _gunluk.YazAsync(kayit.Id, IslemTuru.ParcaCikarildi, $"{satir.Adet} × {satir.Ad}", iptal);
        return kayit.YanitaCevir();
    }

    // --- Fatura ---

    // Fatura yoksa keser, varsa tutarını ve ödeme durumunu günceller.
    [HttpPut("{id:oid}/fatura")]
    public async Task<ActionResult<ServisKaydiYaniti>> FaturayiKaydet(string id, FaturaIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        var yeniFatura = kayit.Fatura is null;
        var simdi = DateTime.UtcNow;
        if (ServisKaydiKurallari.FaturayiKaydet(kayit, istek.Tutar, istek.OdendiMi, simdi) is { } hata)
            return KuralIhlali(hata);

        // Numara fatura ilk kesildiğinde verilir. Kayıt bu sırada başkası tarafından değiştirildiyse yazma
        // reddedilir ve alınan numara boşa gider; sırada atlama olabilir ama aynı numara iki kez verilmez.
        if (kayit.Fatura!.No.Length == 0)
        {
            var yil = Zaman.TurkiyeSaati(simdi).Year;
            var sira = await _db.SiradakiNumaraAsync($"fatura-{yil}", iptal);
            kayit.Fatura.No = $"{FaturaBelgesi.NumaraOnEki(_isletme.Zorunlu)}-{yil}-{sira:D6}";
        }

        var aciklama = $"{kayit.Fatura.No}: {Zaman.Para(istek.Tutar)}, {(istek.OdendiMi ? "ödendi" : "ödenmedi")}";
        return await Kaydet(kayit, yeniFatura ? IslemTuru.FaturaKesildi : IslemTuru.FaturaGuncellendi, aciklama, iptal);
    }

    [HttpDelete("{id:oid}/fatura")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<ActionResult<ServisKaydiYaniti>> FaturayiSil(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();
        if (kayit.Fatura is not { } fatura)
            return Problem(detail: "Bu kaydın faturası yok.", statusCode: StatusCodes.Status404NotFound);

        kayit.Fatura = null;
        return await Kaydet(kayit, IslemTuru.FaturaSilindi, $"{fatura.No}: {Zaman.Para(fatura.Tutar)}", iptal);
    }

    // --- Belgeler ---

    [HttpGet("{id:oid}/fatura/pdf")]
    public async Task<IActionResult> FaturaPdf(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();
        if (kayit.Fatura is null)
            return Problem(detail: "Bu kaydın faturası yok.", statusCode: StatusCodes.Status404NotFound);

        var pdf = FaturaBelgesi.Olustur(kayit, await MusteriyiOku(kayit, iptal), _isletme.Zorunlu);
        return File(pdf, "application/pdf", $"Fatura-{kayit.Fatura.No}.pdf");
    }

    [HttpGet("{id:oid}/kabul-formu")]
    public async Task<IActionResult> KabulFormu(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        var pdf = ServisFormu.Kabul(kayit, await MusteriyiOku(kayit, iptal), _isletme.Zorunlu,
            TakipSayfasi.Adres(Request, _ayarlar, _isletme.Zorunlu));
        return File(pdf, "application/pdf", $"Kabul-{kayit.No}.pdf");
    }

    [HttpGet("{id:oid}/teslim-formu")]
    public async Task<IActionResult> TeslimFormu(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        // Devam eden işin cihazı teslim edilmez; iptal edilen işin cihazı iade edilir.
        if (kayit.Durum.AcikMi())
            return KuralIhlali("Teslim formu için kayıt tamamlanmış ya da iptal edilmiş olmalı.");

        var pdf = ServisFormu.Teslim(kayit, await MusteriyiOku(kayit, iptal), _isletme.Zorunlu);
        return File(pdf, "application/pdf", $"Teslim-{kayit.No}.pdf");
    }

    // --- İşlem geçmişi ---

    [HttpGet("{id:oid}/gecmis")]
    public async Task<ActionResult<IReadOnlyList<IslemYaniti>>> Gecmis(string id, CancellationToken iptal)
    {
        if (await Bul(id, iptal) is null) return Bulunamadi();

        var satirlar = await _db.IslemGecmisi.Find(i => i.ServisKaydiId == id)
            .SortByDescending(i => i.Tarih)
            .Limit(EnFazlaGecmisSatiri)
            .ToListAsync(iptal);

        return satirlar.Select(i => i.YanitaCevir()).ToList();
    }

    // Kayda serbest not ekler (örn. "Müşteri arandı, parça bekleniyor").
    [HttpPost("{id:oid}/notlar")]
    public async Task<ActionResult<IReadOnlyList<IslemYaniti>>> NotEkle(string id, NotIstegi istek, CancellationToken iptal)
    {
        if (await Bul(id, iptal) is null) return Bulunamadi();

        await _gunluk.YazAsync(id, IslemTuru.Not, istek.Metin.Trim(), iptal);
        return await Gecmis(id, iptal);
    }

    // --- Müşteriyi bilgilendirme ---

    // Kaydın durumuna uygun hazır mesaj ve müşterinin iletişim bilgileri.
    [HttpGet("{id:oid}/bildirim")]
    public async Task<ActionResult<BildirimYaniti>> Bildirim(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        var isletme = _isletme.Zorunlu;
        var adres = TakipSayfasi.Adres(Request, _ayarlar, isletme, kayit.TakipKodu);
        var musteri = await MusteriyiOku(kayit, iptal);

        return new BildirimYaniti(MusteriMesaji.Olustur(kayit, isletme, adres), adres,
            MusteriMesaji.WhatsAppNumarasi(kayit.Musteri.Telefon), musteri?.Email);
    }

    [HttpPost("{id:oid}/bildirimler")]
    public async Task<IActionResult> BildirimiKaydet(string id, BildirimIstegi istek, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        var kanal = istek.Kanal switch
        {
            BildirimKanali.WhatsApp => "WhatsApp",
            BildirimKanali.Eposta => "e-posta",
            BildirimKanali.Telefon => "telefon",
            _ => "SMS"
        };
        await _gunluk.YazAsync(id, IslemTuru.MusteriBilgilendirildi,
            $"Müşteri {kanal} ile bilgilendirildi (durum: {kayit.Durum.Ad()})", iptal);
        return NoContent();
    }

    [HttpDelete("{id:oid}")]
    [Authorize(Roles = nameof(KullaniciRol.Admin))]
    public async Task<IActionResult> Sil(string id, CancellationToken iptal)
    {
        var kayit = await Bul(id, iptal);
        if (kayit is null) return Bulunamadi();

        // Belge silinmez, işaretlenir.
        if (ServisKaydiKurallari.Sil(kayit, DateTime.UtcNow) is { } hata)
            return KuralIhlali(hata);

        var sonuc = await Kaydet(kayit, IslemTuru.KayitSilindi, "Kayıt silindi", iptal);
        return sonuc.Result is ObjectResult cakisma ? cakisma : NoContent();
    }

    private Task<ServisKaydi?> Bul(string id, CancellationToken iptal) =>
        _db.ServisKayitlari.Find(s => s.Id == id && s.SilinmeTarihi == null).FirstOrDefaultAsync(iptal)!;

    // Adres ve e-posta kayıttaki kopyada yoktur; silinmiş olsa da müşteri belgesinden okunur.
    private Task<Musteri?> MusteriyiOku(ServisKaydi kayit, CancellationToken iptal) =>
        _db.Musteriler.Find(m => m.Id == kayit.MusteriId).FirstOrDefaultAsync(iptal)!;

    // İstekteki alanları kayda işler; müşteri ve teknisyenin güncel ad bilgisini kopyalar.
    private async Task<ObjectResult?> Uygula(ServisKaydi kayit, ServisKaydiIstegi istek, CancellationToken iptal)
    {
        var musteri = await _db.Musteriler
            .Find(m => m.Id == istek.MusteriId && m.SilinmeTarihi == null).FirstOrDefaultAsync(iptal);
        if (musteri is null)
            return Problem(detail: "Seçilen müşteri bulunamadı.", statusCode: StatusCodes.Status400BadRequest);

        var (teknisyen, atamaHatasi) = await PersonelAtamasi.Coz(_db, istek.TeknisyenId, kayit.TeknisyenId, iptal);
        if (atamaHatasi is not null)
            return Problem(detail: atamaHatasi, statusCode: StatusCodes.Status400BadRequest);

        kayit.MusteriId = musteri.Id;
        kayit.Musteri = musteri.OzetiniCikar();
        kayit.TeknisyenId = teknisyen?.Id;
        kayit.TeknisyenAdi = teknisyen?.AdSoyad;
        kayit.CihazBilgisi = istek.CihazBilgisi.Trim();
        kayit.Sorun = istek.Sorun.Trim();
        kayit.Marka = Metin.BosIseNull(istek.Marka);
        kayit.Model = Metin.BosIseNull(istek.Model);
        kayit.SeriNo = SeriNoyuNormalle(istek.SeriNo);
        kayit.GarantiKapsaminda = istek.GarantiKapsaminda;
        kayit.Aksesuarlar = Metin.BosIseNull(istek.Aksesuarlar);
        kayit.FizikselDurum = Metin.BosIseNull(istek.FizikselDurum);
        AramaMetniniYenile(kayit);
        return null;
    }

    private static void AramaMetniniYenile(ServisKaydi kayit) =>
        kayit.AramaMetni = AramaMetni.Olustur(kayit.No, kayit.CihazBilgisi, kayit.Marka, kayit.Model, kayit.SeriNo, kayit.Sorun);

    private static string? SeriNoyuNormalle(string? seriNo) => Metin.BosIseNull(seriNo)?.ToUpperInvariant();

    // Kaydı yazar ve başarılıysa işlem geçmişine satır ekler. Açıklama boşsa (hiçbir şey değişmediyse)
    // geçmişe yazılmaz.
    private async Task<ActionResult<ServisKaydiYaniti>> Kaydet(ServisKaydi kayit, IslemTuru tur, string? aciklama,
        CancellationToken iptal)
    {
        if (!await Yaz(kayit, iptal)) return Cakisma();

        if (aciklama is not null)
            await _gunluk.YazAsync(kayit.Id, tur, aciklama, iptal);

        return kayit.YanitaCevir();
    }

    // Kaydı yalnızca okunduğu sürümdeyse yazar; arada başkası değiştirdiyse false döner.
    private async Task<bool> Yaz(ServisKaydi kayit, CancellationToken iptal)
    {
        var okunanSurum = kayit.Surum;
        kayit.Surum++;

        var sonuc = await _db.ServisKayitlari.ReplaceOneAsync(
            s => s.Id == kayit.Id && s.Surum == okunanSurum, kayit, cancellationToken: iptal);
        return sonuc.MatchedCount > 0;
    }

    // Geçmişte "ne değişti" yazabilmek için kaydın düzenlenebilir alanları. Uzun metinlerin içeriği yazılmaz.
    private static (string Ad, string Deger, bool Uzun)[] Alanlar(ServisKaydi k) =>
    [
        ("Müşteri", k.Musteri.AdSoyad, false),
        ("Teknisyen", k.TeknisyenAdi ?? "atanmadı", false),
        ("Cihaz", k.CihazBilgisi, false),
        ("Marka", k.Marka ?? "—", false),
        ("Model", k.Model ?? "—", false),
        ("Seri no", k.SeriNo ?? "—", false),
        ("Garanti", k.GarantiKapsaminda ? "kapsamında" : "kapsam dışı", false),
        ("Sorun", k.Sorun, true),
        ("Aksesuarlar", k.Aksesuarlar ?? "", true),
        ("Fiziksel durum", k.FizikselDurum ?? "", true)
    ];

    private static string? DegisiklikOzeti((string Ad, string Deger, bool Uzun)[] once, (string Ad, string Deger, bool Uzun)[] sonra)
    {
        var degisenler = once.Zip(sonra)
            .Where(cift => cift.First.Deger != cift.Second.Deger)
            .Select(cift => cift.First.Uzun
                ? $"{cift.First.Ad} değişti"
                : $"{cift.First.Ad}: {cift.First.Deger} → {cift.Second.Deger}")
            .ToList();

        return degisenler.Count == 0 ? null : string.Join("; ", degisenler);
    }

    private static bool KimlikGecerli(string? kimlik) => kimlik is null || ObjectId.TryParse(kimlik, out _);

    private ObjectResult Bulunamadi() =>
        Problem(detail: "Servis kaydı bulunamadı.", statusCode: StatusCodes.Status404NotFound);

    private ObjectResult KuralIhlali(string mesaj) =>
        Problem(detail: mesaj, statusCode: StatusCodes.Status409Conflict);

    private ObjectResult Cakisma() =>
        Problem(detail: "Kayıt bu sırada başka biri tarafından değiştirildi. Yenileyip yeniden deneyin.",
            statusCode: StatusCodes.Status409Conflict);
}

public static class ServisKaydiDonusumleri
{
    public static ServisKaydiYaniti YanitaCevir(this ServisKaydi k) => new(
        k.Id, k.MusteriId, k.Musteri.AdSoyad, k.Musteri.Telefon, k.TeknisyenId, k.TeknisyenAdi,
        k.CihazBilgisi, k.Sorun, k.Durum, k.OlusturmaTarihi, k.TamamlanmaTarihi,
        k.Fatura is null
            ? null
            : new FaturaYaniti(k.Fatura.No, k.Fatura.Tutar, k.Fatura.OdendiMi, k.Fatura.OlusturmaTarihi, k.Fatura.OdemeTarihi),
        k.No, k.Marka, k.Model, k.SeriNo, k.GarantiKapsaminda, k.Aksesuarlar, k.FizikselDurum, k.YapilanIslem,
        k.TeslimTarihi,
        k.Teklif is null
            ? null
            : new TeklifYaniti(k.Teklif.Tutar, k.Teklif.Aciklama, k.Teklif.Durum, k.Teklif.Tarih, k.Teklif.YanitTarihi, k.Teklif.YanitNotu),
        k.Parcalar.Select(p => new KullanilanParcaYaniti(p.Id, p.ParcaId, p.Ad, p.Adet, p.BirimFiyat)).ToList(),
        k.TakipKodu);

    public static IslemYaniti YanitaCevir(this IslemKaydi i) => new(i.Id, i.Tarih, i.KullaniciAdi, i.Tur, i.Aciklama);
}
