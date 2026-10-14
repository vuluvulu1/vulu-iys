// Raporlar sayfası (yalnızca yönetici): seçilen dönemin özeti, teknisyen ve ay dökümü, en çok kullanılan parçalar.

let baslangic = null;   // dönemin ilk günü (dahil)
let bitis = null;       // dönemden sonraki ilk gün (hariç)

// Seçilen döneme göre başlangıç ve bitişi hesaplar; sınırlar bu bilgisayarın saatine göredir.
function donemiHesapla(donem) {
    const bugun = new Date();
    const ayBasi = new Date(bugun.getFullYear(), bugun.getMonth(), 1);

    if (donem === "gecenAy") {
        baslangic = new Date(bugun.getFullYear(), bugun.getMonth() - 1, 1);
        bitis = ayBasi;
    } else if (donem === "buYil") {
        baslangic = new Date(bugun.getFullYear(), 0, 1);
        bitis = new Date(bugun.getFullYear() + 1, 0, 1);
    } else if (donem === "sonOnIkiAy") {
        baslangic = new Date(bugun.getFullYear(), bugun.getMonth() - 11, 1);
        bitis = new Date(bugun.getFullYear(), bugun.getMonth() + 1, 1);
    } else {
        baslangic = ayBasi;
        bitis = new Date(bugun.getFullYear(), bugun.getMonth() + 1, 1);
    }
}

// Saat cinsinden süreyi okunur yazar: 0,5 -> "30 dakika", 30 -> "30 saat", 72 -> "3 gün"
function sureYaz(saat) {
    if (saat === null || saat === undefined) return "—";
    if (saat < 1) return Math.round(saat * 60) + " dakika";
    if (saat < 48) return saat.toLocaleString("tr-TR", { maximumFractionDigits: 1 }) + " saat";
    return (saat / 24).toLocaleString("tr-TR", { maximumFractionDigits: 1 }) + " gün";
}

function yaz(id, metin) {
    document.getElementById(id).textContent = metin;
}

// Tabloya satır ekler; tablo boşsa tek satırlık bir bilgi yazar.
function tabloyuDoldur(govdeId, satirlar, sutunSayisi, bosMetin) {
    const govde = document.getElementById(govdeId);
    govde.innerHTML = "";
    if (satirlar.length === 0) {
        const satir = document.createElement("tr");
        const hucre = hucreEkle(satir, bosMetin);
        hucre.colSpan = sutunSayisi;
        hucre.className = "text-secondary";
        govde.appendChild(satir);
        return;
    }
    for (const degerler of satirlar) {
        const satir = document.createElement("tr");
        for (const deger of degerler) hucreEkle(satir, deger);
        govde.appendChild(satir);
    }
}

async function yukle() {
    donemiHesapla(document.getElementById("donem").value);

    // Bitiş hariç olduğu için ekranda bir önceki gün gösterilir.
    const sonGun = new Date(bitis.getFullYear(), bitis.getMonth(), bitis.getDate() - 1);
    yaz("aralik", baslangic.toLocaleDateString("tr-TR") + " – " + sonGun.toLocaleDateString("tr-TR"));

    const cevap = await kopruyeSor("rapor", { baslangic: sunucuTarihi(baslangic), bitis: sunucuTarihi(bitis) });
    if (!cevap.basarili) {
        hataGoster("hata", cevap.hata);
        return;
    }
    hataGoster("hata", "");

    const rapor = cevap.veri;
    yaz("acilan", rapor.acilanKayit);
    yaz("tamamlanan", rapor.tamamlananKayit);
    yaz("ortalama", sureYaz(rapor.ortalamaTamirSaati));
    yaz("tahsil", paraYaz(rapor.tahsilEdilen));
    yaz("kesilen-tutar", paraYaz(rapor.kesilenFaturaTutari));
    yaz("kesilen", rapor.kesilenFatura + " kesilen fatura");
    yaz("garanti", rapor.garantiKapsamindaKayit);
    yaz("iptal", rapor.iptalEdilenKayit);

    const teknisyenler = [];
    for (const t of rapor.teknisyenler) {
        teknisyenler.push([t.ad, t.acilanKayit, t.tamamlananKayit, sureYaz(t.ortalamaTamirSaati), paraYaz(t.faturaTutari)]);
    }
    tabloyuDoldur("teknisyenler", teknisyenler, 5, "Bu dönemde kayıt yok.");

    const aylar = [];
    for (const ay of rapor.aylar) {
        const ayAdi = new Date(ay.yil, ay.ay - 1, 1).toLocaleDateString("tr-TR", { month: "long", year: "numeric" });
        aylar.push([ayAdi, ay.acilanKayit, ay.tamamlananKayit, paraYaz(ay.tahsilEdilen)]);
    }
    tabloyuDoldur("aylar", aylar, 4, "Bu dönemde kayıt yok.");

    const parcalar = [];
    for (const p of rapor.parcalar) {
        parcalar.push([p.ad, p.adet, paraYaz(p.tutar)]);
    }
    tabloyuDoldur("parcalar", parcalar, 3, "Bu dönemde tamamlanan işlerde parça kullanılmadı.");
}

async function baslat() {
    const oturum = await sayfayiHazirla();
    if (!oturum) return;

    document.getElementById("donem").addEventListener("change", yukle);
    excelDugmesiniBagla("excel-dugmesi", "excel-bilgisi", function () {
        return { liste: "rapor", baslangic: sunucuTarihi(baslangic), bitis: sunucuTarihi(bitis) };
    });

    await yukle();
}

baslat();
