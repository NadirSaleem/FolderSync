# Generates AppIcon.ico. The icon is drawn as vector shapes and rendered at each size,
# so it is regenerated from here rather than edited as a bitmap.
#
# The installed icon (v1.0.3.4) was produced with:
#
#   .\AppIcon.gen.ps1 -OutDir out `
#       -Fold 175 -ArrowCy 555 -ArrowTail 282 -ArrowTip 817 -HeadHalf 160 -ShaftHalf 79 -HeadLen 244 `
#       -CornerRadius 70 -ArrowRadius 28 -Stroke 42 -OutlineHex '#1E2A3A' -GlobalScale 1.0551 `
#       -RightBand -BandWidth 78 `
#       -FrontRect 70,127,707,954 -BackRect 317,68,954,895 `
#       -Gradient -FrontFrom '#FFE8A6' -FrontTo '#FFC933' -BackFrom '#A8D6FF' -BackTo '#3579D6' `
#       -ArrowFrom '#6FD977' -ArrowTo '#16702F' `
#       -Shadow -ShadowDx 22 -ShadowDy 26 -ShadowSpread 68 -ShadowAlpha 58
#
# The front sheet's two yellows are sampled from the Windows folder icon itself
# (imageres.dll, icon index 3, at 256px): its face runs #FFE8A6 at the top left to
# #FFC933 at the bottom right, the same direction as the shading here.
#
# Sheet rectangles and the band width match the proportions of the icon that preceded it.
# Each sheet and the arrow are lit from the top left and cast a shadow on what is behind
# them, which is what gives the stack its depth; the arrow's shadow is 60% of the sheets',
# since it sits closer to the front sheet than the sheets sit to each other.
param(
    [string]$OutDir,
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256),
    [int]$MasterSize = 1024,
    # design knobs
    [double]$CornerRadius = 70,
    [double]$ArrowRadius = 28,
    [double]$Stroke = 34,
    [double]$Fold = 120,
    [double]$ArrowCy = 540,
    [double]$ArrowTip = 900,
    [double]$ArrowTail = 150,
    [double]$ShaftHalf = 95,
    [double]$HeadHalf = 200,
    [double]$HeadLen = 270,
    [string]$OutlineHex = '#1E2A3A',
    [string]$ArrowHex = '#60D665',
    # scales the whole drawing about the canvas centre, to control how much of the
    # canvas the artwork fills (the current icon fills 95%)
    [double]$GlobalScale = 1.0,
    # draw the arrow on its own, so its silhouette can be measured without the sheets
    [switch]$ArrowOnly,
    # fill each sheet with a top-left to bottom-right gradient instead of a flat colour
    [switch]$Gradient,
    [string]$FrontFrom = '#FFFFFF', [string]$FrontTo = '#E4E4E8',
    [string]$BackFrom = '#71B5FD', [string]$BackTo = '#BFA6F0',
    # the arrow's own shading, used with -Gradient; lit from the same corner as the sheets
    [string]$ArrowFrom = '', [string]$ArrowTo = '',
    # flat fills, and the darker shade used for the folded flap
    [string]$FrontFillHex = '#DBECFE', [string]$FrontFoldHex = '#FFFFFF',
    [string]$BackFillHex = '#71B5FD', [string]$BackFoldHex = '#4BA4FC',
    # band of the darker shade down the inside of each sheet's right edge, as the current icon has
    [switch]$RightBand,
    [double]$BandWidth = 48,
    # each sheet's path rectangle, x0,y0,x1,y1 (the stroke straddles these edges)
    [double[]]$FrontRect = @(60, 230, 640, 960),
    [double[]]$BackRect = @(380, 60, 960, 790),
    # soft shadow cast by each sheet, which is what lifts the front one off the back one
    [switch]$Shadow,
    [double]$ShadowDx = 12, [double]$ShadowDy = 16,
    [double]$ShadowSpread = 40, [int]$ShadowAlpha = 30
)

Add-Type -AssemblyName System.Drawing

# ---------- the design, in a 1024 x 1024 space ----------
$D = 1024.0

function HexBrush([string]$hex) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return New-Object System.Drawing.SolidBrush $c
}

function Darken([string]$hex, [double]$factor = 0.88) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return [System.Drawing.Color]::FromArgb(
        [int][Math]::Round($c.R * $factor),
        [int][Math]::Round($c.G * $factor),
        [int][Math]::Round($c.B * $factor))
}

