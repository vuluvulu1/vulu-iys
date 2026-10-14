// Personel sayfası (yalnızca yönetici): personel listesi, hesap açma/düzenleme ve silme.

let oturum = null;
let duzenlenen = null;
let silinecek = null;

const formPenceresi = new bootstrap.Modal(document.getElementById("form-penceresi"));
const silmePenceresi = new bootstrap.Modal(document.getElementById("silme-penceresi"));

function rolAdi(rol) {
    return rol === "Admin" ? "Yönetici" : "Teknisyen";
}

async function listele() {
    const cevap = await kopruyeSor("personel_listele");
    if (!cevap.basarili) {
        hataGoster("liste-hatasi", cevap.hata);
        return;
    }
    hataGoster("liste-hatasi", "");

    const satirlar = document.getElementById("satirlar");
    satirlar.innerHTML = "";

    for (const kisi of cevap.veri) {
        const satir = document.createElement("tr");
        const benMiyim = kisi.id === oturum.kimlik;

        const ad = hucreEkle(satir, kisi.adSoyad);
        if (benMiyim) {
            const rozet = document.createElement("span");
            rozet.className = "badge text-bg-secondary ms-2";
            rozet.textContent = "siz";
            ad.appendChild(rozet);
        }
        hucreEkle(satir, kisi.kullaniciAdi);
        hucreEkle(satir, bosIseTire(kisi.telefon));
        hucreEkle(satir, bosIseTire(kisi.uzmanlik));

        const rol = hucreEkle(satir, rolAdi(kisi.rol));
        if (kisi.rol !== "Admin" && kisi.stokYetkisi) {
            const rozet = document.createElement("span");
            rozet.className = "badge text-bg-secondary ms-2";
            rozet.textContent = "stok";
            rol.appendChild(rozet);
        }

        const durum = hucreEkle(satir, kisi.aktifMi ? "Aktif" : "Devre dışı");
        durum.className = kisi.aktifMi ? "text-success" : "text-danger";

        const islemler = hucreEkle(satir, "");
        islemler.className = "text-end text-nowrap";
        dugmeEkle(islemler, "Düzenle", "ikincil", function () { formuAc(kisi); });
        // Kişi kendi hesabını silemez.
        const sil = dugmeEkle(islemler, "Sil", "tehlike", function () { silmeyiAc(kisi); });
        sil.disabled = benMiyim;

        satirlar.appendChild(satir);
    }
}

// Rol "Yönetici" seçiliyken stok yetkisi kutusu gizlenir.
function stokKutusunuAyarla() {
    document.getElementById("stok-kutusu").hidden = document.getElementById("f-rol").value === "Admin";
}

function formuAc(kisi) {
    duzenlenen = kisi;
    document.getElementById("form-basligi").textContent = kisi ? "Personeli düzenle" : "Personel kaydı";
    document.getElementById("f-adsoyad").value = kisi ? kisi.adSoyad : "";
    document.getElementById("f-telefon").value = kisi ? (kisi.telefon || "") : "";
    document.getElementById("f-uzmanlik").value = kisi ? (kisi.uzmanlik || "") : "";
    document.getElementById("f-ad").value = kisi ? kisi.kullaniciAdi : "";
    document.getElementById("f-sifre").value = "";
    document.getElementById("sifre-etiketi").textContent = kisi ? "Yeni şifre (boşsa değişmez)" : "Şifre";
    document.getElementById("f-rol").value = kisi ? kisi.rol : "Teknisyen";
    document.getElementById("f-stok").checked = kisi ? kisi.stokYetkisi : false;
    document.getElementById("f-aktif").checked = kisi ? kisi.aktifMi : true;
    // "Hesap aktif" kutusu yalnızca düzenlemede görünür; yeni hesap her zaman aktif açılır.
    document.getElementById("aktif-kutusu").hidden = !kisi;
    stokKutusunuAyarla();
    hataGoster("form-hatasi", "");
    formPenceresi.show();
}

async function kaydet(olay) {
    olay.preventDefault();

    const rol = document.getElementById("f-rol").value;
    const sifre = document.getElementById("f-sifre").value;
    const veri = {
        id: duzenlenen ? duzenlenen.id : "",
        kullaniciAdi: document.getElementById("f-ad").value.trim(),
        rol: rol,
        adSoyad: document.getElementById("f-adsoyad").value.trim(),
        telefon: bosIseNull(document.getElementById("f-telefon").value),
        uzmanlik: bosIseNull(document.getElementById("f-uzmanlik").value),
        // Yöneticiler stoğu zaten yönetir; işaret yalnızca diğer personel için tutulur.
        stokYetkisi: rol !== "Admin" && document.getElementById("f-stok").checked
    };
    if (duzenlenen) {
        veri.aktifMi = document.getElementById("f-aktif").checked;
        veri.yeniSifre = sifre === "" ? null : sifre;
    } else {
        veri.sifre = sifre;
    }

    const dugme = document.getElementById("kaydet-dugmesi");
    dugme.disabled = true;
    const cevap = await kopruyeSor("personel_kaydet", veri);
    dugme.disabled = false;
    document.getElementById("f-sifre").value = "";

    if (!cevap.basarili) {
        hataGoster("form-hatasi", cevap.hata);
        return;
    }
    formPenceresi.hide();
    await listele();
}

function silmeyiAc(kisi) {
    silinecek = kisi;
    document.getElementById("silme-metni").textContent =
        kisi.adSoyad + " (" + kisi.kullaniciAdi + ") kalıcı olarak silinecek. Bu işlem geri alınamaz.";
    hataGoster("silme-hatasi", "");
    silmePenceresi.show();
}

async function sil() {
    const cevap = await kopruyeSor("personel_sil", { id: silinecek.id });
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
    document.getElementById("personel-formu").addEventListener("submit", kaydet);
    document.getElementById("f-rol").addEventListener("change", stokKutusunuAyarla);
    document.getElementById("sil-dugmesi").addEventListener("click", sil);

    await listele();
}

baslat();
