// Giriş ekranı dışındaki bütün sayfalarda ortak olan kısımlar: soldaki menü, şifre değiştirme penceresi ve
// birden fazla sayfanın kullandığı küçük yardımcılar.

// İşletme personelinin menüsü. sadeceYonetici: yalnızca işletme yöneticisi görür.
const menuSayfalari = [
    { adres: "anasayfa.html", ad: "Ana Sayfa", sadeceYonetici: false },
    { adres: "servis-kayitlari.html", ad: "Servis Kayıtları", sadeceYonetici: false },
    { adres: "randevular.html", ad: "Randevular", sadeceYonetici: false },
    { adres: "musteriler.html", ad: "Müşteriler", sadeceYonetici: false },
    { adres: "stok.html", ad: "Stok", sadeceYonetici: false },
    { adres: "raporlar.html", ad: "Raporlar", sadeceYonetici: true },
    { adres: "personel.html", ad: "Personel", sadeceYonetici: true },
    { adres: "isletme.html", ad: "İşletme", sadeceYonetici: true }
];

// Vulu yöneticisinin menüsü; onun oturumu bir işletmeye bağlı değildir.
const vuluMenusu = [
    { adres: "vulu-isletmeler.html", ad: "İşletmeler" },
    { adres: "vulu-yoneticiler.html", ad: "Vulu Yöneticileri" },
    { adres: "vulu-sunucu.html", ad: "Sunucu" }
];

// Şu an açık olan sayfanın dosya adı (örneğin "musteriler.html").
const acikSayfa = window.location.pathname.split("/").pop();