# Gradient running from a shape's top-left to its bottom-right. The vector is pushed a little
# past both corners so antialiased edge pixels can't sample outside it and wrap to the far colour.
function DiagBrush([double]$x0, [double]$y0, [double]$x1, [double]$y1, $c1, $c2) {
    if ($c1 -is [string]) { $c1 = [System.Drawing.ColorTranslator]::FromHtml($c1) }
    if ($c2 -is [string]) { $c2 = [System.Drawing.ColorTranslator]::FromHtml($c2) }
    $dx = ($x1 - $x0) * 0.06; $dy = ($y1 - $y0) * 0.06
    $p1 = New-Object System.Drawing.PointF (($x0 - $dx), ($y0 - $dy))
    $p2 = New-Object System.Drawing.PointF (($x1 + $dx), ($y1 + $dy))
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $p1, $p2, $c1, $c2
    $br.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
    return $br
}

# Rounds every corner of a polygon: each vertex is trimmed back along both of its
# edges and bridged with a quadratic Bezier (written as a cubic), which is what gives
# the shape its soft look rather than a mitred point.
function New-RoundedPolygon([double[][]]$pts, [double]$r) {
    $n = $pts.Count
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $first = $null
    for ($i = 0; $i -lt $n; $i++) {
        $prev = $pts[($i - 1 + $n) % $n]
        $cur = $pts[$i]
        $next = $pts[($i + 1) % $n]

        $v1x = $prev[0] - $cur[0]; $v1y = $prev[1] - $cur[1]
        $v2x = $next[0] - $cur[0]; $v2y = $next[1] - $cur[1]
        $l1 = [Math]::Sqrt($v1x * $v1x + $v1y * $v1y)
        $l2 = [Math]::Sqrt($v2x * $v2x + $v2y * $v2y)
        # never eat more than half of either edge
        $rr = [Math]::Min($r, [Math]::Min($l1 / 2.0, $l2 / 2.0))

        $a = New-Object System.Drawing.PointF (($cur[0] + $v1x / $l1 * $rr), ($cur[1] + $v1y / $l1 * $rr))
        $b = New-Object System.Drawing.PointF (($cur[0] + $v2x / $l2 * $rr), ($cur[1] + $v2y / $l2 * $rr))

        if ($null -eq $first) { $first = $a } else { $path.AddLine($last, $a) }

        # quadratic (control = the original vertex) expressed as a cubic
        $c1 = New-Object System.Drawing.PointF (($a.X + 2.0 / 3.0 * ($cur[0] - $a.X)), ($a.Y + 2.0 / 3.0 * ($cur[1] - $a.Y)))
        $c2 = New-Object System.Drawing.PointF (($b.X + 2.0 / 3.0 * ($cur[0] - $b.X)), ($b.Y + 2.0 / 3.0 * ($cur[1] - $b.Y)))
        $path.AddBezier($a, $c1, $c2, $b)
        $last = $b
    }
    $path.AddLine($last, $first)
    $path.CloseFigure()
    return $path
}

# A sheet of paper: rectangle with the top-right corner folded away.
function New-DocPath([double]$x0, [double]$y0, [double]$x1, [double]$y1, [double]$fold, [double]$r) {
    $pts = @(
        @($x0, $y0),
        @(($x1 - $fold), $y0),
        @($x1, ($y0 + $fold)),
        @($x1, $y1),
        @($x0, $y1)
    )
    return New-RoundedPolygon $pts $r
}

