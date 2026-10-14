// Randevular sayfası: liste ve haftalık takvim görünümü, randevu ekleme/düzenleme, durum değiştirme ve silme.

let oturum = null;
let arama = "";
let durumSuzgeci = "Planlandi";   // sayfa, yaklaşan işleri göstermek için planlanmış randevularla açılır
let sayfaNo = 1;
let takvimMi = false;
let haftaBasi = haftaninBasi(new Date());
let duzenlenen = null;
let silinecek = null;

const formPenceresi = new bootstrap.Modal(document.getElementById("form-penceresi"));
const silmePenceresi = new bootstrap.Modal(document.getElementById("silme-penceresi"));
// Müşteri seçilince (ya da değiştirilince) o müşterinin servis kayıtları yüklenir. Düzenlenen randevunun kendi
// müşterisiyse bağlı olduğu kayıt seçili gelir.
const musteriSecici = musteriSeciciKur("musteri-secici", function (musteri) {
    let seciliKayit = "";
    if (duzenlenen && musteri && duzenlenen.musteriId === musteri.id) seciliKayit = duzenlenen.servisKaydiId || "";
    musterininKayitlariniYukle(musteri, seciliKayit);
});

const gunAdlari = ["Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz"];
const standartSureler = [30, 60, 90, 120, 180, 240];

// --- Tarih yardımcıları ---

// Haftalar Pazartesi başlar; verilen günün haftasının Pazartesi'si (saat 00:00).
function haftaninBasi(gun) {
    const sonuc = new Date(gun.getFullYear(), gun.getMonth(), gun.getDate());
    const geri = (sonuc.getDay() + 6) % 7;   // Pazartesi 0, Pazar 6
    sonuc.setDate(sonuc.getDate() - geri);
    return sonuc;
}

function gunEkle(tarih, gun) {
    const sonuc = new Date(tarih);
    sonuc.setDate(sonuc.getDate() + gun);
    return sonuc;
}

