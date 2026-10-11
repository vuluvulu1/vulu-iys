# vulu İşletme Yönetim Sistemi

Servis ve onarım işi yapan işletmelere hizmet olarak sunulan yönetim paneli. Tek bir API birden çok işletmeye
hizmet verir; her işletmenin verisi kendi veritabanında durur.

Bir işletme bu sistemle şunları yapar:

- müşterilerini ve personelini kaydeder,
- cihazı marka, model, seri numarası, aksesuar ve teslim alındığı andaki durumuyla servise kabul eder,
- fiyat teklifi verir ve müşterinin onayını kaydeder,
- kullanılan yedek parçaları stoktan düşer,
- kabul formu, teslim formu ve fatura belgesini PDF olarak alır,
- randevuları takvimde izler,
- müşteriyi WhatsApp ya da e-postayla bilgilendirir; müşteri cihazının durumunu bir web sayfasından sorgular,
- dönem raporlarını ve her kaydın işlem geçmişini görür.

## Mimari

```text
Masaüstü uygulaması (Windows)  --HTTPS-->  API (Render, Docker)  -->  MongoDB Atlas
     Photino + Blazor                       ASP.NET Core               işletme başına bir veritabanı
```

Masaüstü uygulaması veritabanına hiç bağlanmaz; yalnızca API'nin adresini bilir. Veritabanına erişen tek şey
API'dir, bu yüzden işletmelerin bilgisayarları herhangi bir ağdan çalışır.

## Çok işletmeli yapı

- **Vulu yöneticisi** işletme kaydını açar ve işletmenin ilk yöneticisinin hesabını oluşturur; gerektiğinde
  işletmeyi askıya alır. Giriş ekranında işletme kodu olarak `vulu` yazar ve servis ekranları yerine İşletmeler
  ekranını görür. İşletmelerin müşteri ve servis verisine API üzerinden erişemez.
- **İşletme yöneticisi** kendi personelini ekler, işletmenin adını, adresini, vergi bilgilerini ve logosunu
  düzenler.
- **Giriş** üç alanla yapılır: işletme kodu, kullanıcı adı, şifre. Kullanıcı adları işletme içinde tekildir.
- **Veri ayrımı:** platform veritabanında (`Mongo:VeritabaniAdi`) yalnızca işletme listesi, vulu yöneticileri ve
  yenileme tokenları durur. Her işletmenin personeli, müşterileri, servis kayıtları ve randevuları
  `Mongo:IsletmeVeritabaniOneki` + işletme kodu adlı ayrı bir veritabanındadır. Her istek, oturumdaki işletmenin
  veritabanına bağlanır (`Altyapi/OturumDogrulayici.cs`); askıya alınan işletmenin açık oturumları da reddedilir.
- Veri geçişleri ve indexler açılışta her işletme veritabanına ayrı ayrı uygulanır.

### Vulu yöneticisinin işlemleri

- **İşletme kaydı:** kod ve adla birlikte adres, telefon ve e-posta da kayıt sırasında zorunludur; vulu
  yöneticisi bunları sonradan düzeltebilir.
- Anlaşma süresi, ödemeler ve personel sayısı sınırı sistemde tutulmaz; işletmelerle kâğıt üzerinde, uzun
  dönemli anlaşılır. Anlaşması biten işletme askıya alınır.
- **Logo:** işletme yöneticisi herhangi bir görsel seçebilir; masaüstü uygulaması göndermeden önce en uzun
  kenarı 512 piksele küçültür.
- **Yönetici şifresi sıfırlama:** yalnızca işletmenin yönetici hesapları için. Bu, vulu yöneticisinin o hesapla
  giriş yapıp işletmenin verisini görebileceği anlamına gelir; her sıfırlama API günlüğüne yazılır.
- **Kalıcı silme:** işletme önce askıya alınmış olmalı ve işletme kodu yeniden yazılmalıdır. İşletmenin
  veritabanı geri dönüşsüz silinir.
