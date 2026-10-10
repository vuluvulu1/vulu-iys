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

## Test paketi

Tek tıkla denemek için:

```bash
powershell -ExecutionPolicy Bypass -File .\paketle.ps1
```

`dist\Vulu\Vulu.exe` çift tıklanınca API'yi arka planda başlatır, masaüstü uygulamasını açar ve pencere
kapanınca API'yi kapatır. API çıktısı `dist\Vulu\api.log` dosyasına yazılır. Paket bu makinedeki .NET 10,
yerel MongoDB ile çalışır ve `appsettings.Local.json` içindeki gizli ayarları taşır; dağıtım paketi değildir,
paylaşılmamalıdır.

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
- [ ] 5. Teknisyenler (uç noktalar + ekran)
- [ ] 6. Servis kayıtları ve fatura (uç noktalar + ekran)
- [ ] 7. Randevular (uç noktalar + ekran)
- [ ] 8. Dağıtım paketi

Logo `assets/logo.svg` dosyasındadır. Değiştirilirse pencere ve exe simgesi
`tools/ikon-uret.ps1` ile yeniden üretilir (`assets/logo.ico`).
