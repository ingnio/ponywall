# Build PonyWall.Avalonia\Assets\firewall.ico from a source PNG.
#
# Generates a multi-resolution ICO file containing 16/24/32/48/64/128/256 px
# entries, each stored as an embedded PNG (Vista+ supports PNG-in-ICO natively
# and this avoids hand-rolling DIB encoding with the AND-mask quirk).
#
# The source PNG can be any aspect ratio; the image is scaled to fit inside
# each target square and padded with white (the source's own background
# happens to be near-white with flame glow, so white padding blends in).

param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Destination
)

Add-Type -AssemblyName System.Drawing

$original = [System.Drawing.Image]::FromFile($Source)
try {
    $srcW = $original.Width
    $srcH = $original.Height
    Write-Host ("Source: {0}x{1}" -f $srcW, $srcH)

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $pngs = New-Object 'System.Collections.Generic.List[byte[]]'

    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.InterpolationMode   = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode       = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode     = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.CompositingQuality  = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

                # Paint white background so we preserve the source's light feel.
                $g.Clear([System.Drawing.Color]::White)

                # Fit-inside scale + center.
                $scale   = [Math]::Min($size / $srcW, $size / $srcH)
                $drawW   = $srcW * $scale
                $drawH   = $srcH * $scale
                $offsetX = ($size - $drawW) / 2.0
                $offsetY = ($size - $drawH) / 2.0

                $g.DrawImage($original, [single]$offsetX, [single]$offsetY, [single]$drawW, [single]$drawH)
            } finally {
                $g.Dispose()
            }

            $ms = New-Object System.IO.MemoryStream
            try {
                $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
                $pngs.Add($ms.ToArray())
            } finally {
                $ms.Dispose()
            }
        } finally {
            $bmp.Dispose()
        }
    }
} finally {
    $original.Dispose()
}

# Now assemble the ICO file:
#   ICONDIR            (6 bytes)
#   ICONDIRENTRY * N   (16 bytes each)
#   PNG blob * N
$fs = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $fs
try {
    # ICONDIR
    $bw.Write([UInt16]0)            # Reserved
    $bw.Write([UInt16]1)            # Type: 1 = icon
    $bw.Write([UInt16]$sizes.Count) # Number of images

    $dataOffset = 6 + (16 * $sizes.Count)

    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size    = $sizes[$i]
        $pngSize = $pngs[$i].Length

        # Width/Height in a dir entry is a single byte; 0 is the encoding for 256.
        $w = if ($size -ge 256) { [byte]0 } else { [byte]$size }
        $h = if ($size -ge 256) { [byte]0 } else { [byte]$size }

        $bw.Write([byte]$w)            # Width
        $bw.Write([byte]$h)            # Height
        $bw.Write([byte]0)             # ColorCount (0 for >=256 colors)
        $bw.Write([byte]0)             # Reserved
        $bw.Write([UInt16]1)           # Color planes
        $bw.Write([UInt16]32)          # Bits per pixel
        $bw.Write([UInt32]$pngSize)    # Size of image data
        $bw.Write([UInt32]$dataOffset) # Offset from start of file

        $dataOffset += $pngSize
    }

    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $bw.Write($pngs[$i])
    }

    $bw.Flush()
    [System.IO.File]::WriteAllBytes($Destination, $fs.ToArray())
} finally {
    $bw.Dispose()
    $fs.Dispose()
}

$outBytes = (Get-Item $Destination).Length
Write-Host ("Wrote {0} ({1} bytes, {2} sizes)" -f $Destination, $outBytes, $sizes.Count)
