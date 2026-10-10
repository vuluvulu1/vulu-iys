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
  veritabanına bağlanır (`Altyapi/IsletmeCozucu.cs`); askıya alınan işletmenin açık oturumları da reddedilir.
- Veri geçişleri ve indexler açılışta her işletme veritabanına ayrı ayrı uygulanır.

### Vulu yöneticisinin işlemleri

- **Personel sınırı:** her işletmenin aynı anda aktif olabilecek hesap sayısı sınırlıdır (devre dışı hesaplar
  sayılmaz). Sınır dolunca işletme yöneticisi yeni hesap açamaz.
- Anlaşma süresi ve ödemeler sistemde tutulmaz; işletmelerle kâğıt üzerinde, uzun dönemli anlaşılır. Anlaşması
  biten işletme askıya alınır.
- **Yönetici şifresi sıfırlama:** yalnızca işletmenin yönetici hesapları için. Bu, vulu yöneticisinin o hesapla
  giriş yapıp işletmenin verisini görebileceği anlamına gelir; her sıfırlama API günlüğüne yazılır.
- **Kalıcı silme:** işletme önce askıya alınmış olmalı ve işletme kodu yeniden yazılmalıdır. İşletmenin
  veritabanı geri dönüşsüz silinir.
- **Vulu yöneticileri:** birden fazla olabilir; her biri diğerlerini yönetir. Kendi hesabını silemez ve en az
  bir aktif yönetici kalmalıdır.

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
- `IlkAdmin__Sifre`: ilk yönetici şifresi (en az 8 karakter)

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
- [ ] 8. Dağıtım paketi

Logo `assets/logo.svg` dosyasındadır. Değiştirilirse pencere ve exe simgesi
`tools/ikon-uret.ps1` ile yeniden üretilir (`assets/logo.ico`).
