// Servis kayıtları sayfası: liste ve süzgeçler, kayıt açma/düzenleme formu ve dört sekmeli ayrıntı penceresi
// (genel, teklif ve parçalar, fatura, geçmiş).

let oturum = null;
let arama = "";
let durumSuzgeci = "";
let faturaSuzgeci = "Hepsi";
let sayfaNo = 1;

let acik = null;           // ayrıntı penceresinde açık olan kayıt
let duzenlenen = null;     // formda düzenlenen kayıt; yeni kayıtta null
let silmeOnayi = false;    // "Kaydı sil" iki adımlıdır: ilk tıklama onay ister
let faturaSilmeOnayi = false;

const formPenceresi = new bootstrap.Modal(document.getElementById("form-penceresi"));
const detayPenceresi = new bootstrap.Modal(document.getElementById("detay-penceresi"));
const musteriSecici = musteriSeciciKur("musteri-secici", null);

// İşlem geçmişindeki satır türlerinin ekrandaki adları.
const islemAdlari = {
    KayitAcildi: "Kayıt açıldı",
    KayitGuncellendi: "Güncellendi",
    DurumDegisti: "Durum",
    Not: "Not",
    FaturaKesildi: "Fatura kesildi",
    FaturaGuncellendi: "Fatura güncellendi",
    FaturaSilindi: "Fatura silindi",
    KayitSilindi: "Silindi",
    TeklifVerildi: "Teklif",
    TeklifYanitlandi: "Teklif yanıtı",
    ParcaEklendi: "Parça eklendi",
    ParcaCikarildi: "Parça çıkarıldı",
    MusteriBilgilendirildi: "Bildirim"
};

const teklifDurumlari = {
    Bekliyor: "Yanıt bekleniyor",
    Onaylandi: "Onaylandı",
    Reddedildi: "Reddedildi"
};

// İş henüz bitmemiş ve iptal edilmemiş.
function acikMi(durum) {
    return durum === "Beklemede" || durum === "OnayBekliyor" || durum === "DevamEdiyor";
}

// İş bitmiş; cihaz teslim edilmiş ya da teslim edilmeyi bekliyor.
function bittiMi(durum) {
    return durum === "Tamamlandi" || durum === "TeslimEdildi";
}

function parcaToplami(kayit) {
    let toplam = 0;
    for (const parca of kayit.parcalar) toplam += parca.adet * parca.birimFiyat;
    return toplam;
}

// --- Liste ---

// Tablo hücresine iki satır yazar: üstte asıl bilgi, altta soluk ek bilgi.
function ikiSatirliHucre(satir, ust, alt) {
    const hucre = hucreEkle(satir, ust);
    const altSatir = document.createElement("div");
    altSatir.className = "small text-secondary";
    altSatir.textContent = alt;
    hucre.appendChild(altSatir);
    return hucre;
}

async function listele() {
    const cevap = await kopruyeSor("servis_listele", {
        arama: arama, durum: durumSuzgeci, fatura: faturaSuzgeci, sayfa: sayfaNo, boyut: 20
    });
    if (!cevap.basarili) {
        hataGoster("liste-hatasi", cevap.hata);
        return;
    }
    hataGoster("liste-hatasi", "");

    const sayfa = cevap.veri;
    if (sayfa.ogeler.length === 0 && sayfaNo > 1) {
        sayfaNo = sayfa.sayfaSayisi;
        await listele();
        return;
    }

    const satirlar = document.getElementById("satirlar");
    satirlar.innerHTML = "";

    for (const kayit of sayfa.ogeler) {
        const satir = document.createElement("tr");
        ikiSatirliHucre(satir, kayit.no, tarihYaz(kayit.olusturmaTarihi));
        ikiSatirliHucre(satir, kayit.musteriAdi, kayit.musteriTelefonu || "");

        const cihaz = hucreEkle(satir, kayit.cihazAdi);
        if (kayit.garantiKapsaminda) {
            const rozet = document.createElement("span");
            rozet.className = "badge text-bg-secondary ms-2";
            rozet.textContent = "garantili";
            cihaz.appendChild(rozet);
        }

        hucreEkle(satir, bosIseTire(kayit.teknisyenAdi));
        hucreEkle(satir, "").appendChild(durumEtiketi(servisDurumlari, kayit.durum));

        if (kayit.fatura) {
            const hucre = hucreEkle(satir, paraYaz(kayit.fatura.tutar));
            const odeme = document.createElement("div");
            odeme.className = kayit.fatura.odendiMi ? "small text-success" : "small text-danger";
            odeme.textContent = kayit.fatura.odendiMi ? "Ödendi" : "Ödenmedi";
            hucre.appendChild(odeme);
        } else {
            hucreEkle(satir, "—");
        }

        const islemler = hucreEkle(satir, "");
        islemler.className = "text-end";
        dugmeEkle(islemler, "Aç", "ikincil", function () { detayiAc(kayit); });

        satirlar.appendChild(satir);
    }

    const bos = sayfa.ogeler.length === 0;
    const suzgecVar = arama !== "" || durumSuzgeci !== "" || faturaSuzgeci !== "Hepsi";
    document.getElementById("tablo").hidden = bos;
    document.getElementById("bos-mesaji").hidden = !bos;
    document.getElementById("bos-mesaji").textContent =
        suzgecVar ? "Süzgeçle eşleşen servis kaydı bulunamadı." : "Kayıtlı servis kaydı bulunmuyor.";

    sayfalamayiCiz("sayfalama", sayfa, "kayıt", function (yeniSayfa) {
        sayfaNo = yeniSayfa;
        listele();
    });
}

