param(
    [Parameter(Mandatory)][string]$AssemblyPath
)

# Headless checks only: do not create App, SettingsStore, windows, or tray entries.
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AssemblyPath).Path)
$iconType = $assembly.GetType('RoomSwitcherTray.Core.ScenarioIcon', $true)
$artworkType = $assembly.GetType('RoomSwitcherTray.Core.Services.ScenarioArtwork', $true)
$scenarioType = $assembly.GetType('RoomSwitcherTray.Core.ScenarioDefinition', $true)
$render = $artworkType.GetMethod('Render')
$palette = @($artworkType.GetField('Palette').GetValue($null))
$expected = @(
    'Television', 'Desktop', 'Laptop', 'DualMonitors',
    'LaptopAndMonitor', 'TripleMonitors', 'QuadMonitors', 'Gamepad',
    'Sofa', 'Speakers', 'Headphones', 'Projector',
    'Microphone', 'Webcam', 'Deck', 'DesktopAudio'
)
if (($palette -join ',') -ne ($expected -join ',')) { throw 'Incorrect palette order or count.' }
$legacy = @('Letters', 'Desktop', 'Television', 'Sofa', 'Gamepad')
for ($i = 0; $i -lt $legacy.Count; $i++) {
    if ([int][Enum]::Parse($iconType, $legacy[$i]) -ne $i) { throw 'Legacy icon IDs changed.' }
}
$resources = @($assembly.GetManifestResourceNames() | Where-Object { $_ -like '*ScenarioIcons*' })
if ($resources.Count -ne 112) { throw "Expected 112 embedded PNGs, found $($resources.Count)." }

$count = 0
foreach ($name in $expected) {
    $icon = [Enum]::Parse($iconType, $name)
    foreach ($size in @(16, 20, 24, 32, 48, 64, 96)) {
        $stream = $assembly.GetManifestResourceStream("RoomSwitcherTray.Core.Assets.ScenarioIcons.s$size.$name.png")
        $mask = [Drawing.Bitmap]::new($stream)
        try {
            foreach ($color in @([Drawing.Color]::Black, [Drawing.Color]::White)) {
                $bitmap = $render.Invoke($null, @($icon, '', $color, $size))
                try {
                    if ($bitmap.Width -ne $size -or $bitmap.Height -ne $size) { throw "Wrong size: $name" }
                    $visible = 0
                    for ($y = 0; $y -lt $size; $y++) {
                        for ($x = 0; $x -lt $size; $x++) {
                            $pixel = $bitmap.GetPixel($x, $y)
                            if ($pixel.A -ne $mask.GetPixel($x, $y).A) { throw "Altered alpha mask: $name/$size" }
                            if ($pixel.A -gt 0) {
                                $visible++
                                # GDI+ can round premultiplied white down by one level.
                                if ([Math]::Abs($pixel.R - $color.R) -gt 1 -or
                                    [Math]::Abs($pixel.G - $color.G) -gt 1 -or
                                    [Math]::Abs($pixel.B - $color.B) -gt 1) {
                                    throw "Wrong recoloring: $name/$size"
                                }
                            }
                        }
                    }
                    if ($visible -eq 0 -or $visible -eq $size * $size) { throw "Empty/opaque icon: $name/$size" }
                    $count++
                }
                finally { $bitmap.Dispose() }
            }
        }
        finally { $mask.Dispose(); $stream.Dispose() }
    }
}

$lettersIcon = [Enum]::Parse($iconType, 'Letters')
$normalize = $scenarioType.GetMethod('MakeIconLetters')
foreach ($sample in @('pc', 'ка', 'ШЩ', 'II', '1a2б', '')) {
    $normalized = $normalize.Invoke($null, @($sample))
    if ($normalized.Length -gt 2 -or $normalized -cne $normalized.ToUpperInvariant()) {
        throw "Invalid letter normalization: $sample"
    }
    $bitmap = $render.Invoke($null, @($lettersIcon, $sample, [Drawing.Color]::White, 32))
    try {
        $minX = 32; $maxX = -1; $minY = 32; $maxY = -1
        for ($y = 0; $y -lt 32; $y++) {
            for ($x = 0; $x -lt 32; $x++) {
                if ($bitmap.GetPixel($x, $y).A -gt 16) {
                    $minX = [Math]::Min($minX, $x); $maxX = [Math]::Max($maxX, $x)
                    $minY = [Math]::Min($minY, $y); $maxY = [Math]::Max($maxY, $y)
                }
            }
        }
        if ($maxX -lt 0 -or $minX -lt 1 -or $maxX -gt 30 -or $minY -lt 1 -or $maxY -gt 30) {
            throw "Missing/clipped letters: $sample"
        }
        if ($sample -eq 'pc' -and ($maxX - $minX) -le ($maxY - $minY)) {
            throw 'PC is vertically stretched.'
        }
    }
    finally { $bitmap.Dispose() }
}
"PASS: $count icon renders, 6 letter samples, 112 embedded masks, palette order and legacy IDs."
