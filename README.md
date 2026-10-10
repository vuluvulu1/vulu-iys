# vulu Teknik Servis Yönetim Sistemi

Teknik servis süreçlerini (müşteri, teknisyen, servis kaydı, randevu, fatura) yöneten masaüstü uygulaması.

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

`IlkAdmin` hesabı yalnızca veritabanında hiç kullanıcı yokken oluşturulur. Canlı ortamda aynı ayarlar
ortam değişkeniyle verilir (`Jwt__Anahtar`, `Mongo__BaglantiDizesi`, ...).

```bash
dotnet run --project src/Vulu.Api --launch-profile http
```

```bash
dotnet run --project src/Vulu.Desktop
```

Masaüstü uygulamasının bağlandığı API adresi `src/Vulu.Desktop/appsettings.json` içindedir.

## Fatura belgesi

Kesilen her faturaya yıl içinde sıralı bir numara verilir (`VL-2026-000001`) ve panelden PDF olarak açılabilir.
Belgenin başlığındaki işletme bilgileri ve KDV oranı `src/Vulu.Api/appsettings.json` içindeki `Firma`
bölümünden gelir (barındırma ortamında `Firma__Ad`, `Firma__VergiNo`, ... ortam değişkenleri). Fatura tutarı KDV
dahil girilir; belge matrahı ve KDV'yi bu orana göre ayırır.

Belge bilgilendirme amaçlıdır: e-Fatura / e-Arşiv Fatura değildir ve resmî fatura yerine geçmez. Alttaki not
`Firma:BelgeNotu` ile değiştirilebilir.

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
