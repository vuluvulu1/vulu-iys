# vulu Teknik Servis Yönetim Sistemi

Teknik servis işletmelerine hizmet olarak sunulan yönetim paneli: müşteri, personel, servis kaydı, randevu ve
fatura. Tek bir API birden çok işletmeye hizmet verir; her işletmenin verisi kendi veritabanında durur.

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
API'nin önündeki vekil sayısıdır (Docker imajında 1); ayarlanmamışsa `X-Forwarded-*` başlıkları hiç okunmaz.
Canlıya çıkınca vulu yöneticisi **Sunucu** ekranından denetlemelidir: “Görülen adres” kendi internet adresi
değil de barındırma şirketinin adresiyse değer bir artırılır. Bu, canlı ortamda denenmeden doğrulanamaz.

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
  Vulu.Api       Web API; veritabanına yalnızca bu proje erişir
  Vulu.Shared    API ile masaüstünün paylaştığı DTO ve enum'lar
  Vulu.Desktop   Photino.Blazor masaüstü istemcisi
tests/
  Vulu.Tests     Testler
```

## Çalıştırma

Yerelde `mongodb://localhost:27017` adresinde bir MongoDB gerekir
(`src/Vulu.Api/appsettings.Development.json`).

Gizli ayarlar repoda tutulmaz. `src/Vulu.Api/appsettings.Local.ornek.json` dosyasını aynı klasöre
`appsettings.Local.json` adıyla kopyalayıp değerleri doldurun; bu dosya git'e girmez.

`IlkAdmin` ilk vulu yöneticisinin hesabıdır ve yalnızca hiç vulu yöneticisi yokken oluşturulur; işletmeleri o
açar. Canlı ortamda aynı ayarlar ortam değişkeniyle verilir (`Jwt__Anahtar`, `Mongo__BaglantiDizesi`, ...).

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

## Test paketi

Tek tıkla denemek için:

```bash
powershell -ExecutionPolicy Bypass -File .\paketle.ps1
```

`dist\Vulu\Vulu.exe` çift tıklanınca API'yi arka planda başlatır, masaüstü uygulamasını açar ve pencere
kapanınca API'yi kapatır. API çıktısı `dist\Vulu\api.log` dosyasına yazılır. Paket bu makinedeki .NET 10,
yerel MongoDB ile çalışır ve `appsettings.Local.json` içindeki gizli ayarları taşır; dağıtım paketi değildir,
paylaşılmamalıdır.

## Dağıtım

Hedef: Windows 10 ve 11 (64 bit). API merkezi bir sunucuda (Render) çalışır, veritabanı MongoDB Atlas'tadır;
masaüstü uygulaması yalnızca API adresini bilir, veritabanına doğrudan bağlanmaz.

### API (Render)

`render.yaml` ve `Dockerfile` API'yi kurar. Render panelinde elle girilecek değerler:

- `Mongo__BaglantiDizesi`: Atlas bağlantı dizesi
- `IlkAdmin__Sifre`: ilk yönetici şifresi (en az 8 karakter); ilk girişten sonra uygulamadan değiştirin
- `Takip__Adres` (isteğe bağlı): müşteriye verilen takip bağlantısının kökü, örn. `https://servis.ornek.com`

`Jwt__Anahtar` Render tarafından rastgele üretilir. Sağlık kontrolü `/api/saglik/canli` adresidir.

### Masaüstü

```bash
powershell -ExecutionPolicy Bypass -File .\paketle.ps1 -Dagitim -ApiAdresi https://<servis-adi>.onrender.com/
```

`dist\Vulu-win-x64` klasörünü ve `.zip` dosyasını üretir. Paket .NET kurulumu gerektirmez ve gizli ayar
içermez. Çalışması için bilgisayarda Microsoft Edge WebView2 Runtime bulunmalıdır (Windows 11'de hazır gelir);
yoksa uygulama açılışta indirme adresini gösterir. API adresi `https` olmak zorundadır.

API ile masaüstü arasındaki biçim geriye uyumsuz değişirse `Vulu.Shared/SaglikYaniti.cs` içindeki
`ApiSozlesmesi.Surum` artırılır; eski masaüstü sürümleri girişte güncelleme uyarısı verir.

## Testler

```bash
dotnet test
```

Sağlık testi dışındaki testler gerçek bir MongoDB ister. Adres `VULU_TEST_MONGO` ortam değişkeninden okunur,
yoksa `mongodb://localhost:27017` kullanılır. Her test kendi geçici veritabanını açıp siler.

## Durum

- [x] 0. İskelet
- [x] 1. Veri modeli
- [x] 2. API çekirdeği (kimlik doğrulama, roller, doğrulama)
- [x] 3. Giriş ekranı, oturum yönetimi, kullanıcı yönetimi ekranı
- [x] 4. Müşteriler (uç noktalar + ekran)
- [x] 5. Teknisyenler (uç noktalar + ekran)
- [x] 6. Servis kayıtları ve fatura (uç noktalar + ekran)
- [x] 7. Randevular (uç noktalar + ekran)
- [x] 8. Çok işletmeli yapı, fatura belgesi, takvim
- [x] 9. Cihaz ayrıntısı, formlar, teklif, stok, işlem geçmişi, raporlar, takip sayfası, bildirim
- [ ] 10. Canlı ortama dağıtım (Render + Atlas)

Logo `assets/logo.svg` dosyasındadır. Değiştirilirse pencere ve exe simgesi
`tools/ikon-uret.ps1` ile yeniden üretilir (`assets/logo.ico`).
