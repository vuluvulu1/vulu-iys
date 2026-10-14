// Ana sayfa. Şimdilik yalnızca karşılama yazısı; özet kutuları sayfalar taşınırken eklenecek.

async function baslat() {
    const oturum = await sayfayiHazirla();
    if (!oturum) return;

    document.getElementById("hos-geldiniz").textContent = "Hoş geldiniz, " + oturum.adSoyad + ".";
}

baslat();
