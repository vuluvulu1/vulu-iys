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

Gizli ayarlar repoda tutulmaz; ilk kurulumda user-secrets ile verilir:

```bash
dotnet user-secrets set "Jwt:Anahtar" "<en az 32 karakterlik rastgele dize>" --project src/Vulu.Api
```

```bash
dotnet user-secrets set "IlkAdmin:KullaniciAdi" "admin" --project src/Vulu.Api
```

```bash
dotnet user-secrets set "IlkAdmin:Sifre" "<en az 8 karakter>" --project src/Vulu.Api
```

`IlkAdmin` hesabı yalnızca veritabanında hiç kullanıcı yokken oluşturulur. Canlı ortamda aynı ayarlar
ortam değişkeniyle verilir (`Jwt__Anahtar`, `Mongo__BaglantiDizesi`, ...).

```bash
dotnet run --project src/Vulu.Api --launch-profile http
```

```bash
dotnet run --project src/Vulu.Desktop
```

Masaüstü uygulamasının bağlandığı API adresi `src/Vulu.Desktop/appsettings.json` içindedir.

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
- [ ] 3. API modülleri
- [ ] 4. Testler
- [ ] 5. Masaüstü kabuğu
- [ ] 6. Ekranlar
- [ ] 7. Paketleme