// --- Açma / düzenleme formu ---

function formuAc(kayit) {
    duzenlenen = kayit;
    document.getElementById("form-basligi").textContent = kayit ? "Servis kaydını düzenle – " + kayit.no : "Servis kaydı";

    // Seçici yalnızca ad ve telefonu gösterir; kayıttaki kopya bunun için yeterlidir.
    musteriSecici.ayarla(kayit ? { id: kayit.musteriId, adSoyad: kayit.musteriAdi, telefon: kayit.musteriTelefonu } : null);
    personelSeciminiDoldur("s-teknisyen", kayit ? kayit.teknisyenId : null, kayit ? kayit.teknisyenAdi : null);

    document.getElementById("s-cihaz").value = kayit ? kayit.cihazBilgisi : "";
    document.getElementById("s-marka").value = kayit ? (kayit.marka || "") : "";
    document.getElementById("s-model").value = kayit ? (kayit.model || "") : "";
    document.getElementById("s-seri").value = kayit ? (kayit.seriNo || "") : "";
    document.getElementById("s-garanti").checked = kayit ? kayit.garantiKapsaminda : false;
    document.getElementById("s-aksesuar").value = kayit ? (kayit.aksesuarlar || "") : "";
    document.getElementById("s-fiziksel").value = kayit ? (kayit.fizikselDurum || "") : "";
    document.getElementById("s-sorun").value = kayit ? kayit.sorun : "";
    hataGoster("form-hatasi", "");

    detayPenceresi.hide();
    formPenceresi.show();
}

async function kaydet(olay) {
    olay.preventDefault();

    const musteri = musteriSecici.secili();
    if (!musteri) {
        hataGoster("form-hatasi", "Müşteri seçilmeli.");
        return;
    }

    const dugme = document.getElementById("kaydet-dugmesi");
    dugme.disabled = true;
    const cevap = await kopruyeSor("servis_kaydet", {
        id: duzenlenen ? duzenlenen.id : "",
        musteriId: musteri.id,
        teknisyenId: bosIseNull(document.getElementById("s-teknisyen").value),
        cihazBilgisi: document.getElementById("s-cihaz").value.trim(),
        sorun: document.getElementById("s-sorun").value.trim(),
        marka: bosIseNull(document.getElementById("s-marka").value),
        model: bosIseNull(document.getElementById("s-model").value),
        seriNo: bosIseNull(document.getElementById("s-seri").value),
        garantiKapsaminda: document.getElementById("s-garanti").checked,
        aksesuarlar: bosIseNull(document.getElementById("s-aksesuar").value),
        fizikselDurum: bosIseNull(document.getElementById("s-fiziksel").value)
    });
    dugme.disabled = false;

    if (!cevap.basarili) {
        hataGoster("form-hatasi", cevap.hata);
        return;
    }

    // Kaydedilen kayıt ayrıntı penceresinde açılır; durum, teklif ve fatura oradan yönetilir.
    formPenceresi.hide();
    detayiAc(cevap.veri);
    await listele();
}

