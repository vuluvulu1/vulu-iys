# Kullanıcıya verilen kurulum dosyasını üretir: dist\Vulu-Kurulum.exe
#
#   .\paketle.ps1 -ApiAdresi https://ornek.onrender.com/ -Surum 0.2.0
#
# Kurulum dosyası uygulamayı içinde taşır; çalıştırılınca kurar, Başlat menüsü ve masaüstü kısayolu oluşturur.
# .NET kurulumu gerektirmez (Windows 10/11, 64 bit), verilen adresteki API'ye bağlanır ve içinde gizli ayar yoktur.

param(
    [Parameter(Mandatory)][string]$ApiAdresi,
    [Parameter(Mandatory)][string]$Surum
)

$ErrorActionPreference = 'Stop'
$kok = $PSScriptRoot
$dist = Join-Path $kok 'dist'

$adres = $null
if (-not [Uri]::TryCreate($ApiAdresi, [UriKind]::Absolute, [ref]$adres)) {
    throw "ApiAdresi geçerli bir adres değil: $ApiAdresi"
}
# Şifre ve tokenlar bu bağlantıdan geçer; uygulama da şifresiz adresi reddeder.
if ($adres.Scheme -ne 'https' -and -not $adres.IsLoopback) {
    throw "ApiAdresi https ile başlamalı: $ApiAdresi"
}
if ($Surum -notmatch '^\d+\.\d+\.\d+$') {
    throw "Surum 1.2.3 biçiminde olmalı: $Surum"
}

function Yayimla([string]$proje, [string]$cikti, [string[]]$secenekler) {
    dotnet publish (Join-Path $kok $proje) -c Release -o $cikti --nologo -v quiet "-p:Version=$Surum" @secenekler
    if ($LASTEXITCODE -ne 0) { throw "$proje yayımlanamadı." }
}

# Ara ürünler geçici bir klasörde üretilir ve iş bitince silinir; dist içinde yalnızca kurulum dosyası kalır.
$ara = Join-Path $dist 'ara'
if (Test-Path $ara) { Remove-Item $ara -Recurse -Force }

try {
    $uygulama = Join-Path $ara 'uygulama'
    Yayimla 'src\Vulu.Desktop' $uygulama @('-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=none')

    @{ ApiAdresi = $adres.AbsoluteUri } | ConvertTo-Json |
        Set-Content (Join-Path $uygulama 'appsettings.json') -Encoding utf8

    $zip = Join-Path $ara 'uygulama.zip'
    Compress-Archive -Path (Join-Path $uygulama '*') -DestinationPath $zip

    # Kurulum programı, uygulamayı içine gömerek tek dosya olarak yayımlanır.
    $kurulumKlasoru = Join-Path $ara 'kurulum'
    Yayimla 'tools\Vulu.Kurulum' $kurulumKlasoru @("-p:UygulamaZip=$zip")

    $kurulum = Join-Path $dist 'Vulu-Kurulum.exe'
    Move-Item (Join-Path $kurulumKlasoru 'Vulu-Kurulum.exe') $kurulum -Force
    Write-Host "Hazır: $kurulum"
}
finally {
    if (Test-Path $ara) { Remove-Item $ara -Recurse -Force }
}