function ayniGunMu(a, b) {
    return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

function ikiHane(sayi) {
    return String(sayi).padStart(2, "0");
}

// "datetime-local" kutusunun beklediği yerel yazım: "2026-10-14T09:00"
function kutuTarihi(tarih) {
    return tarih.getFullYear() + "-" + ikiHane(tarih.getMonth() + 1) + "-" + ikiHane(tarih.getDate())
        + "T" + ikiHane(tarih.getHours()) + ":" + ikiHane(tarih.getMinutes());
}

function saatYaz(tarih) {
    return ikiHane(tarih.getHours()) + ":" + ikiHane(tarih.getMinutes());
}

function sureYaz(dakika) {
    if (dakika < 60) return dakika + " dakika";
    if (dakika % 60 === 0) return (dakika / 60) + " saat";
    return Math.floor(dakika / 60) + " saat " + (dakika % 60) + " dakika";
}

// --- Liste ve takvim ---

async function listele() {
    const veri = { arama: arama, durum: durumSuzgeci };
    if (takvimMi) {
        // Takvim seçili haftanın tamamını tek seferde ister; liste sayfalanır.
        veri.baslangic = sunucuTarihi(haftaBasi);
        veri.bitis = sunucuTarihi(gunEkle(haftaBasi, 7));
        veri.boyut = 200;
    } else {
        veri.sayfa = sayfaNo;
        veri.boyut = 20;
    }

    const cevap = await kopruyeSor("randevu_listele", veri);
    if (!cevap.basarili) {
        hataGoster("liste-hatasi", cevap.hata);
        return;
    }
    hataGoster("liste-hatasi", "");

    const sayfa = cevap.veri;
    if (takvimMi) {
        takvimiCiz(sayfa.ogeler);
        return;
    }
    if (sayfa.ogeler.length === 0 && sayfaNo > 1) {
        sayfaNo = sayfa.sayfaSayisi;
        await listele();
        return;
    }
    listeyiCiz(sayfa);
}

function listeyiCiz(sayfa) {
    const satirlar = document.getElementById("satirlar");
    satirlar.innerHTML = "";

    for (const randevu of sayfa.ogeler) {
        const satir = document.createElement("tr");

        const tarih = hucreEkle(satir, tarihYaz(randevu.randevuTarihi));
        const sure = document.createElement("div");
        sure.className = "small text-secondary";
        sure.textContent = randevu.sureDakika + " dk";
        tarih.appendChild(sure);

        const musteri = hucreEkle(satir, randevu.musteriAdi);
        const telefon = document.createElement("div");
        telefon.className = "small text-secondary";
        telefon.textContent = randevu.musteriTelefonu || "";
        musteri.appendChild(telefon);

        hucreEkle(satir, bosIseTire(randevu.teknisyenAdi));
        hucreEkle(satir, bosIseTire(randevu.aciklama));
        hucreEkle(satir, "").appendChild(durumEtiketi(randevuDurumlari, randevu.durum));

        const islemler = hucreEkle(satir, "");
        islemler.className = "text-end text-nowrap";
        if (randevu.durum === "Planlandi") {
            dugmeEkle(islemler, "Tamamla", "ikincil", function () { durumuDegistir(randevu, "Tamamlandi"); });
            dugmeEkle(islemler, "İptal et", "ikincil", function () { durumuDegistir(randevu, "IptalEdildi"); });
        } else {
            dugmeEkle(islemler, "Yeniden planla", "ikincil", function () { durumuDegistir(randevu, "Planlandi"); });
        }
        dugmeEkle(islemler, "Düzenle", "ikincil", function () { formuAc(randevu, null); });
        if (oturum.adminMi) {
            dugmeEkle(islemler, "Sil", "tehlike", function () { silmeyiAc(randevu); });
        }

        satirlar.appendChild(satir);
    }

    const bos = sayfa.ogeler.length === 0;
    document.getElementById("tablo").hidden = bos;
    document.getElementById("bos-mesaji").hidden = !bos;
    let bosMetin = "Kayıtlı randevu bulunmuyor.";
    if (arama !== "") bosMetin = "Aramayla eşleşen randevu yok.";
    else if (durumSuzgeci === "Planlandi") bosMetin = "Planlanmış randevu yok.";
    else if (durumSuzgeci !== "") bosMetin = "Bu durumda randevu yok.";
    document.getElementById("bos-mesaji").textContent = bosMetin;

    sayfalamayiCiz("sayfalama", sayfa, "kayıt", function (yeniSayfa) {
        sayfaNo = yeniSayfa;
        listele();
    });
}

function takvimiCiz(randevular) {
    const son = gunEkle(haftaBasi, 6);
    document.getElementById("hafta-araligi").textContent =
        haftaBasi.toLocaleDateString("tr-TR", { day: "numeric", month: "long" }) + " – "
        + son.toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric" });

    const takvim = document.getElementById("takvim");
    takvim.innerHTML = "";
    const bugun = new Date();

    for (let i = 0; i < 7; i++) {
        const gun = gunEkle(haftaBasi, i);
        const sutun = document.createElement("section");
        sutun.className = "takvim-gunu" + (ayniGunMu(gun, bugun) ? " bugun" : "");

        const baslik = document.createElement("header");
        baslik.textContent = gunAdlari[i] + " " + gun.getDate();
        // Bu güne sabah 9'a ayarlı yeni randevu.
        dugmeEkle(baslik, "+", "ikincil", function () {
            const sabah = new Date(gun);
            sabah.setHours(9);
            formuAc(null, sabah);
        });
        sutun.appendChild(baslik);

        let say = 0;
        for (const randevu of randevular) {
            const baslangic = new Date(randevu.randevuTarihi);
            if (!ayniGunMu(baslangic, gun)) continue;
            say++;

            const bitis = new Date(baslangic.getTime() + randevu.sureDakika * 60000);
            const kart = document.createElement("button");
            kart.type = "button";
            kart.className = "takvim-randevusu " + randevu.durum;
            kart.addEventListener("click", function () { formuAc(randevu, null); });

            const saat = document.createElement("strong");
            saat.textContent = saatYaz(baslangic) + "–" + saatYaz(bitis);
            const ad = document.createElement("div");
            ad.textContent = randevu.musteriAdi;
            kart.appendChild(saat);
            kart.appendChild(ad);
            if (randevu.teknisyenAdi) {
                const teknisyen = document.createElement("div");
                teknisyen.className = "small text-secondary";
                teknisyen.textContent = randevu.teknisyenAdi;
                kart.appendChild(teknisyen);
            }
            sutun.appendChild(kart);
        }

        if (say === 0) {
            const bos = document.createElement("p");
            bos.className = "text-secondary";
            bos.textContent = "—";
            sutun.appendChild(bos);
        }
        takvim.appendChild(sutun);
    }
}

function gorunumSec(takvim) {
    takvimMi = takvim;
    sayfaNo = 1;
    document.getElementById("liste-kutusu").hidden = takvim;
    document.getElementById("takvim-kutusu").hidden = !takvim;
    document.getElementById("liste-gorunumu").className = takvim ? "btn btn-outline-secondary" : "btn btn-primary";
    document.getElementById("takvim-gorunumu").className = takvim ? "btn btn-primary" : "btn btn-outline-secondary";
    listele();
}

async function durumuDegistir(randevu, durum) {
    const cevap = await kopruyeSor("randevu_durum", { id: randevu.id, durum: durum });
    if (!cevap.basarili) {
        hataGoster("liste-hatasi", cevap.hata);
        return;
    }
    await listele();
}

// --- Form ---

// randevu verilirse düzenleme; verilmezse yeni kayıt (onerilenTarih: takvimden seçilen gün).
function formuAc(randevu, onerilenTarih) {
    duzenlenen = randevu;
    document.getElementById("form-basligi").textContent = randevu ? "Randevuyu düzenle" : "Randevu kaydı";

    const musteri = randevu ? { id: randevu.musteriId, adSoyad: randevu.musteriAdi, telefon: randevu.musteriTelefonu } : null;
    musteriSecici.ayarla(musteri);   // ilgili servis kaydı listesi de bununla yüklenir

    let tarih = "";
    if (randevu) tarih = kutuTarihi(new Date(randevu.randevuTarihi));
    else if (onerilenTarih) tarih = kutuTarihi(onerilenTarih);
    document.getElementById("r-tarih").value = tarih;

    // Düzenlenen randevunun süresi standart seçeneklerden biri değilse listeye eklenir ki kaybolmasın.
    const sure = randevu ? randevu.sureDakika : 60;
    const sureler = standartSureler.slice();
    if (!sureler.includes(sure)) sureler.push(sure);
    sureler.sort(function (a, b) { return a - b; });
    const sureListesi = document.getElementById("r-sure");
    sureListesi.innerHTML = "";
    for (const dakika of sureler) {
        const secenek = document.createElement("option");
        secenek.value = dakika;
        secenek.textContent = sureYaz(dakika);
        sureListesi.appendChild(secenek);
    }
    sureListesi.value = sure;

    personelSeciminiDoldur("r-teknisyen", randevu ? randevu.teknisyenId : null, randevu ? randevu.teknisyenAdi : null);
    document.getElementById("r-aciklama").value = randevu ? (randevu.aciklama || "") : "";
    hataGoster("form-hatasi", "");
    formPenceresi.show();
}

// Seçilen müşterinin servis kayıtları (randevuyu bir kayda bağlamak için).
async function musterininKayitlariniYukle(musteri, seciliId) {
    const liste = document.getElementById("r-kayit");
    liste.innerHTML = "";
    const yok = document.createElement("option");
    yok.value = "";
    yok.textContent = "— Yok —";
    liste.appendChild(yok);
    liste.disabled = !musteri;
    if (!musteri) return;

    const cevap = await kopruyeSor("servis_listele", { musteriId: musteri.id, boyut: 50 });
    if (!cevap.basarili) return;
    for (const kayit of cevap.veri.ogeler) {
        const secenek = document.createElement("option");
        secenek.value = kayit.id;
        secenek.textContent = tarihYaz(kayit.olusturmaTarihi) + " – " + kayit.cihazBilgisi;
        liste.appendChild(secenek);
    }
    liste.value = seciliId;
}

async function kaydet(olay) {
    olay.preventDefault();

    const musteri = musteriSecici.secili();
    if (!musteri) {
        hataGoster("form-hatasi", "Müşteri seçilmeli.");
        return;
    }
    const tarih = document.getElementById("r-tarih").value;
    if (tarih === "") {
        hataGoster("form-hatasi", "Tarih ve saat seçilmeli.");
        return;
    }

    const dugme = document.getElementById("kaydet-dugmesi");
    dugme.disabled = true;
    const cevap = await kopruyeSor("randevu_kaydet", {
        id: duzenlenen ? duzenlenen.id : "",
        musteriId: musteri.id,
        teknisyenId: bosIseNull(document.getElementById("r-teknisyen").value),
        servisKaydiId: bosIseNull(document.getElementById("r-kayit").value),
        // Kutudaki saat bu bilgisayarın saatidir; sunucuya UTC gönderilir.
        randevuTarihi: sunucuTarihi(new Date(tarih)),
        sureDakika: Number(document.getElementById("r-sure").value),
        aciklama: bosIseNull(document.getElementById("r-aciklama").value)
    });
    dugme.disabled = false;

    if (!cevap.basarili) {
        hataGoster("form-hatasi", cevap.hata);
        return;
    }
    formPenceresi.hide();
    await listele();
}

function silmeyiAc(randevu) {
    silinecek = randevu;
    document.getElementById("silme-metni").textContent =
        randevu.musteriAdi + " için " + tarihYaz(randevu.randevuTarihi) + " randevusu kalıcı olarak silinecek.";
    hataGoster("silme-hatasi", "");
    silmePenceresi.show();
}

async function sil() {
    const cevap = await kopruyeSor("randevu_sil", { id: silinecek.id });
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

    document.getElementById("yeni-dugmesi").addEventListener("click", function () { formuAc(null, null); });
    document.getElementById("randevu-formu").addEventListener("submit", kaydet);
    document.getElementById("sil-dugmesi").addEventListener("click", sil);
    document.getElementById("liste-gorunumu").addEventListener("click", function () { gorunumSec(false); });
    document.getElementById("takvim-gorunumu").addEventListener("click", function () { gorunumSec(true); });
    document.getElementById("onceki-hafta").addEventListener("click", function () { haftaBasi = gunEkle(haftaBasi, -7); listele(); });
    document.getElementById("bu-hafta").addEventListener("click", function () { haftaBasi = haftaninBasi(new Date()); listele(); });
    document.getElementById("sonraki-hafta").addEventListener("click", function () { haftaBasi = gunEkle(haftaBasi, 7); listele(); });
    document.getElementById("durum-suzgeci").addEventListener("change", function (olay) {
        durumSuzgeci = olay.target.value;
        sayfaNo = 1;
        listele();
    });
    aramaKutusunuBagla("arama", function (metin) {
        arama = metin;
        sayfaNo = 1;
        listele();
    });

    await listele();
}

baslat();
