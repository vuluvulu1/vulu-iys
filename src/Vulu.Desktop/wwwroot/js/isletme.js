// İşletme sayfası (yalnızca yönetici): işletmenin adı, iletişim ve fatura bilgileri, servis koşulları ve logosu.

// Logo sınırları (sunucudaki IsletmeKurallari ile aynı; sunucu da ayrıca denetler).
const LogoKenari = 512;                       // küçültülen logonun en uzun kenarı (piksel)
const EnFazlaLogoBayt = 2 * 1024 * 1024;      // küçültülmüş logonun en büyük boyutu
const EnFazlaKaynakBayt = 25 * 1024 * 1024;   // seçilebilecek en büyük görsel

let mevcutLogo = null;        // sunucudaki logo: { veri, tur } ya da null
let yeniLogo = null;          // seçilip küçültülmüş, henüz kaydedilmemiş logo
let logoKaldirilacak = false;

function onizlemeyiGuncelle() {
    let adres = "img/logo.svg";
    if (yeniLogo) adres = "data:" + yeniLogo.tur + ";base64," + yeniLogo.veri;
    else if (mevcutLogo && !logoKaldirilacak) adres = "data:" + mevcutLogo.tur + ";base64," + mevcutLogo.veri;
    document.getElementById("logo-onizleme").src = adres;
    document.getElementById("logo-kaldir").hidden = !(yeniLogo || (mevcutLogo && !logoKaldirilacak));
}

function doldur(isletme) {
    mevcutLogo = isletme.logoVerisi ? { veri: isletme.logoVerisi, tur: isletme.logoTuru } : null;
    yeniLogo = null;
    logoKaldirilacak = false;

    document.getElementById("i-ad").value = isletme.ad;
    document.getElementById("i-kod").value = isletme.kod;
    document.getElementById("i-adres").value = isletme.adres || "";
    document.getElementById("i-telefon").value = isletme.telefon || "";
    document.getElementById("i-eposta").value = isletme.eposta || "";
    document.getElementById("i-vergi-dairesi").value = isletme.vergiDairesi || "";
    document.getElementById("i-vergi-no").value = isletme.vergiNo || "";
    document.getElementById("i-kdv").value = String(isletme.kdvOrani).replace(".", ",");
    document.getElementById("i-kosullar").value = isletme.servisKosullari || "";
    document.getElementById("i-logo").value = "";
    onizlemeyiGuncelle();
}

// Görseli, en uzun kenarı LogoKenari pikseli aşmayacak şekilde küçültür ve base64 olarak döner.
// tur: "image/png" (saydam zemini korur) ya da "image/jpeg" (fotoğraflarda çok daha küçük).
function kucult(gorsel, tur) {
    const oran = Math.min(1, LogoKenari / Math.max(gorsel.width, gorsel.height));
    const tuval = document.createElement("canvas");
    tuval.width = Math.round(gorsel.width * oran);
    tuval.height = Math.round(gorsel.height * oran);
    tuval.getContext("2d").drawImage(gorsel, 0, 0, tuval.width, tuval.height);

    const adres = tuval.toDataURL(tur, 0.9);            // "data:image/png;base64,...."
    return adres.substring(adres.indexOf(",") + 1);     // yalnızca base64 kısmı
}

// Seçilen görsel olduğu gibi gönderilmez: önce küçültülür. Böylece telefonla çekilmiş büyük bir fotoğraf da
// seçilebilir, sunucuya küçük bir dosya gider.
function logoSecildi(olay) {
    const dosya = olay.target.files[0];
    if (!dosya) return;
    hataGoster("hata", "");
    document.getElementById("kaydedildi").hidden = true;

    if (!dosya.type.startsWith("image/")) {
        hataGoster("hata", "Bir görsel dosyası seçin.");
        return;
    }
    if (dosya.size > EnFazlaKaynakBayt) {
        hataGoster("hata", "Görsel çok büyük; en fazla 25 MB olabilir.");
        return;
    }

    const adres = URL.createObjectURL(dosya);
    const gorsel = new Image();
    gorsel.onload = function () {
        let tur = "image/png";
        let veri = kucult(gorsel, tur);
        // base64 metni gerçek boyutun yaklaşık 4/3'üdür.
        if (veri.length * 3 / 4 > EnFazlaLogoBayt) {
            tur = "image/jpeg";
            veri = kucult(gorsel, tur);
        }
        URL.revokeObjectURL(adres);

        if (veri.length * 3 / 4 > EnFazlaLogoBayt) {
            hataGoster("hata", "Görsel işlenemedi; başka bir dosya deneyin.");
            return;
        }
        yeniLogo = { veri: veri, tur: tur };
        logoKaldirilacak = false;
        onizlemeyiGuncelle();
    };
    gorsel.onerror = function () {
        URL.revokeObjectURL(adres);
        hataGoster("hata", "Görsel okunamadı; başka bir dosya deneyin.");
    };
    gorsel.src = adres;
}

async function kaydet(olay) {
    olay.preventDefault();
    document.getElementById("kaydedildi").hidden = true;

    const kdv = paraOku(document.getElementById("i-kdv").value);
    if (kdv === null) {
        hataGoster("hata", "KDV oranı sayı olmalı (örn. 20).");
        return;
    }

    const dugme = document.getElementById("kaydet-dugmesi");
    dugme.disabled = true;
    dugme.textContent = "Kaydediliyor…";

    const cevap = await kopruyeSor("isletme_kaydet", {
        ad: document.getElementById("i-ad").value.trim(),
        adres: bosIseNull(document.getElementById("i-adres").value),
        telefon: bosIseNull(document.getElementById("i-telefon").value),
        eposta: bosIseNull(document.getElementById("i-eposta").value),
        vergiDairesi: bosIseNull(document.getElementById("i-vergi-dairesi").value),
        vergiNo: bosIseNull(document.getElementById("i-vergi-no").value),
        kdvOrani: kdv,
        logoVerisi: yeniLogo ? yeniLogo.veri : null,
        logoTuru: yeniLogo ? yeniLogo.tur : null,
        logoyuKaldir: logoKaldirilacak,
        servisKosullari: bosIseNull(document.getElementById("i-kosullar").value)
    });

    dugme.disabled = false;
    dugme.textContent = "Kaydet";
    if (!cevap.basarili) {
        hataGoster("hata", cevap.hata);
        return;
    }

    hataGoster("hata", "");
    doldur(cevap.veri);
    document.getElementById("kaydedildi").hidden = false;
    // Yan menüdeki ad ve logo da yenilensin.
    await sayfayiHazirla();
}

async function baslat() {
    const oturum = await sayfayiHazirla();
    if (!oturum) return;

    const cevap = await kopruyeSor("isletme_getir");
    if (!cevap.basarili) {
        document.getElementById("yukleniyor").textContent = cevap.hata;
        return;
    }

    doldur(cevap.veri);
    document.getElementById("yukleniyor").hidden = true;
    document.getElementById("isletme-formu").hidden = false;

    document.getElementById("i-logo").addEventListener("change", logoSecildi);
    document.getElementById("logo-kaldir").addEventListener("click", function () {
        yeniLogo = null;
        logoKaldirilacak = true;
        document.getElementById("i-logo").value = "";
        onizlemeyiGuncelle();
    });
    document.getElementById("isletme-formu").addEventListener("submit", kaydet);
}

baslat();
