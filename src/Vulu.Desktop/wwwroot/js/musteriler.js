// Müşteriler sayfası: liste, arama, kayıt ekleme/düzenleme, silme ve Excel'e aktarma.

let oturum = null;        // giriş yapan kişi (ortak.js'ten gelir)
let arama = "";           // arama kutusundaki metin
let sayfaNo = 1;          // listenin kaçıncı sayfası açık
let duzenlenen = null;    // düzenlenen müşteri; yeni kayıtta null
let silinecek = null;     // silme penceresinde gösterilen müşteri

const formPenceresi = new bootstrap.Modal(document.getElementById("form-penceresi"));
const silmePenceresi = new bootstrap.Modal(document.getElementById("silme-penceresi"));

async function listele() {
    const cevap = await kopruyeSor("musteri_listele", { arama: arama, sayfa: sayfaNo, boyut: 20 });
    if (!cevap.basarili) {
        hataGoster("liste-hatasi", cevap.hata);
        return;
    }
    hataGoster("liste-hatasi", "");

    const sayfa = cevap.veri;

    // Son sayfadaki tek kayıt silindiyse boş sayfada kalmamak için bir önceki sayfaya dönülür.
    if (sayfa.ogeler.length === 0 && sayfaNo > 1) {
        sayfaNo = sayfa.sayfaSayisi;
        await listele();
        return;
    }

    const satirlar = document.getElementById("satirlar");
    satirlar.innerHTML = "";

    for (const musteri of sayfa.ogeler) {
        const satir = document.createElement("tr");
        hucreEkle(satir, musteri.adSoyad);
        hucreEkle(satir, bosIseTire(musteri.telefon));
        hucreEkle(satir, bosIseTire(musteri.email));
        hucreEkle(satir, bosIseTire(musteri.adres));

        const islemler = hucreEkle(satir, "");
        islemler.className = "text-end text-nowrap";
        dugmeEkle(islemler, "Düzenle", "ikincil", function () { formuAc(musteri); });

        // Müşteriyi yalnızca yönetici silebilir (sunucu da aynı kuralı uygular).
        if (oturum.adminMi) {
            dugmeEkle(islemler, "Sil", "tehlike", function () { silmeyiAc(musteri); });
        }

        satirlar.appendChild(satir);
    }

    const bos = sayfa.ogeler.length === 0;
    document.getElementById("tablo").hidden = bos;
    document.getElementById("bos-mesaji").hidden = !bos;
    document.getElementById("bos-mesaji").textContent =
        arama === "" ? "Kayıtlı müşteri bulunmuyor." : "Aramayla eşleşen müşteri bulunamadı.";

    sayfalamayiCiz("sayfalama", sayfa, "kayıt", function (yeniSayfa) {
        sayfaNo = yeniSayfa;
        listele();
    });
}

// musteri verilirse düzenleme, verilmezse yeni kayıt penceresi açılır.
function formuAc(musteri) {
    duzenlenen = musteri;
    document.getElementById("form-basligi").textContent = musteri ? "Müşteriyi düzenle" : "Müşteri kaydı";
    document.getElementById("ad-soyad").value = musteri ? musteri.adSoyad : "";
    document.getElementById("telefon").value = musteri ? (musteri.telefon || "") : "";
    document.getElementById("email").value = musteri ? (musteri.email || "") : "";
    document.getElementById("adres").value = musteri ? (musteri.adres || "") : "";
    hataGoster("form-hatasi", "");
    formPenceresi.show();
}

async function kaydet(olay) {
    olay.preventDefault();
    const dugme = document.getElementById("kaydet-dugmesi");
    dugme.disabled = true;

    const cevap = await kopruyeSor("musteri_kaydet", {
        id: duzenlenen ? duzenlenen.id : "",
        adSoyad: document.getElementById("ad-soyad").value.trim(),
        telefon: document.getElementById("telefon").value.trim(),
        email: bosIseNull(document.getElementById("email").value),
        adres: bosIseNull(document.getElementById("adres").value)
    });

    dugme.disabled = false;
    if (!cevap.basarili) {
        hataGoster("form-hatasi", cevap.hata);
        return;
    }

    formPenceresi.hide();
    await listele();
}

function silmeyiAc(musteri) {
    silinecek = musteri;
    document.getElementById("silinecek-ad").textContent = musteri.adSoyad;
    hataGoster("silme-hatasi", "");
    silmePenceresi.show();
}

async function sil() {
    const dugme = document.getElementById("sil-dugmesi");
    dugme.disabled = true;
    const cevap = await kopruyeSor("musteri_sil", { id: silinecek.id });
    dugme.disabled = false;

    if (!cevap.basarili) {
        hataGoster("silme-hatasi", cevap.hata);
        return;
    }

    silmePenceresi.hide();
    await listele();
}

async function baslat() {
    oturum = await sayfayiHazirla();
    if (!oturum) return;

    // Müşteri listesini Excel'e yalnızca yönetici aktarabilir.
    document.getElementById("excel-dugmesi").hidden = !oturum.adminMi;
    excelDugmesiniBagla("excel-dugmesi", "excel-bilgisi", function () { return { liste: "musteriler" }; });

    document.getElementById("yeni-dugmesi").addEventListener("click", function () { formuAc(null); });
    document.getElementById("musteri-formu").addEventListener("submit", kaydet);
    document.getElementById("sil-dugmesi").addEventListener("click", sil);
    aramaKutusunuBagla("arama", function (metin) {
        arama = metin;
        sayfaNo = 1;
        listele();
    });

    await listele();
}

baslat();
