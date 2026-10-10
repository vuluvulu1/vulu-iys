# İki tür paket üretir:
#
#   .\paketle.ps1
#       Test paketi: dist\Vulu\Vulu.exe çift tıklanınca API'yi ve masaüstü uygulamasını birlikte açar.
#       Bu makinedeki .NET 10 ve yerel MongoDB ile çalışır, gizli ayarları (appsettings.Local.json) içerir;
#       paylaşılmamalıdır.
#
#   .\paketle.ps1 -Dagitim -ApiAdresi https://ornek.onrender.com/
#       Dağıtım paketi: dist\Vulu-win-x64 klasörü ve .zip dosyası. Yalnızca masaüstü uygulamasını içerir,
#       .NET kurulumu gerektirmez (Windows 10/11, 64 bit) ve verilen adresteki API'ye bağlanır.
#       İçinde gizli ayar yoktur.

param(
    [switch]$Dagitim,
    [string]$ApiAdresi
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
    Yayimla 'src\Vulu.Desktop' $hedef @('-r', 'win-x64', '--self-contained', 'true', '-p:DebugType=none')

    @{ ApiAdresi = $adres.AbsoluteUri } | ConvertTo-Json |
        Set-Content (Join-Path $hedef 'appsettings.json') -Encoding utf8

    $zip = "$hedef.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $hedef '*') -DestinationPath $zip

    Write-Host "Hazır: $(Join-Path $hedef 'Vulu.Desktop.exe')"
    Write-Host "Zip:   $zip"
    return
}

$hedef = Join-Path $kok 'dist\Vulu'
Temizle $hedef
Yayimla 'tools\Vulu.Baslatici' $hedef
Yayimla 'src\Vulu.Api' (Join-Path $hedef 'api')
Yayimla 'src\Vulu.Desktop' (Join-Path $hedef 'desktop')

Write-Host "Hazır: $(Join-Path $hedef 'Vulu.exe')"