function New-FoldPath([double]$x1, [double]$y0, [double]$fold, [double]$r) {
    $pts = @(
        @(($x1 - $fold), $y0),
        @($x1, ($y0 + $fold)),
        @(($x1 - $fold), ($y0 + $fold))
    )
    return New-RoundedPolygon $pts ($r * 0.5)
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $s = $size / $D
    $g.ScaleTransform($s, $s)
    if ($GlobalScale -ne 1.0) {
        $c = $D / 2.0
        $g.TranslateTransform($c, $c)
        $g.ScaleTransform($GlobalScale, $GlobalScale)
        $g.TranslateTransform(-$c, -$c)
    }

    $outline = [System.Drawing.ColorTranslator]::FromHtml($OutlineHex)
    $pen = New-Object System.Drawing.Pen $outline, $Stroke
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    if ($Gradient) {
        # each sheet carries its own gradient; the folded flap is the same gradient a shade
        # darker, so it stays readable wherever the sheet's colour has got to
        $backFill = DiagBrush $BackRect[0] $BackRect[1] $BackRect[2] $BackRect[3] $BackFrom $BackTo
        $backFold = DiagBrush $BackRect[0] $BackRect[1] $BackRect[2] $BackRect[3] (Darken $BackFrom) (Darken $BackTo)
        $frontFill = DiagBrush $FrontRect[0] $FrontRect[1] $FrontRect[2] $FrontRect[3] $FrontFrom $FrontTo
        $frontFold = DiagBrush $FrontRect[0] $FrontRect[1] $FrontRect[2] $FrontRect[3] (Darken $FrontFrom) (Darken $FrontTo)
    }
    else {
        $backFill = HexBrush $BackFillHex
        $backFold = HexBrush $BackFoldHex
        $frontFill = HexBrush $FrontFillHex
        $frontFold = HexBrush $FrontFoldHex
    }

    # A soft shadow without a blur: the offset shape is stroked repeatedly with translucent
    # pens of shrinking width, so the alpha builds up towards the shape and falls away from it.
    function CastShadow($path, [double]$scale = 1.0) {
        if (-not $Shadow) { return }
        $sp = $path.Clone()
        $m = New-Object System.Drawing.Drawing2D.Matrix
        $m.Translate([float]($ShadowDx * $scale), [float]($ShadowDy * $scale))
        $sp.Transform($m)

        $steps = 6
        for ($i = $steps; $i -ge 1; $i--) {
            $w = $ShadowSpread * $scale * $i / $steps
            $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb($ShadowAlpha, 10, 20, 35)), $w
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $g.DrawPath($pen, $sp)
            $pen.Dispose()
        }
        # the core, at exactly the alpha the stacked strokes build up to, so there is no step at the edge
        $core = [int](255 * (1 - [Math]::Pow(1 - $ShadowAlpha / 255.0, $steps)))
        if ($core -gt 255) { $core = 255 }
        $br = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($core, 10, 20, 35))
        $g.FillPath($br, $sp)
        $br.Dispose(); $sp.Dispose(); $m.Dispose()
    }

    # the band sits inside the sheet, so it is clipped to the sheet's own outline; the
    # outline is stroked afterwards and hides the clipped edge
    function Band($path, $brush, [double]$x1, [double]$y0, [double]$y1) {
        if (-not $RightBand) { return }
        $g.SetClip($path)
        $g.FillRectangle($brush, [float]($x1 - $BandWidth), [float]$y0, [float]$BandWidth, [float]($y1 - $y0))
        $g.ResetClip()
    }
    $arrowFill = HexBrush $ArrowHex

    $fold = $Fold

    if (-not $ArrowOnly) {
    # back sheet
    $back = New-DocPath $BackRect[0] $BackRect[1] $BackRect[2] $BackRect[3] $fold $CornerRadius
    CastShadow $back
    $g.FillPath($backFill, $back)
    Band $back $backFold $BackRect[2] $BackRect[1] $BackRect[3]
    $g.DrawPath($pen, $back)
    $bf = New-FoldPath $BackRect[2] $BackRect[1] $fold $CornerRadius
    $g.FillPath($backFold, $bf); $g.DrawPath($pen, $bf)

    # front sheet
    $front = New-DocPath $FrontRect[0] $FrontRect[1] $FrontRect[2] $FrontRect[3] $fold $CornerRadius
    CastShadow $front
    $g.FillPath($frontFill, $front)
    Band $front $frontFold $FrontRect[2] $FrontRect[1] $FrontRect[3]
    $g.DrawPath($pen, $front)
    $ff = New-FoldPath $FrontRect[2] $FrontRect[1] $fold $CornerRadius
    $g.FillPath($frontFold, $ff); $g.DrawPath($pen, $ff)
    }

    # arrow, on top of both
    $cy = $ArrowCy
    $hx = $ArrowTip - $HeadLen
    $arrow = New-RoundedPolygon @(
        @($ArrowTail, ($cy - $ShaftHalf)),
        @($hx, ($cy - $ShaftHalf)),
        @($hx, ($cy - $HeadHalf)),
        @($ArrowTip, $cy),
        @($hx, ($cy + $HeadHalf)),
        @($hx, ($cy + $ShaftHalf)),
        @($ArrowTail, ($cy + $ShaftHalf))
    ) $ArrowRadius
    if ($ArrowFrom -and $ArrowTo) {
        $arrowFill = DiagBrush $ArrowTail ($cy - $HeadHalf) $ArrowTip ($cy + $HeadHalf) $ArrowFrom $ArrowTo
    }
    # the arrow sits just above the front sheet, so its shadow is shorter than the sheets' own
    CastShadow $arrow 0.6
    $g.FillPath($arrowFill, $arrow); $g.DrawPath($pen, $arrow)

    $g.Dispose()
    return $bmp
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# master + per-size previews
$master = Render $MasterSize
$master.Save((Join-Path $OutDir 'rounded-master.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$master.Dispose()

$pngs = @()
foreach ($s in $Sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $s; Bytes = $ms.ToArray() }
    $bmp.Save((Join-Path $OutDir "rounded-$s.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $ms.Dispose(); $bmp.Dispose()
}

# assemble the .ico so it is ready to install if it gets picked
$icoPath = Join-Path $OutDir 'AppIcon-rounded.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$p.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Host "wrote $icoPath and per-size PNGs in $OutDir"

