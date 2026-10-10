# assets\logo.svg ile ayni cizimi GDI+ ile cizip assets\logo.ico uretir (16-256 px).
# Logo degisirse buradaki koordinatlar da guncellenmeli.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Renk([string]$hex, [double]$alfa = 1) {
    $r = [Convert]::ToInt32($hex.Substring(1, 2), 16)
    $g = [Convert]::ToInt32($hex.Substring(3, 2), 16)
    $b = [Convert]::ToInt32($hex.Substring(5, 2), 16)
    [System.Drawing.Color]::FromArgb([int](255 * $alfa), $r, $g, $b)
}

function Noktalar([double[]]$koordinatlar, [double]$kaydir = 0) {
    $liste = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    for ($i = 0; $i -lt $koordinatlar.Length; $i += 2) {
        $liste.Add((New-Object System.Drawing.PointF ([single]($koordinatlar[$i] + $kaydir)), ([single]($koordinatlar[$i + 1] + $kaydir))))
    }
    , $liste.ToArray()
}

$v      = 72,110, 128,110, 192,265, 256,110, 312,110, 192,400
$vSol   = 72,110, 128,110, 192,265, 192,400
$vSag   = 192,265, 256,110, 312,110, 192,400
$l      = 308,110, 368,110, 271.2,344, 440,344, 440,400, 192,400
$lAyak  = 271.2,344, 440,344, 440,400, 248,400
$serit  = 312,110, 326,110, 206,400, 192,400
$somun  = 406,182.7, 440,202.35, 440,241.65, 406,261.3, 372,241.65, 372,202.35
$delik  = 406,205, 420.7,213.5, 420.7,230.5, 406,239, 391.3,230.5, 391.3,213.5
$somunSag = 406,182.7, 440,202.35, 440,241.65, 406,261.3, 406,239, 420.7,230.5, 420.7,213.5, 406,205

function Doldur($g, [string]$hex, [double[]]$koordinatlar, [double]$alfa = 1, [double]$kaydir = 0) {
    $firca = New-Object System.Drawing.SolidBrush (Renk $hex $alfa)
    $g.FillPolygon($firca, (Noktalar $koordinatlar $kaydir))
    $firca.Dispose()
}

function Ciz([int]$boyut) {
    $bmp = New-Object System.Drawing.Bitmap $boyut, $boyut, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform($boyut / 512, $boyut / 512)

    $g.Clear((Renk '#2b1a10'))
    $ic = New-Object System.Drawing.RectangleF 22, 22, 468, 468
    $zemin = New-Object System.Drawing.Drawing2D.LinearGradientBrush $ic, (Renk '#5a3923'), (Renk '#3d2616'), 45
    $g.FillRectangle($zemin, $ic)
    $zemin.Dispose()

    Doldur $g '#1f120a' $v 0.55 14
    Doldur $g '#1f120a' $l 0.55 14
    Doldur $g '#1f120a' $somun 0.55 14

    Doldur $g '#e9c699' $l
    Doldur $g '#cfa575' $lAyak
    Doldur $g '#2b1a10' $serit 0.28
    Doldur $g '#b5804f' $vSol
    Doldur $g '#8a5c36' $vSag

    $yol = New-Object System.Drawing.Drawing2D.GraphicsPath ([System.Drawing.Drawing2D.FillMode]::Alternate)
    $yol.AddPolygon((Noktalar $somun))
    $yol.AddPolygon((Noktalar $delik))
    $firca = New-Object System.Drawing.SolidBrush (Renk '#b5804f')
    $g.FillPath($firca, $yol)
    $firca.Dispose()
    $yol.Dispose()
    Doldur $g '#8a5c36' $somunSag

    $g.Dispose()
    $akis = New-Object System.IO.MemoryStream
    $bmp.Save($akis, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $akis.ToArray()
}

$boyutlar = 16, 24, 32, 48, 64, 128, 256
$resimler = @()
foreach ($boyut in $boyutlar) { $resimler += , (Ciz $boyut) }

$hedef = Join-Path $PSScriptRoot '..\assets\logo.ico'
$cikti = New-Object System.IO.MemoryStream
$yazici = New-Object System.IO.BinaryWriter $cikti
$yazici.Write([uint16]0); $yazici.Write([uint16]1); $yazici.Write([uint16]$boyutlar.Count)

$konum = 6 + 16 * $boyutlar.Count
for ($i = 0; $i -lt $boyutlar.Count; $i++) {
    $kenar = if ($boyutlar[$i] -ge 256) { 0 } else { $boyutlar[$i] }
    $yazici.Write([byte]$kenar); $yazici.Write([byte]$kenar)
    $yazici.Write([byte]0); $yazici.Write([byte]0)
    $yazici.Write([uint16]1); $yazici.Write([uint16]32)
    $yazici.Write([uint32]$resimler[$i].Length); $yazici.Write([uint32]$konum)
    $konum += $resimler[$i].Length
}
foreach ($resim in $resimler) { $yazici.Write([byte[]]$resim) }
$yazici.Flush()
[System.IO.File]::WriteAllBytes($hedef, $cikti.ToArray())
Write-Host "Yazildi: $((Resolve-Path $hedef).Path)"
