// Stok sayfası: parça listesi, stok kartı ekleme/düzenleme, stok girişi, hareketler ve silme.
// Kart ve stok işlemlerini yönetici ile "stok yetkisi" verilmiş personel yapar; diğerleri yalnızca listeyi görür.

let oturum = null;
let arama = "";
let yalnizAzalan = false;
let sayfaNo = 1;
let duzenlenen = null;     // düzenlenen parça; yeni kartta null
let stokParcasi = null;    // stok girişi yapılan parça
let silinecek = null;

const formPenceresi = new bootstrap.Modal(document.getElementById("form-penceresi"));
const stokPenceresi = new bootstrap.Modal(document.getElementById("stok-penceresi"));
const hareketPenceresi = new bootstrap.Modal(document.getElementById("hareket-penceresi"));
const silmePenceresi = new bootstrap.Modal(document.getElementById("silme-penceresi"));

async function listele() {
    const cevap = await kopruyeSor("parca_listele", { arama: arama, azalan: yalnizAzalan, sayfa: sayfaNo, boyut: 20 });
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

    for (const parca of sayfa.ogeler) {
        const satir = document.createElement("tr");
        hucreEkle(satir, parca.ad);
        hucreEkle(satir, bosIseTire(parca.kod));
        hucreEkle(satir, paraYaz(parca.birimFiyat));

        const stok = hucreEkle(satir, "");
        const adet = document.createElement("strong");
        adet.textContent = parca.stokAdedi;
        stok.appendChild(adet);
        if (parca.azaldiMi) {
            const uyari = document.createElement("span");
            uyari.className = "badge text-bg-danger ms-2";
            uyari.textContent = parca.stokAdedi === 0 ? "tükendi" : "azaldı";
            stok.appendChild(uyari);
        }

        const islemler = hucreEkle(satir, "");
        islemler.className = "text-end text-nowrap";
        dugmeEkle(islemler, "Hareketler", "ikincil", function () { hareketleriAc(parca); });
        if (oturum.stokYonetebilir) {
            dugmeEkle(islemler, "Stok girişi", "ikincil", function () { stokGirisiniAc(parca); });
            dugmeEkle(islemler, "Düzenle", "ikincil", function () { formuAc(parca); });
            dugmeEkle(islemler, "Sil", "tehlike", function () { silmeyiAc(parca); });
        }

        satirlar.appendChild(satir);
    }

    const bos = sayfa.ogeler.length === 0;
    document.getElementById("tablo").hidden = bos;
    document.getElementById("bos-mesaji").hidden = !bos;
    if (arama !== "" || yalnizAzalan) {
        document.getElementById("bos-mesaji").textContent = "Süzgeçle eşleşen parça bulunamadı.";
    } else {
        document.getElementById("bos-mesaji").textContent = "Kayıtlı stok kartı bulunmuyor.";
    }

    sayfalamayiCiz("sayfalama", sayfa, "parça", function (yeniSayfa) {
        sayfaNo = yeniSayfa;
        listele();
    });
}

// parca verilirse düzenleme, verilmezse yeni kart penceresi açılır.
function formuAc(parca) {
    duzenlenen = parca;
    document.getElementById("form-basligi").textContent = parca ? "Parçayı düzenle" : "Stok kartı";
    document.getElementById("p-ad").value = parca ? parca.ad : "";
    document.getElementById("p-kod").value = parca ? (parca.kod || "") : "";
    document.getElementById("p-fiyat").value = parca ? paraGirdisi(parca.birimFiyat) : "";
    document.getElementById("p-kritik").value = parca ? parca.kritikSeviye : 1;
    document.getElementById("p-stok").value = 0;
    // Başlangıç stoğu yalnızca yeni kartta sorulur; sonrası stok girişiyle değişir.
    document.getElementById("baslangic-kutusu").hidden = parca !== null;
    hataGoster("form-hatasi", "");
    formPenceresi.show();
}

