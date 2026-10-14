// Vulu yöneticisinin işletmeler sayfası: işletme listesi (logolarıyla), yeni işletme açma ve bir işletmeyi yönetme.

let yonetilen = null;   // yönetim penceresinde açık olan işletme

const yeniPenceresi = new bootstrap.Modal(document.getElementById("yeni-penceresi"));
const yonetimPenceresi = new bootstrap.Modal(document.getElementById("yonetim-penceresi"));

// Bir bölümün sonucunu (hata ya da başarı) düğmesinin yanına yazar.
function sonucYaz(kutuId, cevap, basariMetni) {
    const kutu = document.getElementById(kutuId);
    kutu.className = cevap.basarili ? "small text-success" : "small text-danger";
    kutu.textContent = cevap.basarili ? basariMetni : cevap.hata;
}

async function listele() {
    const cevap = await kopruyeSor("vulu_isletmeler");
    if (!cevap.basarili) {
        hataGoster("liste-hatasi", cevap.hata);
        return;
    }
    hataGoster("liste-hatasi", "");

    const isletmeler = cevap.veri;
    const satirlar = document.getElementById("satirlar");
    satirlar.innerHTML = "";

    for (const isletme of isletmeler) {
        const satir = document.createElement("tr");

        // Logosu olmayan (ya da logosu henüz yüklenmemiş) işletme, adının baş harfiyle gösterilir.
        const adHucresi = hucreEkle(satir, "");
        const logo = document.createElement("span");
        logo.className = "kucuk-logo";
        logo.textContent = isletme.ad.trim().substring(0, 1).toLocaleUpperCase("tr-TR");
        adHucresi.appendChild(logo);
        adHucresi.append(isletme.ad);
        if (isletme.logoTuru) logoyuYukle(isletme, logo);

        const kod = document.createElement("code");
        kod.textContent = isletme.kod;
        hucreEkle(satir, "").appendChild(kod);
        hucreEkle(satir, isletme.personelSayisi);
        hucreEkle(satir, tarihYaz(isletme.olusturmaTarihi));
        const durum = hucreEkle(satir, isletme.aktifMi ? "Açık" : "Askıda");
        durum.className = isletme.aktifMi ? "text-success" : "text-danger";

        const islemler = hucreEkle(satir, "");
        islemler.className = "text-end";
        dugmeEkle(islemler, "Yönet", "ikincil", function () { yonetimiAc(isletme); });

        satirlar.appendChild(satir);
    }

    document.getElementById("tablo").hidden = isletmeler.length === 0;
    document.getElementById("bos-mesaji").hidden = isletmeler.length > 0;
    document.getElementById("toplam").textContent = isletmeler.length + " işletme";
}

// Liste hemen gösterilir; logolar arkasından tek tek indirilir ve baş harfin yerine konur.
async function logoyuYukle(isletme, yerTutucu) {
    const cevap = await kopruyeSor("vulu_isletme_logosu", { id: isletme.id, tur: isletme.logoTuru });
    if (!cevap.basarili) return;   // indirilemezse baş harf kalır

    const resim = document.createElement("img");
    resim.className = "kucuk-logo";
    resim.alt = "";
    resim.src = cevap.veri.adres;
    yerTutucu.replaceWith(resim);
}

// --- Yeni işletme ---

function yeniyiAc() {
    for (const id of ["y-ad", "y-kod", "y-adres", "y-telefon", "y-eposta", "y-yonetici-ad", "y-yonetici-kullanici", "y-yonetici-sifre"]) {
        document.getElementById(id).value = "";
    }
    hataGoster("yeni-hatasi", "");
    yeniPenceresi.show();
}

async function yeniyiKaydet(olay) {
    olay.preventDefault();
    const cevap = await kopruyeSor("vulu_isletme_ac", {
        kod: document.getElementById("y-kod").value.trim().toLowerCase(),
        ad: document.getElementById("y-ad").value.trim(),
        adres: document.getElementById("y-adres").value.trim(),
        telefon: document.getElementById("y-telefon").value.trim(),
        eposta: document.getElementById("y-eposta").value.trim(),
        yoneticiKullaniciAdi: document.getElementById("y-yonetici-kullanici").value.trim(),
        yoneticiSifre: document.getElementById("y-yonetici-sifre").value,
        yoneticiAdSoyad: document.getElementById("y-yonetici-ad").value.trim()
    });
    document.getElementById("y-yonetici-sifre").value = "";

    if (!cevap.basarili) {
        hataGoster("yeni-hatasi", cevap.hata);
        return;
    }
    yeniPenceresi.hide();
    await listele();
}