// --- Ayrıntı penceresi ---

// Tanım listesine (dl) bir "başlık: değer" satırı ekler. deger metin ya da hazır bir öğe olabilir.
function bilgiEkle(liste, baslik, deger) {
    const dt = document.createElement("dt");
    dt.className = "col-sm-3 text-secondary fw-normal";
    dt.textContent = baslik;
    const dd = document.createElement("dd");
    dd.className = "col-sm-9 cok-satirli";
    if (typeof deger === "string" || typeof deger === "number") dd.textContent = deger;
    else dd.appendChild(deger);
    liste.appendChild(dt);
    liste.appendChild(dd);
    return dd;
}

function detayiAc(kayit) {
    acik = kayit;
    silmeOnayi = false;
    faturaSilmeOnayi = false;

    // Pencere her açılışta "Genel" sekmesiyle başlar.
    bootstrap.Tab.getOrCreateInstance(document.querySelector("[data-bs-target='#sekme-genel']")).show();

    document.getElementById("d-islem").value = kayit.yapilanIslem || "";
    document.getElementById("bildirim-metni").hidden = true;
    document.getElementById("not-metni").value = "";
    document.getElementById("teklif-tutari").value = "";
    document.getElementById("teklif-aciklamasi").value = "";
    document.getElementById("yanit-notu").value = "";
    document.getElementById("parca-arama").value = "";
    hataGoster("detay-hatasi", "");

    detayiCiz();
    faturaAlanlariniDoldur();
    ayniCihaziGoster();
    parcalariYukle("");
    gecmisiYukle();
    detayPenceresi.show();
}

// Açık kaydın bütün sekmelerini, kaydın son haline göre yeniden çizer.
function detayiCiz() {
    const kayit = acik;
    document.getElementById("detay-basligi").textContent = "Servis kaydı " + kayit.no;

    // Genel bilgiler
    const genel = document.getElementById("genel-bilgiler");
    genel.innerHTML = "";
    bilgiEkle(genel, "Müşteri", kayit.musteriAdi + "  " + (kayit.musteriTelefonu || ""));
    bilgiEkle(genel, "Cihaz", kayit.cihazAdi + (kayit.garantiKapsaminda ? " (garantili)" : ""));
    if (kayit.seriNo) bilgiEkle(genel, "Seri no / IMEI", kayit.seriNo);
    if (kayit.aksesuarlar) bilgiEkle(genel, "Aksesuarlar", kayit.aksesuarlar);
    if (kayit.fizikselDurum) bilgiEkle(genel, "Teslim alındığında", kayit.fizikselDurum);
    bilgiEkle(genel, "Sorun", kayit.sorun);
    bilgiEkle(genel, "Teknisyen", kayit.teknisyenAdi || "Atanmadı");
    bilgiEkle(genel, "Açılış", tarihYaz(kayit.olusturmaTarihi));
    if (kayit.tamamlanmaTarihi) bilgiEkle(genel, "Tamamlanma", tarihYaz(kayit.tamamlanmaTarihi));
    if (kayit.teslimTarihi) bilgiEkle(genel, "Teslim", tarihYaz(kayit.teslimTarihi));

    // Durum düğmeleri. Faturası olan kayıt yeniden açılamaz ya da iptal edilemez.
    const durumlar = document.getElementById("durum-dugmeleri");
    durumlar.innerHTML = "";
    for (const durum in servisDurumlari) {
        const dugme = dugmeEkle(durumlar, servisDurumlari[durum].ad, durum === kayit.durum ? "ana" : "ikincil", function () {
            if (durum !== acik.durum) kayitDegistir("servis_durum", { durum: durum });
        });
        dugme.disabled = kayit.fatura !== null && kayit.fatura !== undefined && !bittiMi(durum);
    }
    document.getElementById("durum-notu").hidden = !kayit.fatura;

    // Belgeler ve bilgilendirme
    document.getElementById("teslim-formu").disabled = acikMi(kayit.durum);
    document.getElementById("teslim-notu").hidden = !acikMi(kayit.durum);
    document.getElementById("takip-kodu").textContent = kayit.takipKodu;

    teklifiCiz(kayit);
    parcalariCiz(kayit);
    faturayiCiz(kayit);

    // Kaydı yalnızca yönetici silebilir.
    const silDugmesi = document.getElementById("kayit-sil");
    silDugmesi.hidden = !oturum.adminMi;
    silDugmesi.textContent = silmeOnayi ? "Silmeyi onayla" : "Kaydı sil";
}

