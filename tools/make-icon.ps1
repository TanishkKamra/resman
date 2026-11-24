# Regenerates assets\resman.ico from the same glyph the tray uses. Run after .\build.ps1, then build again.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
$asm = [Reflection.Assembly]::LoadFrom((Join-Path $root 'build\ResMan.exe'))
$render = $asm.GetType('ResMan.TrayApp').GetMethod('RenderGlyph')

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = $render.Invoke($null, @($s, $true, $false))
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

New-Item -ItemType Directory -Force -Path (Join-Path $root 'assets') | Out-Null
$out = New-Object IO.BinaryWriter([IO.File]::Create((Join-Path $root 'assets\resman.ico')))
try {
    $out.Write([uint16]0); $out.Write([uint16]1); $out.Write([uint16]$sizes.Count)  # ICONDIR
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {                                         # ICONDIRENTRY
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $out.Write([byte]$dim); $out.Write([byte]$dim); $out.Write([byte]0); $out.Write([byte]0)
        $out.Write([uint16]1); $out.Write([uint16]32)
        $out.Write([uint32]$images[$i].Length); $out.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $out.Write($img) }
}
finally { $out.Dispose() }
Write-Host "Wrote $(Join-Path $root 'assets\resman.ico')"