// --- Yönetim penceresi ---

function yonetimiCiz() {
    document.getElementById("yonetim-basligi").textContent = yonetilen.ad;
    // Silme yalnızca askıya alınmış işletmede açılır.
    document.getElementById("silme-once-askiya").hidden = !yonetilen.aktifMi;
    document.getElementById("silme-formu").hidden = yonetilen.aktifMi;
    document.getElementById("silme-kodu").textContent = yonetilen.kod;
}

async function yonetimiAc(isletme) {
    yonetilen = isletme;
    document.getElementById("g-ad").value = isletme.ad;
    document.getElementById("g-adres").value = isletme.adres || "";
    document.getElementById("g-telefon").value = isletme.telefon || "";
    document.getElementById("g-eposta").value = isletme.eposta || "";
    document.getElementById("g-aktif").checked = isletme.aktifMi;
    document.getElementById("s-sifre").value = "";
    document.getElementById("silme-onayi").value = "";
    document.getElementById("sil-dugmesi").disabled = true;
    for (const id of ["genel-sonucu", "sifre-sonucu", "silme-sonucu"]) document.getElementById(id).textContent = "";
    yonetimiCiz();
    yonetimPenceresi.show();

    // İşletmenin yönetici hesapları (şifre sıfırlama için).
    const liste = document.getElementById("s-yonetici");
    liste.innerHTML = "";
    const cevap = await kopruyeSor("vulu_isletme_yoneticileri", { id: isletme.id });
    if (!cevap.basarili) {
        sonucYaz("sifre-sonucu", cevap, "");
        return;
    }
    for (const yonetici of cevap.veri) {
        const secenek = document.createElement("option");
        secenek.value = yonetici.id;
        secenek.textContent = yonetici.adSoyad + " (" + yonetici.kullaniciAdi + ")" + (yonetici.aktifMi ? "" : " – devre dışı");
        liste.appendChild(secenek);
    }
}

async function geneliKaydet(olay) {
    olay.preventDefault();
    const cevap = await kopruyeSor("vulu_isletme_guncelle", {
        id: yonetilen.id,
        ad: document.getElementById("g-ad").value.trim(),
        aktifMi: document.getElementById("g-aktif").checked,
        adres: document.getElementById("g-adres").value.trim(),
        telefon: document.getElementById("g-telefon").value.trim(),
        eposta: document.getElementById("g-eposta").value.trim()
    });
    sonucYaz("genel-sonucu", cevap, "Kaydedildi.");
    if (cevap.basarili) {
        yonetilen = cevap.veri;
        yonetimiCiz();
        listele();
    }
}

async function sifreyiSifirla(olay) {
    olay.preventDefault();
    const yoneticiId = document.getElementById("s-yonetici").value;
    if (yoneticiId === "") return;

    const cevap = await kopruyeSor("vulu_sifre_sifirla", {
        id: yonetilen.id,
        yoneticiId: yoneticiId,
        yeniSifre: document.getElementById("s-sifre").value
    });
    sonucYaz("sifre-sonucu", cevap, "Şifre değiştirildi; yeni şifreyi işletmeye iletin.");
    if (cevap.basarili) document.getElementById("s-sifre").value = "";
}

async function kaliciSil(olay) {
    olay.preventDefault();
    const kod = document.getElementById("silme-onayi").value.trim();
    if (kod !== yonetilen.kod) return;

    const cevap = await kopruyeSor("vulu_isletme_sil", { id: yonetilen.id, kod: kod });
    if (!cevap.basarili) {
        document.getElementById("silme-sonucu").textContent = cevap.hata;
        return;
    }
    yonetimPenceresi.hide();
    await listele();
}

async function baslat() {
    const oturum = await sayfayiHazirla();
    if (!oturum) return;

    document.getElementById("yeni-dugmesi").addEventListener("click", yeniyiAc);
    document.getElementById("yenile-dugmesi").addEventListener("click", listele);
    document.getElementById("yeni-formu").addEventListener("submit", yeniyiKaydet);
    document.getElementById("genel-formu").addEventListener("submit", geneliKaydet);
    document.getElementById("sifre-sifirlama").addEventListener("submit", sifreyiSifirla);
    document.getElementById("silme-formu").addEventListener("submit", kaliciSil);
    // Silme düğmesi ancak işletme kodu doğru yazılınca açılır.
    document.getElementById("silme-onayi").addEventListener("input", function (olay) {
        document.getElementById("sil-dugmesi").disabled = olay.target.value.trim() !== yonetilen.kod;
    });

    await listele();
}

baslat();
