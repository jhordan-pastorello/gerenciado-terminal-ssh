Add-Type -AssemblyName System.Drawing

$outDir = Join-Path (Split-Path $PSScriptRoot -Parent) "Assets"
New-Item -ItemType Directory -Force $outDir | Out-Null

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $size / 256.0
    $g.ScaleTransform($scale, $scale)

    $rect = New-Object System.Drawing.RectangleF 8, 8, 240, 240
    $shape = New-RoundedPath 8 8 240 240 56

    # Deep black body with a faint teal vignette.
    $body = New-Object System.Drawing.Drawing2D.PathGradientBrush $shape
    $body.CenterColor = [System.Drawing.Color]::FromArgb(255, 14, 22, 21)
    $body.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(255, 3, 4, 5))
    $body.CenterPoint = New-Object System.Drawing.PointF 92, 82
    $g.FillPath($body, $shape)

    # Pane mosaic hint (the app's 2x2 tiling).
    $panePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(18, 143, 184, 176)), 2
    $panes = @(
        @(42, 42, 94, 94),
        @(146, 42, 94, 94),
        @(42, 146, 94, 94),
        @(146, 146, 94, 94)
    )
    foreach ($pane in $panes) {
        $panePath = New-RoundedPath $pane[0] $pane[1] $pane[2] $pane[3] 12
        $g.DrawPath($panePen, $panePath)
        $panePath.Dispose()
    }

    # Glass sheen.
    $g.SetClip($shape)
    $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        (New-Object System.Drawing.RectangleF 8, 8, 240, 120)),
        ([System.Drawing.Color]::FromArgb(18, 255, 255, 255)),
        ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)),
        90.0
    $g.FillRectangle($sheen, 8, 8, 240, 120)
    $g.ResetClip()

    # Neon terminal prompt.
    $family = New-Object System.Drawing.FontFamily "Consolas"
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $textPath.AddString(">_", $family, [int][System.Drawing.FontStyle]::Bold, 138,
        (New-Object System.Drawing.RectangleF 8, 22, 240, 205), $format)

    $glowOuter = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(16, 143, 184, 176)), 18
    $glowOuter.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($glowOuter, $textPath)

    $glowInner = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(35, 170, 207, 201)), 8
    $glowInner.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($glowInner, $textPath)

    $textBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        (New-Object System.Drawing.RectangleF 30, 50, 200, 160)),
        ([System.Drawing.Color]::FromArgb(255, 195, 225, 220)),
        ([System.Drawing.Color]::FromArgb(255, 111, 154, 147)),
        45.0
    $g.FillPath($textBrush, $textPath)

    $edgePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(50, 220, 235, 232)), 1.2
    $g.DrawPath($edgePen, $textPath)

    # Border gradient.
    $borderBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255, 62, 91, 86)),
        ([System.Drawing.Color]::FromArgb(255, 10, 15, 18)),
        45.0
    $borderPen = New-Object System.Drawing.Pen $borderBrush, 3
    $g.DrawPath($borderPen, $shape)

    # Small online indicator.
    $dotGlow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(28, 111, 191, 155))
    $g.FillEllipse($dotGlow, 184, 34, 40, 40)
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 111, 191, 155))
    $g.FillEllipse($dot, 196, 46, 16, 16)

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($s, $ms.ToArray())
    if ($s -eq 256) {
        $bmp.Save((Join-Path $env:TEMP "StorageTerm-preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $ms.Dispose()
    $bmp.Dispose()
}

$icoPath = Join-Path $outDir "StorageTerm.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$frames.Count)
$offset = 6 + (16 * $frames.Count)
foreach ($f in $frames) {
    $size = $f[0]
    $data = $f[1]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $bw.Write($f[1]) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Get-Item $icoPath | Select-Object FullName, Length
Write-Output ("preview=" + (Join-Path $env:TEMP "StorageTerm-preview.png"))

