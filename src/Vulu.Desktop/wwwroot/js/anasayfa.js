// Ana sayfa: işlerin özeti, yaklaşan randevular ve en uzun süredir açık kayıtlar.

function yaz(id, metin) {
    document.getElementById(id).textContent = metin;
}

// Listeye bir satır ekler: solda tarih, yanında açıklama ve sağda soluk bir ek bilgi.
function satirEkle(liste, tarih, metin, ek) {
    const satir = document.createElement("li");
    satir.className = "d-flex gap-3 py-1 border-bottom";

    const tarihKutusu = document.createElement("span");
    tarihKutusu.className = "text-secondary text-nowrap";
    tarihKutusu.textContent = tarih;

    const metinKutusu = document.createElement("span");
    metinKutusu.className = "flex-grow-1";
    metinKutusu.textContent = metin;

    satir.appendChild(tarihKutusu);
    satir.appendChild(metinKutusu);
    if (typeof ek === "string") {
        const ekKutusu = document.createElement("span");
        ekKutusu.className = "text-secondary";
        ekKutusu.textContent = ek;
        satir.appendChild(ekKutusu);
    } else {
        satir.appendChild(ek);   // durum etiketi
    }
    liste.appendChild(satir);
}

function bosMesaji(liste, metin) {
    const satir = document.createElement("li");
    satir.className = "text-secondary";
    satir.textContent = metin;
    liste.appendChild(satir);
}

async function baslat() {
    const oturum = await sayfayiHazirla();
    if (!oturum) return;

    yaz("hos-geldiniz", "Hoş geldiniz, " + oturum.adSoyad);

    // "Bugün" ve "bu ay" bu bilgisayarın saatine göre hesaplanır.
    const simdi = new Date();
    const bugun = new Date(simdi.getFullYear(), simdi.getMonth(), simdi.getDate());
    const ayBasi = new Date(simdi.getFullYear(), simdi.getMonth(), 1);

    const cevap = await kopruyeSor("ozet", {
        gunBaslangici: sunucuTarihi(bugun),
        ayBaslangici: sunucuTarihi(ayBasi)
    });
    if (!cevap.basarili) {
        hataGoster("hata", cevap.hata);
        return;
    }

    const ozet = cevap.veri;
    yaz("bekleyen", ozet.bekleyenKayit);
    yaz("onay-bekleyen", ozet.onayBekleyenKayit);
    yaz("devam-eden", ozet.devamEdenKayit);
    yaz("teslim-bekleyen", ozet.teslimBekleyenKayit);
    yaz("bugunku-randevu", ozet.bugunkuRandevu);
    yaz("odenmemis-tutar", paraYaz(ozet.odenmemisTutar));
    yaz("odenmemis-fatura", ozet.odenmemisFatura + " ödenmemiş fatura");
    yaz("bu-ay", paraYaz(ozet.buAyTahsilat));
    yaz("azalan", ozet.azalanParca);
    document.getElementById("azalan-kutusu").hidden = ozet.azalanParca === 0;

    const randevular = document.getElementById("randevular");
    if (ozet.yaklasanRandevular.length === 0) bosMesaji(randevular, "Planlanmış randevu yok.");
    for (const randevu of ozet.yaklasanRandevular) {
        satirEkle(randevular, tarihYaz(randevu.randevuTarihi), randevu.musteriAdi, randevu.teknisyenAdi || "Teknisyen atanmadı");
    }

    const kayitlar = document.getElementById("acik-kayitlar");
    if (ozet.acikKayitlar.length === 0) bosMesaji(kayitlar, "Açık servis kaydı yok.");
    for (const kayit of ozet.acikKayitlar) {
        satirEkle(kayitlar, tarihYaz(kayit.olusturmaTarihi), kayit.musteriAdi + " – " + kayit.cihazAdi,
            durumEtiketi(servisDurumlari, kayit.durum));
    }
}

baslat();
