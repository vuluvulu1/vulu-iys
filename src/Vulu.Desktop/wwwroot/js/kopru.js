// C# ile konuşmanın tek yolu (C# tarafı: Kopru.cs).
// Her istek bir numara alır; C# cevabı aynı numarayla yollar, böylece hangi cevabın hangi isteğe ait olduğu bilinir.
//
// Numaranın başında sayfanın kendi kimliği vardır ("k3f9x-1", "k3f9x-2"...). Kullanıcı bir sayfadan cevap gelmeden
// başka sayfaya geçerse eski sayfanın geç kalan cevabı yeni sayfaya ulaşır; kimlik farklı olduğu için hiçbir
// isteğe eşleşmez ve yok sayılır. Kimlik olmasaydı iki sayfanın 1 numaralı istekleri birbirine karışırdı.

const sayfaKimligi = Math.random().toString(36).slice(2, 8);
let sonNumara = 0;
const bekleyenler = {};        // numara -> cevabı bekleyen fonksiyon
const olayDinleyicileri = {};  // olay adı -> fonksiyon

// --- Bekleme göstergesi ---
// Cevap beklenirken ekranın üstünde dönen bir halka ve yazı görünür; böylece bir şeyin geldiği belli olur.
// Kısa istekler göstergeyi hiç çıkarmaz. Bekleme uzarsa (sunucu uykudan uyanıyor olabilir) yazı değişir ve geçen
// saniye sayılır.
let cevapBekleyenIstek = 0;
let beklemeBaslangici = 0;       // ilk bekleyen isteğin başladığı an (milisaniye)
let beklemeZamanlayicisi = null; // göstergeyi her saniye güncelleyen zamanlayıcı

function gostergeyiGuncelle() {
    // Giriş ekranı sunucu kontrolünü kendi yazısıyla anlatır.
    if (window.location.pathname.endsWith("giris.html")) return;

    const gecen = Math.floor((Date.now() - beklemeBaslangici) / 1000);
    if (Date.now() - beklemeBaslangici < 400) return;

    let gosterge = document.getElementById("bekleme-gostergesi");
    if (!gosterge) {
        gosterge = document.createElement("div");
        gosterge.id = "bekleme-gostergesi";
        gosterge.className = "bekleme-gostergesi";
        gosterge.innerHTML = '<span class="spinner-border spinner-border-sm"></span><span id="bekleme-metni"></span>';
        document.body.appendChild(gosterge);
    }

    let metin = "Yükleniyor…";
    if (gecen >= 3) {
        metin = "Sunucuya bağlanılıyor… Sunucu uykudaysa bu bir dakika kadar sürebilir (" + gecen + " sn)";
    }
    document.getElementById("bekleme-metni").textContent = metin;
    gosterge.hidden = false;
}

function istekBasladi() {
    cevapBekleyenIstek++;
    if (beklemeZamanlayicisi === null) {
        beklemeBaslangici = Date.now();
        beklemeZamanlayicisi = setInterval(gostergeyiGuncelle, 200);
    }
}

function istekBitti() {
    cevapBekleyenIstek--;
    if (cevapBekleyenIstek > 0) return;

    // Bir cevap çoğu zaman hemen yeni bir isteği başlatır (önce oturum, sonra liste). Gösterge bu kısa arada
    // kapanıp açılmasın diye biraz beklenir.
    setTimeout(function () {
        if (cevapBekleyenIstek > 0) return;

        clearInterval(beklemeZamanlayicisi);
        beklemeZamanlayicisi = null;
        const gosterge = document.getElementById("bekleme-gostergesi");
        if (gosterge) gosterge.hidden = true;
    }, 100);
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
    const numara = sayfaKimligi + "-" + sonNumara;

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
