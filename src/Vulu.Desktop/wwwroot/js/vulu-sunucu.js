// Vulu yöneticisinin, canlı ortamda sunucunun istekleri doğru gördüğünü denetlediği sayfa.

async function yukle() {
    const cevap = await kopruyeSor("vulu_baglanti");
    if (!cevap.basarili) {
        hataGoster("hata", cevap.hata);
        return;
    }
    hataGoster("hata", "");

    const baglanti = cevap.veri;
    document.getElementById("adres").textContent = baglanti.istemciAdresi || "bilinmiyor";
    const https = document.getElementById("https");
    https.textContent = baglanti.https ? "Evet (HTTPS)" : "Hayır";
    https.className = "col-sm-8 " + (baglanti.https ? "text-success" : "text-danger");
    document.getElementById("vekil").textContent = baglanti.iletilenAdresSayisi;
}

async function baslat() {
    const oturum = await sayfayiHazirla();
    if (!oturum) return;

    document.getElementById("yenile-dugmesi").addEventListener("click", yukle);
    await yukle();
}

baslat();