function teklifiCiz(kayit) {
    const bilgiler = document.getElementById("teklif-bilgileri");
    bilgiler.innerHTML = "";
    const teklif = kayit.teklif;

    if (teklif) {
        bilgiEkle(bilgiler, "Tutar", paraYaz(teklif.tutar) + " (KDV dahil)");
        if (teklif.aciklama) bilgiEkle(bilgiler, "Açıklama", teklif.aciklama);
        bilgiEkle(bilgiler, "Verildi", tarihYaz(teklif.tarih));
        let yanit = teklifDurumlari[teklif.durum];
        if (teklif.yanitTarihi) yanit += "  " + tarihYaz(teklif.yanitTarihi);
        if (teklif.yanitNotu) yanit += " – " + teklif.yanitNotu;
        bilgiEkle(bilgiler, "Yanıt", yanit);
    }

    const isAcik = acikMi(kayit.durum);
    document.getElementById("teklif-yanit-kutusu").hidden = !(teklif && teklif.durum === "Bekliyor" && isAcik);
    document.getElementById("teklif-ver-kutusu").hidden = !isAcik;
    document.getElementById("teklif-ver").textContent = teklif ? "Yeni teklif ver" : "Teklif ver";
    document.getElementById("teklif-yok").hidden = isAcik || !!teklif;
}

function parcalariCiz(kayit) {
    const satirlar = document.getElementById("parca-satirlari");
    satirlar.innerHTML = "";

    for (const parca of kayit.parcalar) {
        const satir = document.createElement("tr");
        hucreEkle(satir, parca.ad);
        hucreEkle(satir, parca.adet);
        hucreEkle(satir, paraYaz(parca.birimFiyat));
        hucreEkle(satir, paraYaz(parca.adet * parca.birimFiyat));
        const islem = hucreEkle(satir, "");
        islem.className = "text-end";
        const cikar = dugmeEkle(islem, "Çıkar", "ikincil", async function () {
            if (await kayitDegistir("servis_parca_cikar", { satirId: parca.id })) parcalariYukle(document.getElementById("parca-arama").value);
        });
        cikar.disabled = !!kayit.fatura;
        satirlar.appendChild(satir);
    }

    const varMi = kayit.parcalar.length > 0;
    document.getElementById("parca-tablosu").hidden = !varMi;
    document.getElementById("parca-yok").hidden = varMi;
    document.getElementById("parca-toplami").textContent = paraYaz(parcaToplami(kayit));
    document.getElementById("parca-kilitli").hidden = !kayit.fatura;
    document.getElementById("parca-ekle-kutusu").hidden = !!kayit.fatura || kayit.durum === "IptalEdildi";
}

function faturayiCiz(kayit) {
    const fatura = kayit.fatura;
    const kesilebilir = fatura || bittiMi(kayit.durum);
    document.getElementById("fatura-once-tamamla").hidden = kesilebilir;
    document.getElementById("fatura-kutusu").hidden = !kesilebilir;

    const bilgiler = document.getElementById("fatura-bilgileri");
    bilgiler.innerHTML = "";
    if (fatura) {
        bilgiEkle(bilgiler, "Fatura no", fatura.no);
        bilgiEkle(bilgiler, "Tutar", paraYaz(fatura.tutar));
        bilgiEkle(bilgiler, "Ödeme", fatura.odemeTarihi ? "Ödendi  " + tarihYaz(fatura.odemeTarihi) : "Ödenmedi");
        bilgiEkle(bilgiler, "Kesildi", tarihYaz(fatura.olusturmaTarihi));
    }

    document.getElementById("fatura-kaydet").textContent = fatura ? "Faturayı güncelle" : "Fatura kes";
    document.getElementById("fatura-pdf").hidden = !fatura;
    document.getElementById("fatura-sil").hidden = !fatura || !oturum.adminMi;
    document.getElementById("fatura-sil").textContent = faturaSilmeOnayi ? "Silmeyi onayla" : "Faturayı sil";

    let not = "Tutar KDV dahil toplamdır ve parçaları da kapsar.";
    if (kayit.parcalar.length > 0) {
        not += " Parça toplamı: " + paraYaz(parcaToplami(kayit)) + "; kalan tutar faturada işçilik olarak görünür.";
    }
    if (kayit.teklif && kayit.teklif.durum === "Onaylandi") {
        not += " Müşterinin onayladığı teklif: " + paraYaz(kayit.teklif.tutar) + ".";
    }
    document.getElementById("fatura-notu").textContent = not;
}

