param(
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 192, 256)
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$destinationPath = [System.IO.Path]::GetFullPath($Destination)
$destinationDir = [System.IO.Path]::GetDirectoryName($destinationPath)
[System.IO.Directory]::CreateDirectory($destinationDir) | Out-Null

$sourceImage = [System.Drawing.Image]::FromFile($sourcePath)
$frames = New-Object System.Collections.Generic.List[object]

try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
            }
            finally {
                $graphics.Dispose()
            }

            $stream = [System.IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $frames.Add([PSCustomObject]@{
                    Size = $size
                    Bytes = $stream.ToArray()
                })
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $sourceImage.Dispose()
}

$outStream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($outStream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)

    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frame.Bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $frame.Bytes.Length
    }

    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame.Bytes)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($destinationPath, $outStream.ToArray())
}
finally {
    $writer.Dispose()
    $outStream.Dispose()
}

$bytes = [System.IO.File]::ReadAllBytes($destinationPath)
if ($bytes.Length -lt 6) { throw 'ICO output is truncated.' }
$count = [BitConverter]::ToUInt16($bytes, 4)
if ($count -ne $sizes.Count) {
    throw "ICO frame count mismatch. Expected $($sizes.Count), got $count."
}

$actualSizes = @()
for ($i = 0; $i -lt $count; $i++) {
    $entry = 6 + (16 * $i)
    $width = [int]$bytes[$entry]
    if ($width -eq 0) { $width = 256 }
    $height = [int]$bytes[$entry + 1]
    if ($height -eq 0) { $height = 256 }
    if ($width -ne $height) { throw ("ICO frame {0} is not square: {1}x{2}" -f $i, $width, $height) }

    $length = [BitConverter]::ToUInt32($bytes, $entry + 8)
    $dataOffset = [BitConverter]::ToUInt32($bytes, $entry + 12)
    if ($dataOffset + 8 -gt $bytes.Length) { throw "ICO frame $i has an invalid offset." }

    $pngSignature = [byte[]](0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A)
    for ($j = 0; $j -lt 8; $j++) {
        if ($bytes[$dataOffset + $j] -ne $pngSignature[$j]) {
            throw "ICO frame $i is not PNG-backed."
        }
    }

    if ($dataOffset + $length -gt $bytes.Length) {
        throw "ICO frame $i exceeds the file length."
    }

    $actualSizes += $width
}

if ((Compare-Object $sizes $actualSizes).Count -ne 0) {
    throw "ICO sizes mismatch. Expected $($sizes -join ','), got $($actualSizes -join ',')"
}

Write-Host "Generated Windows icon: $destinationPath"
Write-Host "ICO sizes: $($actualSizes -join ', ')"
