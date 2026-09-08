$ErrorActionPreference = "Stop"

function To-Xyz($arr) {
    if ($null -eq $arr) { return @(0.0, 0.0, 0.0) }
    $a = @($arr)
    while ($a.Count -eq 1) {
        $inner = $a[0]
        if ($inner -is [System.Array] -or $inner -is [System.Collections.IList]) { $a = @($inner) } else { break }
    }
    return @(
        [double]([string]$a[0]),
        [double]([string]$a[1]),
        [double]([string]$a[2])
    )
}

function Expand-Aabb($el, [double]$ox, [double]$oy, [double]$oz, [ref]$minx, [ref]$miny, [ref]$minz, [ref]$maxx, [ref]$maxy, [ref]$maxz) {
    $from = To-Xyz $el.from
    $to   = To-Xyz $el.to
    $ax = $ox + $from[0]; $ay = $oy + $from[1]; $az = $oz + $from[2]
    $bx = $ox + $to[0];   $by = $oy + $to[1];   $bz = $oz + $to[2]
    $x0 = [math]::Min($ax, $bx); $x1 = [math]::Max($ax, $bx)
    $y0 = [math]::Min($ay, $by); $y1 = [math]::Max($ay, $by)
    $z0 = [math]::Min($az, $bz); $z1 = [math]::Max($az, $bz)
    if ($x0 -lt $minx.Value) { $minx.Value = $x0 }
    if ($y0 -lt $miny.Value) { $miny.Value = $y0 }
    if ($z0 -lt $minz.Value) { $minz.Value = $z0 }
    if ($x1 -gt $maxx.Value) { $maxx.Value = $x1 }
    if ($y1 -gt $maxy.Value) { $maxy.Value = $y1 }
    if ($z1 -gt $maxz.Value) { $maxz.Value = $z1 }
    if ($el.PSObject.Properties['children'] -and $el.children) {
        foreach ($c in $el.children) {
            Expand-Aabb $c $ax $ay $az $minx $miny $minz $maxx $maxy $maxz
        }
    }
}

function Get-GuiTransform($shapePath) {
    $json = (Get-Content -Raw -Path $shapePath) | ConvertFrom-Json
    $minx = 1e9; $miny = 1e9; $minz = 1e9
    $maxx = -1e9; $maxy = -1e9; $maxz = -1e9
    foreach ($el in $json.elements) {
        Expand-Aabb $el 0 0 0 ([ref]$minx) ([ref]$miny) ([ref]$minz) ([ref]$maxx) ([ref]$maxy) ([ref]$maxz)
    }
    $cx = ($minx + $maxx) / 2.0 / 16.0
    $cy = ($miny + $maxy) / 2.0 / 16.0
    $cz = ($minz + $maxz) / 2.0 / 16.0
    $sx = ($maxx - $minx) / 16.0
    $sy = ($maxy - $miny) / 16.0
    $sz = ($maxz - $minz) / 16.0
    $maxs = [math]::Max($sx, [math]::Max($sy, $sz))
    if ($maxs -lt 0.01) { $maxs = 1 }
    $scale = [math]::Round(1.10 / $maxs, 2)
    return [pscustomobject]@{
        Size = ("{0:n2}x{1:n2}x{2:n2}" -f $sx,$sy,$sz)
        OriginX = [math]::Round($cx, 3)
        OriginY = [math]::Round($cy, 3)
        OriginZ = [math]::Round($cz, 3)
        Scale = $scale
        Min = ("{0:n1},{1:n1},{2:n1}" -f $minx,$miny,$minz)
        Max = ("{0:n1},{1:n1},{2:n1}" -f $maxx,$maxy,$maxz)
    }
}

$root = "C:\Users\Kotl\Documents\GitHub\ElectricalProgressive"
$items = @(
    @{ Name="emetalforming"; Shape="$root\ElectricalProgressive-Industry\assets\electricalprogressiveindustry\shapes\block\emetalforming\emetalforming.json" },
    @{ Name="ehammer"; Shape="$root\ElectricalProgressive-Industry\assets\electricalprogressiveindustry\shapes\block\ehammer\ehammer.json" },
    @{ Name="eblastfurnace"; Shape="$root\ElectricalProgressive-Industry\assets\electricalprogressiveindustry\shapes\block\eblastfurnace\eblastfurnace.json" },
    @{ Name="eextruder"; Shape="$root\ElectricalProgressive-Industry\assets\electricalprogressiveindustry\shapes\block\eextruder\eextruder.json" },
    @{ Name="epress"; Shape="$root\ElectricalProgressive-Industry\assets\electricalprogressiveindustry\shapes\block\epress\epress.json" },
    @{ Name="ecrusher"; Shape="$root\ElectricalProgressive-Industry\assets\electricalprogressiveindustry\shapes\block\ecrusher\ecrusher.json" },
    @{ Name="esieve"; Shape="$root\ElectricalProgressive-Industry\assets\electricalprogressiveindustry\shapes\block\esieve\esieve.json" },
    @{ Name="erecycler"; Shape="$root\ElectricalProgressive-Industry\assets\electricalprogressiveindustry\shapes\block\erecycler\eRecycler.json" },
    @{ Name="eaquaaccum"; Shape="$root\ElectricalProgressive-Industry\assets\electricalprogressiveindustry\shapes\block\eaquaaccum\eaquaaccum.json" },
    @{ Name="efuelgenerator"; Shape="$root\ElectricalProgressive-Basics\assets\electricalprogressivebasics\shapes\block\fuelgenerator\fuelgenerator.json" },
    @{ Name="etermogenerator"; Shape="$root\ElectricalProgressive-Basics\assets\electricalprogressivebasics\shapes\block\termogenerator\termogenerator.json" }
)

foreach ($it in $items) {
    if (-not (Test-Path $it.Shape)) { Write-Output "$($it.Name): MISSING"; continue }
    $r = Get-GuiTransform $it.Shape
    Write-Output ("{0,-16} size={1,-20} origin=({2}, {3}, {4}) scale={5}  [{6} .. {7}]" -f $it.Name,$r.Size,$r.OriginX,$r.OriginY,$r.OriginZ,$r.Scale,$r.Min,$r.Max)
}