// Fatura kutularını kaydın faturasıyla doldurur. Fatura yoksa tutar, onaylanan teklifle ya da parça toplamıyla önerilir.
function faturaAlanlariniDoldur() {
    const kayit = acik;
    let tutar = "";
    if (kayit.fatura) tutar = paraGirdisi(kayit.fatura.tutar);
    else if (kayit.teklif && kayit.teklif.durum === "Onaylandi") tutar = paraGirdisi(kayit.teklif.tutar);
    else if (parcaToplami(kayit) > 0) tutar = paraGirdisi(parcaToplami(kayit));

    document.getElementById("fatura-tutari").value = tutar;
    document.getElementById("fatura-odendi").checked = kayit.fatura ? kayit.fatura.odendiMi : false;
}

// Kaydı değiştiren bir istek gönderir. Sunucu kaydın güncel halini döner; pencere ve liste yenilenir.
// Başarılıysa true döner.
async function kayitDegistir(islem, veri) {
    veri.id = acik.id;
    hataGoster("detay-hatasi", "");

    const cevap = await kopruyeSor(islem, veri);
    if (!cevap.basarili) {
        hataGoster("detay-hatasi", cevap.hata);
        return false;
    }

    acik = cevap.veri;
    detayiCiz();
    listele();
    return true;
}

// Aynı seri numaralı diğer kayıtlar: cihaz daha önce de gelmiş mi?
async function ayniCihaziGoster() {
    const kayit = acik;
    if (!kayit.seriNo) return;

    const cevap = await kopruyeSor("servis_listele", { seriNo: kayit.seriNo, boyut: 20 });
    if (!cevap.basarili || acik !== kayit) return;

    const oncekiler = cevap.veri.ogeler.filter(function (k) { return k.id !== kayit.id; });
    if (oncekiler.length === 0) return;

    const liste = document.createElement("ul");
    liste.className = "list-unstyled mb-0";
    for (const onceki of oncekiler) {
        const satir = document.createElement("li");
        satir.textContent = onceki.no + "  " + tarihYaz(onceki.olusturmaTarihi) + "  ";
        satir.appendChild(durumEtiketi(servisDurumlari, onceki.durum));
        satir.append("  " + onceki.sorun);
        liste.appendChild(satir);
    }
    const kutu = document.createElement("div");
    kutu.textContent = "Bu cihaz daha önce " + oncekiler.length + " kez geldi:";
    kutu.appendChild(liste);
    bilgiEkle(document.getElementById("genel-bilgiler"), "Önceki kayıtlar", kutu);
}

// Stoktaki parçalar (parça ekleme listesi).
async function parcalariYukle(aramaMetni) {
    const liste = document.getElementById("parca-secimi");
    const cevap = await kopruyeSor("parca_listele", { arama: aramaMetni, boyut: 50 });
    if (!cevap.basarili) return;

    liste.innerHTML = "";
    const ilk = document.createElement("option");
    ilk.value = "";
    ilk.textContent = cevap.veri.ogeler.length === 0 ? "Parça bulunamadı" : "Parça seçin";
    liste.appendChild(ilk);

    for (const parca of cevap.veri.ogeler) {
        const secenek = document.createElement("option");
        secenek.value = parca.id;
        secenek.textContent = parca.ad + " – " + paraYaz(parca.birimFiyat) + " (stok: " + parca.stokAdedi + ")";
        secenek.disabled = parca.stokAdedi <= 0;
        liste.appendChild(secenek);
    }
}

