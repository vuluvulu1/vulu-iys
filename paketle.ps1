# İki tür paket üretir:
#
#   .\paketle.ps1
#       Test paketi: dist\Vulu\Vulu.exe çift tıklanınca API'yi ve masaüstü uygulamasını birlikte açar.
#       Bu makinedeki .NET 10 ile çalışır; gizli ayarları ve veritabanı bağlantısını (appsettings.Local.json) içerir;
#       paylaşılmamalıdır.
#
#   .\paketle.ps1 -Dagitim -ApiAdresi https://ornek.onrender.com/ -Surum 0.2.0
#       Dağıtım paketi: dist\Vulu-Kurulum.exe. Kullanıcıya verilen tek dosyadır; uygulamayı kurar, Başlat menüsü
#       ve masaüstü kısayolu oluşturur. .NET kurulumu gerektirmez (Windows 10/11, 64 bit), verilen adresteki
#       API'ye bağlanır ve içinde gizli ayar yoktur. Ara ürün olarak dist\Vulu-win-x64 klasörü ve .zip de kalır.

param(
    [switch]$Dagitim,
    [string]$ApiAdresi,
    [string]$Surum = '0.0.0'
)

$ErrorActionPreference = 'Stop'
$kok = $PSScriptRoot

function Yayimla([string]$proje, [string]$cikti, [string[]]$ekSecenekler = @()) {
    dotnet publish (Join-Path $kok $proje) -c Release -o $cikti --nologo -v quiet @ekSecenekler
    if ($LASTEXITCODE -ne 0) { throw "$proje yayımlanamadı." }
}

function Temizle([string]$klasor) {
    # Açık kalan bir paket dosyaları kilitler.
    Get-Process -Name 'Vulu', 'Vulu.Api', 'Vulu.Desktop' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($klasor, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force
    if (Test-Path $klasor) { Remove-Item $klasor -Recurse -Force }
}

if ($Dagitim) {
    $adres = $null
    if (-not [Uri]::TryCreate($ApiAdresi, [UriKind]::Absolute, [ref]$adres)) {
        throw "-ApiAdresi gerekli, örn: -ApiAdresi https://ornek.onrender.com/"
    }
    if ($adres.Scheme -ne 'https' -and -not $adres.IsLoopback) {
        throw "ApiAdresi https ile başlamalı: $ApiAdresi"
    }

    $hedef = Join-Path $kok 'dist\Vulu-win-x64'
    Temizle $hedef
    if ($Surum -notmatch '^\d+\.\d+\.\d+$') { throw "-Surum 1.2.3 biçiminde olmalı: $Surum" }

    Yayimla 'src\Vulu.Desktop' $hedef @('-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=none', "-p:Version=$Surum")

    @{ ApiAdresi = $adres.AbsoluteUri } | ConvertTo-Json |
        Set-Content (Join-Path $hedef 'appsettings.json') -Encoding utf8

    $zip = "$hedef.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $hedef '*') -DestinationPath $zip

    # Kurulum programı, az önce üretilen zip'i içine gömerek tek dosya olarak yayımlanır.
    $kurulumKlasoru = Join-Path $kok 'dist\kurulum'
    Temizle $kurulumKlasoru
    Yayimla 'tools\Vulu.Kurulum' $kurulumKlasoru @("-p:UygulamaZip=$zip", "-p:Version=$Surum")

    $kurulum = Join-Path $kok 'dist\Vulu-Kurulum.exe'
    Move-Item (Join-Path $kurulumKlasoru 'Vulu-Kurulum.exe') $kurulum -Force
    Remove-Item $kurulumKlasoru -Recurse -Force

    Write-Host "Hazır: $kurulum"
    return
}

$hedef = Join-Path $kok 'dist\Vulu'
Temizle $hedef
Yayimla 'tools\Vulu.Baslatici' $hedef
Yayimla 'src\Vulu.Api' (Join-Path $hedef 'api')
Yayimla 'src\Vulu.Desktop' (Join-Path $hedef 'desktop')

Write-Host "Hazır: $(Join-Path $hedef 'Vulu.exe')"
