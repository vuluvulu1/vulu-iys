// Vulu yöneticisi hesapları: liste, ekleme/düzenleme ve silme.

let oturum = null;
let duzenlenen = null;
let silinecek = null;

const formPenceresi = new bootstrap.Modal(document.getElementById("form-penceresi"));
const silmePenceresi = new bootstrap.Modal(document.getElementById("silme-penceresi"));

async function listele() {
    const cevap = await kopruyeSor("vulu_yoneticiler");
    if (!cevap.basarili) {
        hataGoster("liste-hatasi", cevap.hata);
        return;
    }
    hataGoster("liste-hatasi", "");

    const satirlar = document.getElementById("satirlar");
    satirlar.innerHTML = "";
    for (const yonetici of cevap.veri) {
        const satir = document.createElement("tr");
        const benMiyim = yonetici.id === oturum.kimlik;

        const ad = hucreEkle(satir, yonetici.adSoyad);
        if (benMiyim) {
            const rozet = document.createElement("span");
            rozet.className = "badge text-bg-secondary ms-2";
            rozet.textContent = "siz";
            ad.appendChild(rozet);
        }
        hucreEkle(satir, yonetici.kullaniciAdi);
        const durum = hucreEkle(satir, yonetici.aktifMi ? "Aktif" : "Devre dışı");
        durum.className = yonetici.aktifMi ? "text-success" : "text-danger";

        const islemler = hucreEkle(satir, "");
        islemler.className = "text-end text-nowrap";
        dugmeEkle(islemler, "Düzenle", "ikincil", function () { formuAc(yonetici); });
        const sil = dugmeEkle(islemler, "Sil", "tehlike", function () { silmeyiAc(yonetici); });
        sil.disabled = benMiyim;

        satirlar.appendChild(satir);
    }
}

function formuAc(yonetici) {
    duzenlenen = yonetici;
    document.getElementById("form-basligi").textContent = yonetici ? "Vulu yöneticisini düzenle" : "Vulu yöneticisi kaydı";
    document.getElementById("v-adsoyad").value = yonetici ? yonetici.adSoyad : "";
    document.getElementById("v-ad").value = yonetici ? yonetici.kullaniciAdi : "";
    // Kullanıcı adı açıldıktan sonra değiştirilemez.
    document.getElementById("v-ad").disabled = yonetici !== null;
    document.getElementById("v-sifre").value = "";
    document.getElementById("sifre-etiketi").textContent = yonetici ? "Yeni şifre (boşsa değişmez)" : "Şifre";
    document.getElementById("v-aktif").checked = yonetici ? yonetici.aktifMi : true;
    document.getElementById("aktif-kutusu").hidden = yonetici === null;
    hataGoster("form-hatasi", "");
    formPenceresi.show();
}

async function kaydet(olay) {
    olay.preventDefault();
    const sifre = document.getElementById("v-sifre").value;
    const adSoyad = document.getElementById("v-adsoyad").value.trim();

    let veri;
    if (duzenlenen) {
        veri = { id: duzenlenen.id, adSoyad: adSoyad, aktifMi: document.getElementById("v-aktif").checked, yeniSifre: sifre === "" ? null : sifre };
    } else {
        veri = { kullaniciAdi: document.getElementById("v-ad").value.trim(), sifre: sifre, adSoyad: adSoyad };
    }

    const cevap = await kopruyeSor("vulu_yonetici_kaydet", veri);
    document.getElementById("v-sifre").value = "";
    if (!cevap.basarili) {
        hataGoster("form-hatasi", cevap.hata);
        return;
    }
    formPenceresi.hide();
    await listele();
}

function silmeyiAc(yonetici) {
    silinecek = yonetici;
    document.getElementById("silme-metni").textContent =
        yonetici.adSoyad + " (" + yonetici.kullaniciAdi + ") artık işletmeleri yönetemeyecek. Bu işlem geri alınamaz.";
    hataGoster("silme-hatasi", "");
    silmePenceresi.show();
}

async function sil() {
    const cevap = await kopruyeSor("vulu_yonetici_sil", { id: silinecek.id });
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

    document.getElementById("yeni-dugmesi").addEventListener("click", function () { formuAc(null); });
    document.getElementById("yonetici-formu").addEventListener("submit", kaydet);
    document.getElementById("sil-dugmesi").addEventListener("click", sil);

    await listele();
}

baslat();
