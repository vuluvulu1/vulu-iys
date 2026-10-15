// C# ile konuşmanın tek yolu (C# tarafı: Kopru.cs).
// Her istek bir numara alır; C# cevabı aynı numarayla yollar, böylece hangi cevabın hangi isteğe ait olduğu bilinir.

let sonNumara = 0;
const bekleyenler = {};        // numara -> cevabı bekleyen fonksiyon
const olayDinleyicileri = {};  // olay adı -> fonksiyon

// --- Bekleme yazısı ---
// Sunucu bir süre kullanılmayınca uyur ve ilk istek bir dakika kadar sürebilir. Bir istek 1,5 saniyeden uzun
// sürerse ekranın üstünde bilgi yazısı çıkar, bütün cevaplar gelince kaybolur. Giriş ekranı bunu kendisi
// anlattığı için orada gösterilmez.
let cevapBekleyenIstek = 0;
let beklemeZamanlayicisi = null;

function beklemeYazisiniGoster() {
    if (window.location.pathname.endsWith("giris.html")) return;

    let yazi = document.getElementById("bekleme-yazisi");
    if (!yazi) {
        yazi = document.createElement("div");
        yazi.id = "bekleme-yazisi";
        yazi.className = "bekleme-yazisi";
        yazi.textContent = "Sunucuya bağlanılıyor… Sunucu uykudaysa bu bir dakika kadar sürebilir.";
        document.body.appendChild(yazi);
    }
    yazi.hidden = false;
}

function istekBasladi() {
    cevapBekleyenIstek++;
    if (beklemeZamanlayicisi === null) {
        beklemeZamanlayicisi = setTimeout(beklemeYazisiniGoster, 1500);
    }
}

function istekBitti() {
    cevapBekleyenIstek--;
    if (cevapBekleyenIstek > 0) return;

    cevapBekleyenIstek = 0;
    clearTimeout(beklemeZamanlayicisi);
    beklemeZamanlayicisi = null;
    const yazi = document.getElementById("bekleme-yazisi");
    if (yazi) yazi.hidden = true;
}

// C#'tan gelen her mesaj buraya düşer.
window.external.receiveMessage(function (metin) {
    const mesaj = JSON.parse(metin);

    // Cevap değil, C#'ın kendiliğinden yolladığı bir haber (örneğin indirme yüzdesi).
    if (mesaj.olay) {
        const dinleyici = olayDinleyicileri[mesaj.olay];
        if (dinleyici) dinleyici(mesaj.veri);
        return;
    }

    const bekleyen = bekleyenler[mesaj.no];
    delete bekleyenler[mesaj.no];
    if (bekleyen) istekBitti();

    // Oturumun süresi dolduysa ya da yönetici kapattıysa giriş ekranına dönülür.
    if (mesaj.oturumKapandi) {
        window.location.href = "giris.html";
        return;
    }

    if (bekleyen) bekleyen(mesaj);
});

// islem: Kopru.cs'teki switch'te yazan ad (örneğin "giris_yap").
// Geriye { basarili, veri, hata } döner; await ile beklenir.
function kopruyeSor(islem, veri) {
    sonNumara++;
    const numara = sonNumara;

    return new Promise(function (cevapGelince) {
        bekleyenler[numara] = cevapGelince;
        istekBasladi();
        window.external.sendMessage(JSON.stringify({ no: numara, islem: islem, veri: veri || {} }));
    });
}

// C#'ın kendiliğinden yolladığı bir haberi dinler.
function kopruOlayiniDinle(olay, fonksiyon) {
    olayDinleyicileri[olay] = fonksiyon;
}
