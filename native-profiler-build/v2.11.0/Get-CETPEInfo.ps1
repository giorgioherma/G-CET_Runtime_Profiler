param(
    [Parameter(Mandatory=$true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

function Read-U16([byte[]]$b, [int]$o) {
    return [BitConverter]::ToUInt16($b, $o)
}
function Read-U32([byte[]]$b, [int]$o) {
    return [BitConverter]::ToUInt32($b, $o)
}

$p = (Resolve-Path $Path).Path
$b = [IO.File]::ReadAllBytes($p)

if ($b.Length -lt 512 -or $b[0] -ne 0x4D -or $b[1] -ne 0x5A) {
    throw "Not a valid PE file: $p"
}

$pe = [int](Read-U32 $b 0x3C)
if ($b[$pe] -ne 0x50 -or $b[$pe+1] -ne 0x45) {
    throw "PE signature missing: $p"
}

$fileHeader = $pe + 4
$optional = $fileHeader + 20

$timestamp = Read-U32 $b ($fileHeader + 4)
$majorLinker = $b[$optional + 2]
$minorLinker = $b[$optional + 3]
$sizeOfCode = Read-U32 $b ($optional + 4)
$entryPoint = Read-U32 $b ($optional + 16)
$sizeOfImage = Read-U32 $b ($optional + 56)

[pscustomobject]@{
    Path = $p
    Length = $b.Length
    SHA256 = (Get-FileHash -Algorithm SHA256 $p).Hash
    PETimeDateStamp = [DateTimeOffset]::FromUnixTimeSeconds($timestamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
    Linker = "$majorLinker.$minorLinker"
    SizeOfCode = $sizeOfCode
    EntryPointRVA = ('0x{0:X}' -f $entryPoint)
    SizeOfImage = $sizeOfImage
}
