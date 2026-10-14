// Giriş ekranı dışındaki bütün sayfalarda ortak olan kısım: soldaki menü ve oturum bilgisi.

// Menüde görünen sayfalar. sadeceYonetici: yalnızca işletme yöneticisi görür.
// Yeni bir sayfa taşındıkça buraya eklenir.
const menuSayfalari = [
    { adres: "anasayfa.html", ad: "Ana Sayfa", sadeceYonetici: false },
    { adres: "musteriler.html", ad: "Müşteriler", sadeceYonetici: false }
];

// Her sayfa açılınca çağırır: oturumu sorar ve menüyü çizer. Oturum bilgisini geri döner.
// Oturum yoksa kopru.js zaten giriş ekranına götürür; o durumda null döner.
async function sayfayiHazirla() {
    const cevap = await kopruyeSor("oturum_bilgisi");
    if (!cevap.basarili) return null;

    menuyuCiz(cevap.veri);
    return cevap.veri;
}

function menuyuCiz(oturum) {
    const menu = document.getElementById("yan-menu");

    // Bu kalıpta yalnızca sabit metin var; kullanıcıdan gelen bilgiler aşağıda textContent ile yazılır,
    // böylece içlerinde HTML olsa bile çalışmaz.
    menu.innerHTML = `
        <div class="marka">
            <img id="marka-logo" alt="" />
            <span id="marka-adi"></span>
        </div>
        <nav id="menu-baglantilari" class="nav flex-column"></nav>
        <div class="mt-auto pt-3 border-top">
            <div id="kullanici-adi" class="fw-semibold"></div>
            <div id="kullanici-rolu" class="small text-secondary mb-2"></div>
            <button type="button" id="cikis-dugmesi" class="btn btn-outline-secondary btn-sm w-100">Çıkış yap</button>
        </div>`;

    document.getElementById("marka-logo").src = oturum.logo || "img/logo.svg";
    document.getElementById("marka-adi").textContent = oturum.isletmeAdi || "vulu";
    document.getElementById("kullanici-adi").textContent = oturum.adSoyad;
    document.getElementById("kullanici-rolu").textContent = oturum.rol;

    // Şu an açık olan sayfanın dosya adı (örneğin "anasayfa.html"); menüde vurgulanır.
    const acikSayfa = window.location.pathname.split("/").pop();
    const baglantilar = document.getElementById("menu-baglantilari");

    for (const sayfa of menuSayfalari) {
        if (sayfa.sadeceYonetici && !oturum.adminMi) continue;

        const baglanti = document.createElement("a");
        baglanti.className = "nav-link";
        baglanti.href = sayfa.adres;
        baglanti.textContent = sayfa.ad;
        if (sayfa.adres === acikSayfa) baglanti.classList.add("active");
        baglantilar.appendChild(baglanti);
    }

    document.getElementById("cikis-dugmesi").addEventListener("click", async function () {
        await kopruyeSor("cikis_yap");
        window.location.href = "giris.html";
    });
}

// --- Liste sayfalarının ortak yardımcıları ---

// Boş değerler tabloda "—" olarak görünür.
function bosIseTire(deger) {
    if (deger === null || deger === undefined || deger === "") return "—";
    return deger;
}

// Tablo satırına bir hücre ekler. Metin textContent ile yazılır; içinde HTML olsa bile çalışmaz.
function hucreEkle(satir, metin) {
    const hucre = document.createElement("td");
    hucre.textContent = metin;
    satir.appendChild(hucre);
    return hucre;
}

// Kutuya hata mesajı yazar; mesaj boşsa kutuyu gizler.
function hataGoster(kutuId, mesaj) {
    const kutu = document.getElementById(kutuId);
    kutu.textContent = mesaj || "";
    kutu.hidden = !mesaj;
}

// Arama kutusu: yazma durduktan kısa süre sonra arar, her tuşta sunucuya istek gitmez.
function aramaKutusunuBagla(kutuId, aramaYap) {
    let zamanlayici = null;
    document.getElementById(kutuId).addEventListener("input", function (olay) {
        clearTimeout(zamanlayici);
        zamanlayici = setTimeout(function () {
            aramaYap(olay.target.value.trim());
        }, 300);
    });
}

// Listenin altındaki "toplam / önceki / sonraki" kısmı. sayfa: API'den gelen sayfa bilgisi.
function sayfalamayiCiz(kutuId, sayfa, birim, sayfayaGit) {
    const kutu = document.getElementById(kutuId);
    kutu.innerHTML = "";

    const toplam = document.createElement("span");
    toplam.className = "text-secondary me-3";
    toplam.textContent = sayfa.toplam + " " + birim;
    kutu.appendChild(toplam);

    if (sayfa.sayfaSayisi <= 1) return;

    const onceki = document.createElement("button");
    onceki.className = "btn btn-outline-secondary btn-sm";
    onceki.textContent = "Önceki";
    onceki.disabled = sayfa.sayfaNo <= 1;
    onceki.addEventListener("click", function () { sayfayaGit(sayfa.sayfaNo - 1); });

    const konum = document.createElement("span");
    konum.className = "mx-2";
    konum.textContent = sayfa.sayfaNo + " / " + sayfa.sayfaSayisi;

    const sonraki = document.createElement("button");
    sonraki.className = "btn btn-outline-secondary btn-sm";
    sonraki.textContent = "Sonraki";
    sonraki.disabled = sayfa.sayfaNo >= sayfa.sayfaSayisi;
    sonraki.addEventListener("click", function () { sayfayaGit(sayfa.sayfaNo + 1); });

    kutu.appendChild(onceki);
    kutu.appendChild(konum);
    kutu.appendChild(sonraki);
}
