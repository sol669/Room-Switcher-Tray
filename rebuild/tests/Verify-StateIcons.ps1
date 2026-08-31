param([Parameter(Mandatory)][string]$AssemblyPath, [Parameter(Mandatory)][string]$PreviewPath)
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$type = $assembly.GetType('RoomSwitcherTray.Core.Services.ScenarioArtwork', $true)
$sheet = [Drawing.Bitmap]::new(320, 160)
$graphics = [Drawing.Graphics]::FromImage($sheet)
$graphics.Clear([Drawing.Color]::White)
$dark = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(32, 32, 32))
$graphics.FillRectangle($dark, 0, 80, 320, 80)
$count = 0
try {
    $column = 0
    foreach ($method in @('RenderWarning','RenderRemote')) {
        foreach ($size in @(16,20,24,32,48,64)) {
            foreach ($color in @([Drawing.Color]::Black,[Drawing.Color]::White)) {
                $bitmap = $type.GetMethod($method).Invoke($null, @($color, $size))
                try {
                    $opaque = 0
                    for ($y=0; $y -lt $size; $y++) { for ($x=0; $x -lt $size; $x++) {
                        $pixel = $bitmap.GetPixel($x,$y)
                        if ($pixel.A -gt 64) { $opaque++ }
                        if ($pixel.A -gt 200) {
                            if ([Math]::Abs($pixel.R-$color.R) -gt 1 -or
                                [Math]::Abs($pixel.G-$color.G) -gt 1 -or
                                [Math]::Abs($pixel.B-$color.B) -gt 1) { throw 'Icon is not monochrome.' }
                        }
                    } }
                    if ($opaque -lt 5 -or $opaque -gt $size*$size/2) { throw "Invalid icon coverage: $method/$size" }
                    if ($bitmap.GetPixel(0,0).A -ne 0) { throw 'Opaque icon background.' }
                    if ($size -in @(16,32,64)) {
                        $x = $column * 160 + @{16=4;32=32;64=80}[$size]
                        $y = if ($color.R -eq 0) { 8 } else { 88 }
                        $graphics.DrawImageUnscaled($bitmap,$x,$y)
                    }
                    $count++
                } finally { $bitmap.Dispose() }
            }
        }
        $column++
    }
    $sheet.Save([IO.Path]::GetFullPath($PreviewPath), [Drawing.Imaging.ImageFormat]::Png)
    "PASS: $count monochrome warning/RDP renders; transparent backgrounds; sizes 16–64."
} finally { $dark.Dispose(); $graphics.Dispose(); $sheet.Dispose() }
