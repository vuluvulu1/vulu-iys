# Test paketi üretir: dist\Vulu\Vulu.exe çift tıklanınca API'yi ve masaüstü uygulamasını birlikte açar.
# Paket bu makinedeki .NET 10 ve yerel MongoDB ile çalışır, gizli ayarları (appsettings.Local.json) içerir;
# dağıtım paketi değildir, paylaşılmamalıdır.

$ErrorActionPreference = 'Stop'
$kok = $PSScriptRoot
$hedef = Join-Path $kok 'dist\Vulu'

# Açık kalan bir paket dosyaları kilitler.
Get-Process -Name 'Vulu', 'Vulu.Api', 'Vulu.Desktop' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($hedef, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force

if (Test-Path $hedef) { Remove-Item $hedef -Recurse -Force }

$projeler = @(
    @{ Yol = 'tools\Vulu.Baslatici'; Cikti = $hedef },
    @{ Yol = 'src\Vulu.Api';         Cikti = Join-Path $hedef 'api' },
    @{ Yol = 'src\Vulu.Desktop';     Cikti = Join-Path $hedef 'desktop' }
)

foreach ($proje in $projeler) {
    dotnet publish (Join-Path $kok $proje.Yol) -c Release -o $proje.Cikti --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "$($proje.Yol) yayımlanamadı." }
}

Write-Host "Hazır: $(Join-Path $hedef 'Vulu.exe')"
