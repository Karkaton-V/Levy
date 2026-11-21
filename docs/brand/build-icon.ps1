# Builds src/Workbench.Desktop/Assets/App/levy.ico (exe, shortcut, window and taskbar icon) from the levy-icon.svg shapes. Run: powershell -STA -File build-icon.ps1
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$out = Join-Path $PSScriptRoot "..\..\src\Workbench.Desktop\Assets\App\levy.ico"
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

# The icon as XAML, on a 256 x 256 canvas (same paths as levy-icon.svg).
$xaml = @'
<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="256" Height="256">
  <Canvas.Clip><RectangleGeometry Rect="0,0,256,256" RadiusX="56" RadiusY="56" /></Canvas.Clip>
  <Rectangle Width="256" Height="256" Fill="#2563EB" />
  <Path Fill="#FFFFFF" Opacity="0.5" Data="M20,200 C30,160 54,122 86,106 C104,97 120,95 130,101 L114,106 C102,108 94,118 92,132 C90,150 92,174 94.5,188 L92,200 Z" />
  <Path Fill="#FFFFFF" Opacity="0.6">
    <Path.Data><GeometryGroup>
      <EllipseGeometry Center="124,82" RadiusX="4.5" RadiusY="4.5" />
      <EllipseGeometry Center="110,76" RadiusX="3.5" RadiusY="3.5" />
      <EllipseGeometry Center="134,72" RadiusX="3" RadiusY="3" />
    </GeometryGroup></Path.Data>
  </Path>
  <Path Fill="#FFFFFF" Data="M92,200 L124,62 L158,62 L158,168 L222,168 L222,200 Z" />
  <Rectangle Canvas.Top="200" Width="256" Height="56" Fill="#1D4ED8" />
</Canvas>
'@

$pngs = foreach ($size in $sizes) {
  $canvas = [System.Windows.Markup.XamlReader]::Parse($xaml)
  $box = New-Object System.Windows.Controls.Viewbox
  $box.Child = $canvas
  $box.Measure((New-Object System.Windows.Size($size, $size)))
  $box.Arrange((New-Object System.Windows.Rect(0, 0, $size, $size)))
  $box.UpdateLayout()
  $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
  $bitmap.Render($box)
  $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $stream = New-Object System.IO.MemoryStream
  $encoder.Save($stream)
  , $stream.ToArray()
}

# ICO file: header, one directory entry per size, then the PNG images.
$file = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($file)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i] % 256
  $w.Write([byte]$s); $w.Write([byte]$s); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Flush()
[System.IO.File]::WriteAllBytes($out, $file.ToArray())
"Wrote $out ($($file.Length) bytes, sizes $($sizes -join ', '))"
