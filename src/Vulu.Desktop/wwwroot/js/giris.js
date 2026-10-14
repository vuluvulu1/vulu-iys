// Giriş ekranı. Açılınca önce güncelleme ve sunucu kontrol edilir; giriş formu ancak her şey yolundaysa görünür.

const bolumler = ["kontrol-ediliyor", "ulasilamiyor", "uyumsuz", "guncelleme", "giris-formu"];

const isletmeKoduKutusu = document.getElementById("isletme-kodu");
const kullaniciAdiKutusu = document.getElementById("kullanici-adi");
const sifreKutusu = document.getElementById("sifre");
const girisDugmesi = document.getElementById("giris-dugmesi");
const girisHatasi = document.getElementById("giris-hatasi");

// Vulu yöneticisi işletmeler listesiyle, işletme personeli ana sayfayla başlar.
function acilisSayfasinaGit(vuluYoneticisi) {
    window.location.href = vuluYoneticisi ? "vulu-isletmeler.html" : "anasayfa.html";
}

// Verilen bölümü gösterir, diğerlerini gizler.
function bolumuGoster(gosterilecek) {
    for (const bolum of bolumler) {
        document.getElementById(bolum).hidden = (bolum !== gosterilecek);
    }
}

async function baslangicKontrolu() {
    bolumuGoster("kontrol-ediliyor");

    const cevap = await kopruyeSor("baslangic_kontrolu");
    if (!cevap.basarili) {
        bolumuGoster("ulasilamiyor");
        return;
    }

    const durum = cevap.veri.durum;
    if (durum === "girisYapildi") {
        acilisSayfasinaGit(cevap.veri.vuluYoneticisi);
    } else if (durum === "guncelleme") {
        document.getElementById("yeni-surum").textContent = cevap.veri.yeniSurum;
        document.getElementById("mevcut-surum").textContent = cevap.veri.mevcutSurum;
        bolumuGoster("guncelleme");
    } else if (durum === "hazir") {
        isletmeKoduKutusu.value = cevap.veri.sonIsletmeKodu;
        bolumuGoster("giris-formu");
        // İşletme kodu hatırlanıyorsa doğrudan kullanıcı adına geçilir.
        if (isletmeKoduKutusu.value) kullaniciAdiKutusu.focus();
        else isletmeKoduKutusu.focus();
    } else if (durum === "uyumsuz") {
        bolumuGoster("uyumsuz");
    } else {
        bolumuGoster("ulasilamiyor");
    }
}

async function guncelle() {
    const dugme = document.getElementById("guncelle-dugmesi");
    const hata = document.getElementById("guncelleme-hatasi");

    dugme.hidden = true;
    hata.hidden = true;
    document.getElementById("ilerleme-kutusu").hidden = false;

    // Başarılıysa uygulama kapanıp yeni sürümle açılır; cevap yalnızca bir hata olduğunda gelir.
    const cevap = await kopruyeSor("guncelle");

    document.getElementById("ilerleme-kutusu").hidden = true;
    hata.textContent = cevap.hata;
    hata.hidden = false;
    dugme.textContent = "Yeniden dene";
    dugme.hidden = false;
}

// İndirme sürerken C# yüzdeyi bildirir.
kopruOlayiniDinle("indirme", function (yuzde) {
    document.getElementById("ilerleme").style.width = yuzde + "%";
    document.getElementById("ilerleme").textContent = "%" + yuzde;
});

// Üç kutu da doluysa giriş düğmesi açılır.
function dugmeyiGuncelle() {
    const dolu = isletmeKoduKutusu.value.trim() !== ""
        && kullaniciAdiKutusu.value.trim() !== ""
        && sifreKutusu.value !== "";
    girisDugmesi.disabled = !dolu;
}

async function girisYap(olay) {
    olay.preventDefault();   // formun sayfayı yeniden yüklemesini engeller
    if (girisDugmesi.disabled) return;

    girisDugmesi.disabled = true;
    girisDugmesi.textContent = "Giriş yapılıyor…";
    girisHatasi.hidden = true;

    const cevap = await kopruyeSor("giris_yap", {
        isletmeKodu: isletmeKoduKutusu.value,
        kullaniciAdi: kullaniciAdiKutusu.value,
        sifre: sifreKutusu.value
    });

    if (cevap.basarili) {
        acilisSayfasinaGit(cevap.veri.vuluYoneticisi);
        return;
    }

    sifreKutusu.value = "";
    girisHatasi.textContent = cevap.hata;
    girisHatasi.hidden = false;
    girisDugmesi.textContent = "Giriş yap";
    dugmeyiGuncelle();
    sifreKutusu.focus();
}

document.getElementById("yeniden-dene").addEventListener("click", baslangicKontrolu);
document.getElementById("guncelle-dugmesi").addEventListener("click", guncelle);
document.getElementById("giris-formu").addEventListener("submit", girisYap);
isletmeKoduKutusu.addEventListener("input", dugmeyiGuncelle);
kullaniciAdiKutusu.addEventListener("input", dugmeyiGuncelle);
sifreKutusu.addEventListener("input", dugmeyiGuncelle);

baslangicKontrolu();