- **Vulu yöneticileri:** birden fazla olabilir; her biri diğerlerini yönetir. Kendi hesabını silemez ve en az
  bir aktif yönetici kalmalıdır.

## Güvenlik

- **Oturum:** erişim tokenı 15 dakika geçerlidir ve her istekte veritabanındaki güncel durumla karşılaştırılır
  (`Altyapi/OturumDogrulayici.cs`). İşletme askıya alınınca, hesap silinince ya da devre dışı bırakılınca, rolü
  ya da şifresi değişince açık oturumları tokenın süresi dolmadan kapanır. Bunu sağlayan, hesabın üzerinde duran
  ve tokena da yazılan oturum damgasıdır.
- **Yenileme tokenı:** tek kullanımlıktır ve veritabanında yalnızca özeti durur. Kullanılmış bir token yeniden
  sunulursa çalınmış sayılır ve hesabın bütün oturumları kapatılır.
- **Şifreler:** bcrypt ile saklanır; en az 8 karakter, en fazla 72 bayt. Herkes kendi şifresini yan menüdeki
  “Şifre değiştir” ile değiştirir; yöneticinin verdiği ilk şifrenin değiştirilmesi beklenir.
- **Deneme sınırları:** giriş denemeleri adres başına dakikada `GirisSiniri:DakikadaDeneme` (10) ile, hesap
  başına da `GirisKilidi:Deneme` (10) yanlış denemeden sonra `GirisKilidi:KilitDakika` (10) dakikalık kilitle
  sınırlıdır. Hesap kilidinin bedeli, kullanıcı adını bilen birinin sahibini kısa süre dışarıda bırakabilmesidir.
  Kilit sayaçları bellekte tutulur; API yeniden başlayınca sıfırlanır.
- **Girdi:** bütün istekler doğrulama kurallarından geçer, istek gövdesi 4 MB ile sınırlıdır, arama metinleri
  düzenli ifade olarak yorumlanmaz, logo gerçekten açılabilen ve makul boyutta bir resim olmalıdır.
- **Yetki:** açıkça herkese açık bırakılmayan her uç oturum ister; herkese açık uçların listesi bir testle
  sabitlenmiştir (`tests/Vulu.Tests/GuvenlikTestleri.cs`). O dosyadaki testlerin her biri bir saldırı denemesidir.
- **Günlük:** başarısız girişler, hesap kilitleri, şifre değişiklikleri, vulu yöneticisinin yaptığı şifre
  sıfırlamaları ve yeniden sunulan yenileme tokenları API günlüğüne yazılır.

### Ters vekil arkasında

Adres başına sınırların çalışması için API'nin istemcinin gerçek adresini görmesi gerekir. `Vekil:AtlamaSayisi`
API'nin önündeki vekil sayısıdır; ayarlanmamışsa `X-Forwarded-*` başlıkları hiç okunmaz. Render'da doğru değer
3'tür (`render.yaml`): 1 ile denendiğinde API, kullanıcının adresi yerine Render'ın iç ağ adresini gördü; 3 ile
gerçek adresi görüyor ve istekle gönderilen sahte adresi kabul etmiyor. Barındırma ortamı değişirse vulu
yöneticisi **Sunucu** ekranından denetlemelidir: “Görülen adres” kendi internet adresi olmalı, “İşlenmeyen
vekil” sıfır olmalıdır.

### Bilinen sınırlar

- Vulu yöneticisi bir işletme yöneticisinin şifresini sıfırlayıp o hesapla işletmenin verisini görebilir; her
  sıfırlama günlüğe yazılır ama teknik olarak engellenmez.
- Silinen müşteri ve servis kayıtları veritabanında işaretli olarak kalır (geçmiş faturalar bozulmasın diye);
  kalıcı silme ve saklama süresi (KVKK) için bir düzenek yoktur.
- Veritabanı yedeği uygulamanın değil barındırma tarafının (MongoDB Atlas) işidir.
- Masaüstü uygulamasının açtığı PDF'ler geçici klasöre yazılır; uygulama açılırken ve kapanırken silinir.