async function gecmisiYukle() {
    const liste = document.getElementById("gecmis");
    liste.textContent = "Yükleniyor…";
    const cevap = await kopruyeSor("servis_gecmis", { id: acik.id });
    if (cevap.basarili) gecmisiCiz(cevap.veri);
    else liste.textContent = cevap.hata;
}

function gecmisiCiz(islemler) {
    const liste = document.getElementById("gecmis");
    liste.innerHTML = "";
    for (const islem of islemler) {
        const satir = document.createElement("li");
        satir.className = "d-flex gap-3 py-1 border-bottom";

        const tarih = document.createElement("span");
        tarih.className = "text-secondary text-nowrap";
        tarih.textContent = tarihYaz(islem.tarih);

        const metin = document.createElement("span");
        const tur = document.createElement("strong");
        tur.textContent = (islemAdlari[islem.tur] || islem.tur) + " ";
        metin.appendChild(tur);
        metin.append(islem.aciklama);
        const kim = document.createElement("span");
        kim.className = "text-secondary";
        kim.textContent = " – " + islem.kullaniciAdi;
        metin.appendChild(kim);

        satir.appendChild(tarih);
        satir.appendChild(metin);
        liste.appendChild(satir);
    }
}

// --- Ayrıntı penceresindeki düğmeler ---

async function belgeAc(belge) {
    hataGoster("detay-hatasi", "");
    // Fatura belgesinin adı fatura numarasıyla, diğerleri kayıt numarasıyla kaydedilir.
    const no = belge === "fatura" ? acik.fatura.no : acik.no;
    const cevap = await kopruyeSor("servis_belge", { id: acik.id, belge: belge, no: no });
    if (!cevap.basarili) hataGoster("detay-hatasi", cevap.hata);
}

async function bilgilendir(kanal) {
    hataGoster("detay-hatasi", "");
    const cevap = await kopruyeSor("servis_bilgilendir", { id: acik.id, kanal: kanal, no: acik.no });
    if (!cevap.basarili) {
        hataGoster("detay-hatasi", cevap.hata);
        return;
    }
    const metin = document.getElementById("bildirim-metni");
    metin.value = cevap.veri.metin;
    metin.hidden = false;
    gecmisiYukle();
}

async function teklifVer() {
    const tutar = paraOku(document.getElementById("teklif-tutari").value);
    if (tutar === null || tutar <= 0) {
        hataGoster("detay-hatasi", "Geçerli bir teklif tutarı girin (örn. 1500).");
        return;
    }
    const tamam = await kayitDegistir("servis_teklif", {
        tutar: tutar,
        aciklama: bosIseNull(document.getElementById("teklif-aciklamasi").value)
    });
    if (tamam) {
        document.getElementById("teklif-tutari").value = "";
        document.getElementById("teklif-aciklamasi").value = "";
    }
}

async function teklifiYanitla(onaylandi) {
    const tamam = await kayitDegistir("servis_teklif_yanit", {
        onaylandi: onaylandi,
        not: bosIseNull(document.getElementById("yanit-notu").value)
    });
    if (tamam) {
        document.getElementById("yanit-notu").value = "";
        faturaAlanlariniDoldur();
    }
}

async function parcaEkle() {
    const parcaId = document.getElementById("parca-secimi").value;
    const adet = Number(document.getElementById("parca-adedi").value || 0);
    if (parcaId === "") {
        hataGoster("detay-hatasi", "Eklenecek parçayı seçin.");
        return;
    }
    if (adet < 1) {
        hataGoster("detay-hatasi", "Adet en az 1 olmalı.");
        return;
    }

    if (await kayitDegistir("servis_parca_ekle", { parcaId: parcaId, adet: adet })) {
        document.getElementById("parca-adedi").value = 1;
    }
    // Stok sayıları değişti; istek başarısız olduysa da güncel stok görünsün.
    parcalariYukle(document.getElementById("parca-arama").value);
}

async function faturayiKaydet() {
    const tutar = paraOku(document.getElementById("fatura-tutari").value);
    if (tutar === null || tutar <= 0) {
        hataGoster("detay-hatasi", "Geçerli bir tutar girin (örn. 750,50).");
        return;
    }
    if (await kayitDegistir("servis_fatura", { tutar: tutar, odendiMi: document.getElementById("fatura-odendi").checked })) {
        faturaAlanlariniDoldur();
    }
}

