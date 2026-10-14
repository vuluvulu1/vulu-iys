// C# ile konuşmanın tek yolu (C# tarafı: Kopru.cs).
// Her istek bir numara alır; C# cevabı aynı numarayla yollar, böylece hangi cevabın hangi isteğe ait olduğu bilinir.

let sonNumara = 0;
const bekleyenler = {};        // numara -> cevabı bekleyen fonksiyon
const olayDinleyicileri = {};  // olay adı -> fonksiyon

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
        window.external.sendMessage(JSON.stringify({ no: numara, islem: islem, veri: veri || {} }));
    });
}

// C#'ın kendiliğinden yolladığı bir haberi dinler.
function kopruOlayiniDinle(olay, fonksiyon) {
    olayDinleyicileri[olay] = fonksiyon;
}