## Teknolojiler

- .NET 10
- ASP.NET Core Web API
- MongoDB (`MongoDB.Driver`)
- Photino.NET + Blazor (masaüstü arayüzü)
- xUnit

## Proje yapısı

```text
src/
  Vulu.Api         Web API; veritabanına yalnızca bu proje erişir
  Vulu.Shared      API ile masaüstünün paylaştığı istek/yanıt türleri ve kurallar
  Vulu.Desktop     Photino.Blazor masaüstü istemcisi
tests/
  Vulu.Tests       Otomatik testler (237 test; GuvenlikTestleri.cs saldırı denemeleridir)
tools/
  Vulu.Kurulum     Kullanıcıya verilen kurulum programı
  ikon-uret.ps1    Logodan pencere ve exe simgesi üretir
paketle.ps1        Kurulum dosyasını üretir
Dockerfile         API'nin barındırma imajı
render.yaml        Render kurulum tanımı (Blueprint)
```

Derleme uyarı bırakmaz: çözümleyiciler açıktır ve uyarılar hata sayılır (`Directory.Build.props`,
`.editorconfig`).

## Geliştirme ortamında çalıştırma

Gerekenler: .NET 10 SDK ve bir MongoDB (bu bilgisayarda kurulu ya da MongoDB Atlas'ta).

Gizli ayarlar repoda tutulmaz. `src/Vulu.Api/appsettings.Local.ornek.json` dosyasını aynı klasöre
`appsettings.Local.json` adıyla kopyalayıp değerleri doldurun; bu dosya git'e girmez.

- `Jwt:Anahtar`: en az 32 karakterlik rastgele bir dize.
- `IlkAdmin`: ilk vulu yöneticisinin hesabı. Yalnızca veritabanında hiç vulu yöneticisi yokken oluşturulur;
  işletmeleri o açar.
- `Mongo:BaglantiDizesi`: yazılmazsa bu bilgisayardaki MongoDB kullanılır (`mongodb://localhost:27017`). Atlas'a
  bağlanmak için buraya Atlas bağlantı dizesi yazılır ve Atlas'ın "Network Access" listesine bu bilgisayarın
  adresi eklenir.

Canlı ortamda aynı ayarlar ortam değişkeniyle verilir (`Jwt__Anahtar`, `Mongo__BaglantiDizesi`, ...).

```bash
dotnet run --project src/Vulu.Api --launch-profile http
```

```bash
dotnet run --project src/Vulu.Desktop
```

Masaüstü uygulamasının bağlandığı API adresi `src/Vulu.Desktop/appsettings.json` içindedir.

## Personel

Her personel bir kullanıcı hesabıdır: ad soyad, telefon ve uzmanlık hesabın üzerinde durur, servis kayıtları ve
randevular bu hesaplara atanır. Hesapları yalnızca yönetici yönetir (Personel sayfası). Üzerinde iş geçmişi olan
hesap silinemez, devre dışı bırakılır; devre dışı hesap giriş yapamaz ve yeni iş atanamaz.

## Servis kaydı

- **Kayıt numarası ve cihaz bilgisi:** her kayıt işletme içinde yıllık sıralı bir numara alır (`SK-2026-00042`).
  Cihazın markası, modeli, seri numarası / IMEI'si, garanti durumu, teslim alınan aksesuarlar ve teslim alındığı
  andaki görünür durumu ayrı alanlardır. Seri numarası girilen cihazın önceki kayıtları ayrıntı penceresinde
  listelenir.
- **Durumlar:** Beklemede → Onay bekliyor → Devam ediyor → Tamamlandı → Teslim edildi; ayrıca İptal edildi.
  Faturası olan kayıt yeniden açılamaz ve iptal edilemez.
- **Fiyat teklifi:** teklif verilince kayıt “Onay bekliyor” olur. Müşteri onaylarsa iş başlar, reddederse kayıt
  iptal edilir; onayın nasıl alındığı not olarak yazılır.
- **Kullanılan parçalar:** kayda eklenen parça stoktan düşer, çıkarılırsa stoğa döner. Parçanın adı ve fiyatı
  kullanıldığı andaki değeriyle kayda kopyalanır. Fatura tutarı parçaları da kapsar; belgede parçalar ayrı
  satırlarda, kalan tutar işçilik olarak görünür.
- **Formlar:** servis kabul formu (cihaz alınırken) ve teslim formu (cihaz verilirken) PDF olarak açılır;
  ikisinde de imza alanları vardır. Kabul formunun altındaki koşullar İşletme sayfasından değiştirilir.
- **İşlem geçmişi:** kaydı kimin, ne zaman, nasıl değiştirdiği `islemGecmisi` koleksiyonunda tutulur; satırlar
  yalnızca eklenir. Personel kayda serbest not da ekleyebilir.
- **Müşteriyi bilgilendirme:** kaydın durumuna göre hazırlanan mesaj, personelin bilgisayarındaki WhatsApp ya da
  e-posta programında açılır; uygulama mesajı kendisi göndermez (SMS sağlayıcısı bağlı değildir). Bilgilendirme
  işlem geçmişine yazılır.

### Cihaz durumu sorgulama sayfası

API, oturum gerektirmeyen `/takip/{işletme kodu}` sayfasını sunar. Müşteri kabul formundaki takip koduyla
cihazının durumunu görür. Sayfa internete açıktır; bu yüzden yalnızca cihazın adını, kayıt numarasını ve durumu
gösterir (müşterinin adı, telefonu, adresi yer almaz), takip kodu tahmin edilemeyen 10 karakterdir ve sorgu
sayısı adres başına dakikada `Takip:DakikadaSorgu` (varsayılan 20) ile sınırlıdır. API başka bir alan adının
arkasındaysa müşteriye verilen bağlantının kökü `Takip:Adres` ayarıyla belirlenir.

## Stok

Stok kartlarını ve stok girişlerini işletme yöneticisi yönetir; bütün personel stoğu görür ve servis kaydında
parça kullanır. Stok adedi yalnızca atomik artırma/azaltma ile değişir: aynı anda gelen iki istek stokta olmayan
parçayı kullanamaz ve stok eksiye düşmez. Her değişiklik `stokHareketleri` koleksiyonuna yazılır. Stoğu kritik
seviyeye inen parçalar Stok sayfasında işaretlenir ve ana sayfada sayılır.

## Raporlar

Yönetici, seçtiği dönem için açılan/tamamlanan/iptal edilen iş sayısını, ortalama tamir süresini, kesilen ve
tahsil edilen tutarı, teknisyen ve ay bazında dökümü ve en çok kullanılan parçaları görür.

## Excel'e aktarma

Müşteri listesi, servis kayıtları (ekrandaki süzgeçle), stok ve dönem raporu Excel dosyası olarak alınabilir;
dosya İndirilenler klasörüne kaydedilir. Müşteri verisi içeren listeleri yalnızca yönetici aktarabilir. Dosyayı
API üretir (`Altyapi/ExcelYazici.cs`); dışarıdan bir kitaplık kullanılmaz. Kullanıcının yazdığı her şey hücreye
düz metin olarak konur: `=` ile başlayan bir müşteri adı ya da not Excel'de formül olarak çalışmaz.

## Veri geçişleri

Veri modeli değiştiğinde mevcut belgeleri yeni biçime taşıyan adımlar `src/Vulu.Api/Veri/Gecisler.cs` içindedir.
API açılışta uygulanmamış adımları sırayla çalıştırır; son uygulanan adımın numarası `sema` koleksiyonunda durur.
Yeni bir adım, listenin sonuna bir sonraki numarayla eklenir ve yarıda kesilip yeniden çalıştırılabilecek şekilde
yazılır.

## Fatura belgesi

Kesilen her faturaya, işletmenin kendi sırasında yıl içinde artan bir numara verilir (ön ek işletme kodundan
türetilir, örn. `DUZC-2026-000001`) ve panelden PDF olarak açılabilir. Belgenin başlığındaki ad, adres, vergi
bilgileri, logo ve KDV oranı işletmenin kendi kaydından gelir (İşletme sayfası). Fatura tutarı KDV dahil girilir;
belge matrahı ve KDV'yi bu orana göre ayırır.

Belge bilgilendirme amaçlıdır: e-Fatura / e-Arşiv Fatura değildir ve resmî fatura yerine geçmez.

PDF üretimi QuestPDF ile yapılır; Community lisansı yıllık geliri 1 milyon ABD dolarının altındaki işletmeler
için ücretsizdir.

## Dağıtım

Hedef: Windows 10 ve 11 (64 bit). API Render'da, veritabanı MongoDB Atlas'ta çalışır.

### Veritabanı (MongoDB Atlas)

1. Bir küme ve bir veritabanı kullanıcısı oluşturulur. Kullanıcının bütün veritabanlarında okuma, yazma ve
   yönetim yetkisi olmalıdır: API her işletme için ayrı bir veritabanı açar ve işletme silinince onu kaldırır.
   Bu yüzden küme başka bir projeyle paylaşılmamalıdır.
2. "Network Access" listesine `0.0.0.0/0` eklenir; Render'ın ücretsiz planında sabit çıkış adresi yoktur. Bu
   durumda veritabanını koruyan şey kullanıcı şifresidir; uzun ve rastgele olmalıdır.

Platform veritabanı (`vulu_platform`) ve işletme veritabanları (`isletme_<kod>`) ilk kullanımda kendiliğinden
oluşur; elle veritabanı açmak gerekmez.

### API (Render)

Render'da "New → Blueprint" ile bu depo seçilir; `render.yaml` servisi tanımlar ve iki değer ister:

- `Mongo__BaglantiDizesi`: Atlas bağlantı dizesi
- `IlkAdmin__Sifre`: ilk vulu yöneticisinin şifresi (en az 8 karakter); ilk girişten sonra uygulamadan
  değiştirilmelidir

`Jwt__Anahtar` Render tarafından rastgele üretilir. İsteğe bağlı olarak `Takip__Adres` ile müşteriye verilen
takip bağlantısının kökü değiştirilebilir (örn. `https://servis.ornek.com`). Sağlık kontrolü `/api/saglik/canli`
adresidir. `main` dalına gönderilen her commit yeniden yayımlanır.

Bilinmesi gerekenler:

- `render.yaml` bölge belirtmez; Render varsayılan bölgeyi seçer. API ile veritabanı aynı bölgede olursa
  yanıtlar belirgin hızlanır. Bölge servis kurulduktan sonra değiştirilemez.
- Ücretsiz planda servis 15 dakika istek almayınca uyur; sonraki ilk istek bir dakika kadar sürebilir. Giriş
  ekranı bu sırada beklediğini söyler.

### Masaüstü

```bash
powershell -ExecutionPolicy Bypass -File .\paketle.ps1 -ApiAdresi https://<servis-adi>.onrender.com/ -Surum 0.2.0
```

`dist\Vulu-Kurulum.exe` dosyasını üretir; kullanıcıya verilen tek dosya budur. Çalıştırılınca:

- uygulamayı `%LOCALAPPDATA%\Programs\Vulu` klasörüne kurar (yönetici izni istemez),
- Başlat menüsüne ve masaüstüne kısayol ekler,
- "Program ekle/kaldır" listesine kaydeder; oradan kaldırılınca klasör, kısayollar ve kayıt silinir,
- yeniden çalıştırılırsa kurulu sürümün üzerine yazar.

Kurulum programı bu depodadır (`tools/Vulu.Kurulum`); dışarıdan bir kurulum aracı gerekmez. Uygulamayı içinde
taşır, .NET kurulumu gerektirmez ve gizli ayar içermez; içindeki tek ayar API adresidir. `/sessiz` seçeneğiyle
soru sormadan kurar. Uygulamanın çalışması için bilgisayarda Microsoft Edge WebView2 Runtime bulunmalıdır
(Windows 11'de hazır gelir); yoksa kurulum bunu söyler ve indirme adresini gösterir. API adresi `https` olmak
zorundadır. Dosyalar imzalı değildir; Windows ilk çalıştırmada "bilinmeyen yayıncı" uyarısı gösterebilir.

Kurulum dosyası GitHub'da sürüm olarak yayımlanır; dosyanın adı `Vulu-Kurulum.exe` kalmalıdır:

```bash
gh release create v0.4.0 dist\Vulu-Kurulum.exe --target main --title "0.4.0"
```

### Otomatik güncelleme

Kurulu uygulama her açılışta GitHub'a bu deponun en son sürümünü sorar (`Servisler/Guncelleyici.cs`). Daha yeni
bir sürüm varsa giriş formu yerine güncelleme ekranı gelir; güncelleme **zorunludur**. Uygulama kurulum
dosyasını indirir, kurulumu güncelleme kipinde başlatıp kapanır; kurulum dosyaları değiştirir ve uygulamayı
yeniden açar. Sürüm numarası başka hiçbir yerde tutulmaz: yeni sürümü yayımlamak, onu bütün kurulu kopyalara
göndermek demektir. Bu yüzden bir sürüm, yayımlanmadan önce denenmiş olmalıdır.

- Dosya yalnızca bu deponun sürüm sayfasından, şifreli bağlantıyla indirilir ve GitHub'ın bildirdiği boyut ile
  SHA-256 özetiyle eşleşmeden çalıştırılmaz. Güven GitHub hesabına dayanır: hesap ele geçirilirse saldırgan
  bütün kurulu kopyalara kendi programını gönderebilir. Hesapta iki adımlı doğrulama açık olmalıdır.
- Sürüm bilgisine ulaşılamazsa (internet yok, GitHub yanıt vermiyor) kontrol atlanır ve giriş yapılabilir.
- Yalnızca kurulum programının yerleştirdiği kopya kendini günceller; geliştirme sırasında çalıştırılan kopya
  güncelleme aramaz.
- Depo herkese açık olmalıdır; özel depoda sürüm bilgisi dışarıdan okunamaz.

API ile masaüstü arasındaki biçim geriye uyumsuz değişirse `Vulu.Shared/SaglikYaniti.cs` içindeki
`ApiSozlesmesi.Surum` artırılır; eski masaüstü sürümleri girişte güncelleme uyarısı verir.

## Testler

```bash
dotnet test
```

Testler bu bilgisayarda çalışan bir MongoDB ister. Adres `VULU_TEST_MONGO` ortam değişkeninden okunur, yoksa
`mongodb://localhost:27017` kullanılır; `appsettings.Local.json` içindeki bağlantı dizesi testlerde kullanılmaz,
yani testler canlı veritabanına dokunmaz. Her test kendi geçici veritabanını açıp siler.

## Durum

Sistem canlıdadır: API Render'da, veritabanı Atlas'ta çalışır ve masaüstü uygulaması GitHub sürümü olarak
dağıtılır. Henüz olmayanlar:

- Otomatik SMS (bir SMS sağlayıcısı bağlı değildir; bilgilendirme personelin WhatsApp ya da e-posta programı
  üzerinden yapılır).
- İmzalı exe.
- e-Fatura / e-Arşiv bağlantısı.

## Lisans

[GNU Affero Genel Kamu Lisansı v3.0](LICENSE) (AGPL-3.0). Yazılımı değiştirip bir ağ üzerinden başkalarına
hizmet olarak sunan herkes, değiştirdiği kaynak kodu da aynı lisansla o kullanıcılara açmak zorundadır.

Logo `assets/logo.svg` dosyasındadır. Değiştirilirse pencere ve exe simgesi
`tools/ikon-uret.ps1` ile yeniden üretilir (`assets/logo.ico`).