// Her sayfa açılınca çağırır: oturumu sorar ve menüyü çizer. Oturum bilgisini geri döner.
// Oturum yoksa kopru.js zaten giriş ekranına götürür; o durumda null döner.
async function sayfayiHazirla() {
    const cevap = await kopruyeSor("oturum_bilgisi");
    if (!cevap.basarili) return null;
    const oturum = cevap.veri;

    // Vulu yöneticisi işletme sayfalarına, işletme personeli vulu sayfalarına giremez.
    const vuluSayfasi = acikSayfa.startsWith("vulu-");
    if (oturum.vuluYoneticisi && !vuluSayfasi) {
        window.location.href = "vulu-isletmeler.html";
        return null;
    }
    if (!oturum.vuluYoneticisi && vuluSayfasi) {
        window.location.href = "anasayfa.html";
        return null;
    }

    // Yöneticiye özel bir sayfaya başka biri adres yazarak gelirse ana sayfaya döner (sunucu da izin vermez).
    for (const sayfa of menuSayfalari) {
        if (sayfa.adres === acikSayfa && sayfa.sadeceYonetici && !oturum.adminMi) {
            window.location.href = "anasayfa.html";
            return null;
        }
    }

    menuyuCiz(oturum);
    return oturum;
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
            <button type="button" id="sifre-dugmesi" class="btn btn-outline-secondary btn-sm w-100 mb-2">Şifre değiştir</button>
            <button type="button" id="cikis-dugmesi" class="btn btn-outline-secondary btn-sm w-100">Çıkış yap</button>
        </div>`;

    document.getElementById("marka-logo").src = oturum.logo || "img/logo.svg";
    document.getElementById("marka-adi").textContent = oturum.isletmeAdi || "vulu";
    document.getElementById("kullanici-adi").textContent = oturum.adSoyad;
    document.getElementById("kullanici-rolu").textContent = oturum.rol;

    const baglantilar = document.getElementById("menu-baglantilari");
    const sayfalar = oturum.vuluYoneticisi ? vuluMenusu : menuSayfalari;

    for (const sayfa of sayfalar) {
        if (sayfa.sadeceYonetici && !oturum.adminMi) continue;

        const baglanti = document.createElement("button");
        baglanti.type = "button";
        baglanti.className = "nav-link";
        baglanti.dataset.adres = sayfa.adres;
        baglanti.textContent = sayfa.ad;
        if (sayfa.adres === acikSayfa) baglanti.classList.add("active");
        baglantilar.appendChild(baglanti);
    }

    // Sayfa geçişleri: "data-adres" taşıyan her öğe tıklanınca o sayfaya gider. Düz bağlantı (href) kullanılmaz,
    // çünkü pencerenin içindeki tarayıcı, fare bağlantının üstüne gelince sol altta dosyanın adresini gösterir.
    for (const oge of document.querySelectorAll("[data-adres]")) {
        oge.addEventListener("click", function () {
            window.location.href = oge.dataset.adres;
        });
    }

    document.getElementById("sifre-dugmesi").addEventListener("click", sifrePenceresiniAc);
    document.getElementById("cikis-dugmesi").addEventListener("click", async function () {
        await kopruyeSor("cikis_yap");
        window.location.href = "giris.html";
    });
}

// --- Şifre değiştirme penceresi (her sayfada menüden açılır) ---

let sifrePenceresi = null;

function sifrePenceresiniAc() {
    // Pencere ilk kullanımda sayfaya eklenir.
    if (!sifrePenceresi) {
        const kutu = document.createElement("div");
        kutu.innerHTML = `
            <div class="modal fade" id="sifre-penceresi" tabindex="-1">
                <div class="modal-dialog">
                    <form class="modal-content" id="sifre-formu">
                        <div class="modal-header">
                            <h2 class="modal-title h5">Şifre değiştir</h2>
                            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Kapat"></button>
                        </div>
                        <div class="modal-body">
                            <label for="sd-mevcut" class="form-label">Mevcut şifre</label>
                            <input id="sd-mevcut" type="password" class="form-control mb-3" autocomplete="current-password" />
                            <label for="sd-yeni" class="form-label">Yeni şifre</label>
                            <input id="sd-yeni" type="password" class="form-control mb-3" autocomplete="new-password" />
                            <label for="sd-tekrar" class="form-label">Yeni şifre (tekrar)</label>
                            <input id="sd-tekrar" type="password" class="form-control mb-2" autocomplete="new-password" />
                            <p class="small text-secondary">En az 8 karakter. Başka yerde kullanmadığınız bir şifre seçin.</p>
                            <p id="sd-hata" class="text-danger mb-0" hidden></p>
                            <p id="sd-tamam" class="text-success mb-0" hidden>Şifreniz değiştirildi. Diğer cihazlardaki oturumlarınız kapatıldı.</p>
                        </div>
                        <div class="modal-footer">
                            <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Kapat</button>
                            <button type="submit" id="sd-kaydet" class="btn btn-primary">Kaydet</button>
                        </div>
                    </form>
                </div>
            </div>`;
        document.body.appendChild(kutu);
        sifrePenceresi = new bootstrap.Modal(document.getElementById("sifre-penceresi"));
        document.getElementById("sifre-formu").addEventListener("submit", sifreyiKaydet);
    }

    document.getElementById("sd-mevcut").value = "";
    document.getElementById("sd-yeni").value = "";
    document.getElementById("sd-tekrar").value = "";
    document.getElementById("sd-tamam").hidden = true;
    document.getElementById("sd-kaydet").hidden = false;
    hataGoster("sd-hata", "");
    sifrePenceresi.show();
}

async function sifreyiKaydet(olay) {
    olay.preventDefault();
    const yeni = document.getElementById("sd-yeni").value;
    if (yeni !== document.getElementById("sd-tekrar").value) {
        hataGoster("sd-hata", "Yeni şifre ile tekrarı aynı değil.");
        return;
    }

    const cevap = await kopruyeSor("sifre_degistir", {
        mevcutSifre: document.getElementById("sd-mevcut").value,
        yeniSifre: yeni
    });

    // Şifreler kutularda gereğinden uzun kalmaz.
    document.getElementById("sd-mevcut").value = "";
    document.getElementById("sd-yeni").value = "";
    document.getElementById("sd-tekrar").value = "";

    if (!cevap.basarili) {
        hataGoster("sd-hata", cevap.hata);
        return;
    }
    hataGoster("sd-hata", "");
    document.getElementById("sd-tamam").hidden = false;
    document.getElementById("sd-kaydet").hidden = true;
}

// --- Biçimler ---

// Boş değerler ekranda "—" olarak görünür.
function bosIseTire(deger) {
    if (deger === null || deger === undefined || deger === "") return "—";
    return deger;
}

// Formlardaki isteğe bağlı alanlar boş bırakıldıysa sunucuya "yok" (null) olarak gider.
function bosIseNull(metin) {
    const temiz = (metin || "").trim();
    return temiz === "" ? null : temiz;
}

// 1250.5 -> "1.250,50 ₺"
function paraYaz(tutar) {
    return Number(tutar).toLocaleString("tr-TR", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + " ₺";
}

// Para kutusuna yazılacak hali: 1250.5 -> "1250,50"
function paraGirdisi(tutar) {
    return Number(tutar).toFixed(2).replace(".", ",");
}

// Kullanıcının yazdığı tutarı sayıya çevirir: "1250", "1250,50", "1.250,50" ve "1250.50" kabul edilir.
// Okunamazsa null döner.
function paraOku(metin) {
    let girdi = (metin || "").replace(/\s/g, "").replace("₺", "");
    if (girdi === "") return null;

    if (girdi.includes(",")) {
        girdi = girdi.replace(/\./g, "").replace(",", ".");   // Türkçe yazım: nokta binlik, virgül kuruş
    } else if (/^\d{1,3}(\.\d{3})+$/.test(girdi)) {
        girdi = girdi.replace(/\./g, "");                     // "1.250" binlik ayraçlı yazılmış
    }

    if (!/^\d+(\.\d{1,2})?$/.test(girdi)) return null;
    return Number(girdi);
}

// Sunucunun verdiği UTC tarihi bu bilgisayarın saatine göre yazar: "14.10.2026 21:30"
function tarihYaz(metin) {
    if (!metin) return "—";
    const tarih = new Date(metin);
    return tarih.toLocaleDateString("tr-TR") + " " + tarih.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
}

// Yerel bir tarihi sunucuya gönderilecek biçime (UTC) çevirir.
function sunucuTarihi(tarih) {
    return tarih.toISOString();
}

// Servis kaydı durumlarının ekrandaki adları ve renkleri.
const servisDurumlari = {
    Beklemede: { ad: "Beklemede", renk: "text-bg-secondary" },
    OnayBekliyor: { ad: "Onay bekliyor", renk: "text-bg-warning" },
    DevamEdiyor: { ad: "Devam ediyor", renk: "text-bg-info" },
    Tamamlandi: { ad: "Tamamlandı", renk: "text-bg-success" },
    TeslimEdildi: { ad: "Teslim edildi", renk: "text-bg-primary" },
    IptalEdildi: { ad: "İptal edildi", renk: "text-bg-danger" }
};

const randevuDurumlari = {
    Planlandi: { ad: "Planlandı", renk: "text-bg-info" },
    Tamamlandi: { ad: "Tamamlandı", renk: "text-bg-success" },
    IptalEdildi: { ad: "İptal edildi", renk: "text-bg-danger" }
};

// Durumu renkli bir etiket olarak döner. durumlar: servisDurumlari ya da randevuDurumlari.
function durumEtiketi(durumlar, durum) {
    const etiket = document.createElement("span");
    const bilgi = durumlar[durum] || { ad: durum, renk: "text-bg-secondary" };
    etiket.className = "badge " + bilgi.renk;
    etiket.textContent = bilgi.ad;
    return etiket;
}

// --- Liste sayfalarının ortak yardımcıları ---

// Tablo satırına bir hücre ekler. Metin textContent ile yazılır; içinde HTML olsa bile çalışmaz.
function hucreEkle(satir, metin) {
    const hucre = document.createElement("td");
    hucre.textContent = metin;
    satir.appendChild(hucre);
    return hucre;
}

// Bir kutuya düğme ekler. tur: "ikincil" (çerçeveli), "tehlike" (kırmızı) ya da "ana" (dolu).
function dugmeEkle(kutu, metin, tur, tiklaninca) {
    const dugme = document.createElement("button");
    dugme.type = "button";
    if (tur === "tehlike") dugme.className = "btn btn-outline-danger btn-sm ms-1";
    else if (tur === "ana") dugme.className = "btn btn-primary btn-sm ms-1";
    else dugme.className = "btn btn-outline-secondary btn-sm ms-1";
    dugme.textContent = metin;
    dugme.addEventListener("click", tiklaninca);
    kutu.appendChild(dugme);
    return dugme;
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

    const onceki = dugmeEkle(kutu, "Önceki", "ikincil", function () { sayfayaGit(sayfa.sayfaNo - 1); });
    onceki.disabled = sayfa.sayfaNo <= 1;

    const konum = document.createElement("span");
    konum.className = "mx-2";
    konum.textContent = sayfa.sayfaNo + " / " + sayfa.sayfaSayisi;
    kutu.appendChild(konum);

    const sonraki = dugmeEkle(kutu, "Sonraki", "ikincil", function () { sayfayaGit(sayfa.sayfaNo + 1); });
    sonraki.disabled = sayfa.sayfaNo >= sayfa.sayfaSayisi;
}

// "Excel'e aktar" düğmesi. veriyiHazirla: hangi listenin hangi süzgeçle aktarılacağını döner.
function excelDugmesiniBagla(dugmeId, bilgiId, veriyiHazirla) {
    const dugme = document.getElementById(dugmeId);
    dugme.addEventListener("click", async function () {
        const bilgi = document.getElementById(bilgiId);
        dugme.disabled = true;
        bilgi.textContent = "Hazırlanıyor…";

        const cevap = await kopruyeSor("excel", veriyiHazirla());

        dugme.disabled = false;
        bilgi.textContent = cevap.basarili
            ? "İndirilenler klasörüne kaydedildi: " + cevap.veri.dosyaAdi
            : cevap.hata;
    });
}

// --- Formlardaki seçiciler ---

// Teknisyen açılır listesini doldurur. mevcutId/mevcutAd: düzenlenen kayıtta şu an atanmış kişi; hesabı
// sonradan devre dışı bırakılmışsa listede olmaz, ataması kaybolmasın diye ayrıca eklenir.
async function personelSeciminiDoldur(listeId, mevcutId, mevcutAd) {
    const liste = document.getElementById(listeId);
    liste.innerHTML = "";

    const bos = document.createElement("option");
    bos.value = "";
    bos.textContent = "— Atanmadı —";
    liste.appendChild(bos);

    const cevap = await kopruyeSor("personel_ozet");
    const personel = cevap.basarili ? cevap.veri : [];
    let mevcutListede = false;

    for (const kisi of personel) {
        const secenek = document.createElement("option");
        secenek.value = kisi.id;
        secenek.textContent = kisi.uzmanlik ? kisi.adSoyad + " (" + kisi.uzmanlik + ")" : kisi.adSoyad;
        liste.appendChild(secenek);
        if (kisi.id === mevcutId) mevcutListede = true;
    }

    if (mevcutId && !mevcutListede) {
        const secenek = document.createElement("option");
        secenek.value = mevcutId;
        secenek.textContent = mevcutAd + " (devre dışı)";
        liste.appendChild(secenek);
    }

    liste.value = mevcutId || "";
}

// Müşteri seçici: müşteri çok olabileceği için açılır liste yerine aranarak seçilir.
// kutuId: seçicinin çizileceği boş kutu. secilince: müşteri seçilince (ya da seçim kaldırılınca) çağrılır.
// Geriye iki fonksiyonlu bir nesne döner: secili() seçili müşteriyi verir, ayarla(musteri) dışarıdan seçer.
function musteriSeciciKur(kutuId, secilince) {
    const kutu = document.getElementById(kutuId);
    let secili = null;

    function ciz() {
        kutu.innerHTML = "";

        if (secili) {
            // Seçili müşteri ve "Değiştir" düğmesi.
            const satir = document.createElement("div");
            satir.className = "d-flex align-items-center justify-content-between border rounded p-2";
            const ad = document.createElement("span");
            const kalin = document.createElement("strong");
            kalin.textContent = secili.adSoyad;
            ad.appendChild(kalin);
            ad.append(" " + (secili.telefon || ""));
            satir.appendChild(ad);
            dugmeEkle(satir, "Değiştir", "ikincil", function () { ayarla(null); });
            kutu.appendChild(satir);
            return;
        }

        // Arama kutusu ve sonuç listesi.
        const arama = document.createElement("input");
        arama.type = "search";
        arama.className = "form-control";
        arama.placeholder = "Müşteri adı veya telefonu yazın";
        arama.id = kutuId + "-arama";
        const sonuclar = document.createElement("div");
        sonuclar.className = "list-group mt-1";
        kutu.appendChild(arama);
        kutu.appendChild(sonuclar);

        aramaKutusunuBagla(arama.id, async function (metin) {
            sonuclar.innerHTML = "";
            if (metin === "") return;

            const cevap = await kopruyeSor("musteri_listele", { arama: metin, boyut: 6 });
            if (!cevap.basarili) {
                sonuclar.textContent = cevap.hata;
                return;
            }
            if (cevap.veri.ogeler.length === 0) {
                const bilgi = document.createElement("div");
                bilgi.className = "small text-secondary";
                bilgi.textContent = "Eşleşen müşteri yok. Önce Müşteriler sayfasından ekleyin.";
                sonuclar.appendChild(bilgi);
                return;
            }
            for (const musteri of cevap.veri.ogeler) {
                const secenek = document.createElement("button");
                secenek.type = "button";
                secenek.className = "list-group-item list-group-item-action";
                secenek.textContent = musteri.adSoyad + "  " + (musteri.telefon || "");
                secenek.addEventListener("click", function () { ayarla(musteri); });
                sonuclar.appendChild(secenek);
            }
        });
    }

    function ayarla(musteri) {
        secili = musteri;
        ciz();
        if (secilince) secilince(musteri);
    }

    ciz();
    return {
        secili: function () { return secili; },
        ayarla: ayarla
    };
}