async function faturayiSil() {
    // İki adımlı: ilk tıklama onay ister, ikincisi siler.
    if (!faturaSilmeOnayi) {
        faturaSilmeOnayi = true;
        detayiCiz();
        return;
    }
    faturaSilmeOnayi = false;
    if (await kayitDegistir("servis_fatura_sil", {})) faturaAlanlariniDoldur();
    else detayiCiz();
}

async function kaydiSil() {
    if (!silmeOnayi) {
        silmeOnayi = true;
        detayiCiz();
        return;
    }
    silmeOnayi = false;

    const cevap = await kopruyeSor("servis_sil", { id: acik.id });
    if (!cevap.basarili) {
        hataGoster("detay-hatasi", cevap.hata);
        detayiCiz();
        return;
    }
    detayPenceresi.hide();
    await listele();
}

async function notEkle() {
    const metin = document.getElementById("not-metni").value.trim();
    if (metin.length < 2) {
        hataGoster("detay-hatasi", "Not en az 2 karakter olmalı.");
        return;
    }
    // Sunucu, yeni notu da içeren güncel geçmişi döner.
    const cevap = await kopruyeSor("servis_not", { id: acik.id, metin: metin });
    if (!cevap.basarili) {
        hataGoster("detay-hatasi", cevap.hata);
        return;
    }
    hataGoster("detay-hatasi", "");
    document.getElementById("not-metni").value = "";
    gecmisiCiz(cevap.veri);
}

// --- Başlangıç ---

async function baslat() {
    oturum = await sayfayiHazirla();
    if (!oturum) return;

    // Liste ekranda hangi süzgeçle görünüyorsa Excel'e o haliyle aktarılır; yalnızca yönetici aktarabilir.
    document.getElementById("excel-dugmesi").hidden = !oturum.adminMi;
    excelDugmesiniBagla("excel-dugmesi", "excel-bilgisi", function () {
        return { liste: "servis", arama: arama, durum: durumSuzgeci, fatura: faturaSuzgeci };
    });

    document.getElementById("yeni-dugmesi").addEventListener("click", function () { formuAc(null); });
    document.getElementById("kayit-formu").addEventListener("submit", kaydet);
    document.getElementById("durum-suzgeci").addEventListener("change", function (olay) {
        durumSuzgeci = olay.target.value;
        sayfaNo = 1;
        listele();
    });
    document.getElementById("fatura-suzgeci").addEventListener("change", function (olay) {
        faturaSuzgeci = olay.target.value;
        sayfaNo = 1;
        listele();
    });
    aramaKutusunuBagla("arama", function (metin) {
        arama = metin;
        sayfaNo = 1;
        listele();
    });

    // Ayrıntı penceresi
    document.getElementById("islem-kaydet").addEventListener("click", function () {
        kayitDegistir("servis_yapilan_islem", { metin: document.getElementById("d-islem").value.trim() });
    });
    document.getElementById("kabul-formu").addEventListener("click", function () { belgeAc("kabul"); });
    document.getElementById("teslim-formu").addEventListener("click", function () { belgeAc("teslim"); });
    document.getElementById("fatura-pdf").addEventListener("click", function () { belgeAc("fatura"); });
    for (const dugme of document.querySelectorAll(".bildirim")) {
        dugme.addEventListener("click", function () { bilgilendir(dugme.dataset.kanal); });
    }
    document.getElementById("teklif-ver").addEventListener("click", teklifVer);
    document.getElementById("teklif-onay").addEventListener("click", function () { teklifiYanitla(true); });
    document.getElementById("teklif-red").addEventListener("click", function () { teklifiYanitla(false); });
    document.getElementById("parca-ekle").addEventListener("click", parcaEkle);
    aramaKutusunuBagla("parca-arama", parcalariYukle);
    document.getElementById("fatura-kaydet").addEventListener("click", faturayiKaydet);
    document.getElementById("fatura-sil").addEventListener("click", faturayiSil);
    document.getElementById("kayit-sil").addEventListener("click", kaydiSil);
    document.getElementById("not-ekle").addEventListener("click", notEkle);
    document.getElementById("duzenle-dugmesi").addEventListener("click", function () { formuAc(acik); });

    await listele();
}

baslat();