async function kaydet(olay) {
    olay.preventDefault();

    const fiyat = paraOku(document.getElementById("p-fiyat").value);
    if (fiyat === null) {
        hataGoster("form-hatasi", "Geçerli bir birim fiyat girin (örn. 250 ya da 249,90).");
        return;
    }
    const baslangic = Number(document.getElementById("p-stok").value || 0);
    if (baslangic < 0) {
        hataGoster("form-hatasi", "Başlangıç stoğu eksi olamaz.");
        return;
    }

    const dugme = document.getElementById("kaydet-dugmesi");
    dugme.disabled = true;

    const cevap = await kopruyeSor("parca_kaydet", {
        id: duzenlenen ? duzenlenen.id : "",
        ad: document.getElementById("p-ad").value.trim(),
        kod: bosIseNull(document.getElementById("p-kod").value),
        birimFiyat: fiyat,
        kritikSeviye: Number(document.getElementById("p-kritik").value || 0)
    });

    // Yeni kartın başlangıç stoğu ayrı bir stok girişidir; böylece hareket geçmişinde de görünür.
    if (cevap.basarili && !duzenlenen && baslangic > 0) {
        const giris = await kopruyeSor("parca_stok", { id: cevap.veri.id, adet: baslangic, aciklama: "Başlangıç stoğu" });
        if (!giris.basarili) {
            hataGoster("liste-hatasi", "Parça eklendi ama başlangıç stoğu yazılamadı: " + giris.hata);
        }
    }

    dugme.disabled = false;
    if (!cevap.basarili) {
        hataGoster("form-hatasi", cevap.hata);
        return;
    }

    formPenceresi.hide();
    await listele();
}

function stokGirisiniAc(parca) {
    stokParcasi = parca;
    document.getElementById("stok-basligi").textContent = "Stok girişi – " + parca.ad;
    document.getElementById("mevcut-stok").textContent = parca.stokAdedi;
    document.getElementById("h-adet").value = 1;
    document.getElementById("h-aciklama").value = "";
    hataGoster("stok-hatasi", "");
    stokPenceresi.show();
}

async function stokuKaydet(olay) {
    olay.preventDefault();

    const adet = Number(document.getElementById("h-adet").value || 0);
    if (adet === 0) {
        hataGoster("stok-hatasi", "Adet sıfır olamaz.");
        return;
    }

    const cevap = await kopruyeSor("parca_stok", {
        id: stokParcasi.id,
        adet: adet,
        aciklama: bosIseNull(document.getElementById("h-aciklama").value)
    });
    if (!cevap.basarili) {
        hataGoster("stok-hatasi", cevap.hata);
        return;
    }

    stokPenceresi.hide();
    await listele();
}

async function hareketleriAc(parca) {
    document.getElementById("hareket-basligi").textContent = "Stok hareketleri – " + parca.ad;
    document.getElementById("hareket-bilgisi").innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Yükleniyor…';
    document.getElementById("hareket-tablosu").hidden = true;
    hareketPenceresi.show();

    const cevap = await kopruyeSor("parca_hareketleri", { id: parca.id });
    if (!cevap.basarili) {
        document.getElementById("hareket-bilgisi").textContent = cevap.hata;
        return;
    }
    if (cevap.veri.length === 0) {
        document.getElementById("hareket-bilgisi").textContent = "Bu parçanın stok hareketi yok.";
        return;
    }

    const govde = document.getElementById("hareketler");
    govde.innerHTML = "";
    for (const hareket of cevap.veri) {
        const satir = document.createElement("tr");
        hucreEkle(satir, tarihYaz(hareket.tarih));
        const adet = hucreEkle(satir, (hareket.adet > 0 ? "+" : "") + hareket.adet);
        adet.className = hareket.adet > 0 ? "text-success" : "text-danger";
        hucreEkle(satir, bosIseTire(hareket.aciklama));
        hucreEkle(satir, bosIseTire(hareket.servisKaydiNo));
        hucreEkle(satir, hareket.kullaniciAdi);
        govde.appendChild(satir);
    }
    document.getElementById("hareket-bilgisi").textContent = "";
    document.getElementById("hareket-tablosu").hidden = false;
}

function silmeyiAc(parca) {
    silinecek = parca;
    document.getElementById("silinecek-ad").textContent = parca.ad;
    hataGoster("silme-hatasi", "");
    silmePenceresi.show();
}

async function sil() {
    const cevap = await kopruyeSor("parca_sil", { id: silinecek.id });
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

    document.getElementById("yeni-dugmesi").hidden = !oturum.stokYonetebilir;
    document.getElementById("yeni-dugmesi").addEventListener("click", function () { formuAc(null); });
    document.getElementById("parca-formu").addEventListener("submit", kaydet);
    document.getElementById("stok-formu").addEventListener("submit", stokuKaydet);
    document.getElementById("sil-dugmesi").addEventListener("click", sil);
    document.getElementById("suzgec").addEventListener("change", function (olay) {
        yalnizAzalan = olay.target.value === "true";
        sayfaNo = 1;
        listele();
    });
    excelDugmesiniBagla("excel-dugmesi", "excel-bilgisi", function () { return { liste: "stok" }; });
    aramaKutusunuBagla("arama", function (metin) {
        arama = metin;
        sayfaNo = 1;
        listele();
    });

    await listele();
}

baslat();
