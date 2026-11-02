# Makes the RUNNING app switch floors on cue, by dropping re-named copies of a real Factory
# screenshot into the folder the app watches.
#
# The app's only position input is the screenshot file name, so this is the one way to show the
# floor-following feature by eye without a game client: same pixels, same map, real x/z, and a
# height per step of a scripted walk.
#
#   scripts\floor-follow-demo.ps1 -Folder "C:\...\Tarkov\Screenshots"
#   scripts\floor-follow-demo.ps1 -Folder "C:\...\Screenshots" -DryRun     # print only, writes nothing
#   scripts\floor-follow-demo.ps1 -Folder "C:\...\Screenshots" -Keep       # leave the files behind
#
# This script does NOT start the app. Start it first, with -Folder set to the screenshot folder the
# app itself is pointed at, and make sure screenshots are being read at all (take a real one, or
# check the status line reacts). Then pick Factory in the map list: a fix that is already inside the
# shown map is applied straight away, whereas a fix on another map is only *suggested* unless
# "switch without asking" is on.
#
# Heads-up: it writes files into the game's screenshot folder and deletes them again at the end.
# Every name it creates carries the date 2031-01-01, which no real capture of this machine can have,
# and the cleanup only removes the exact paths this run created.
param(
    [Parameter(Mandatory = $true)][string]$Folder,
    [string]$Map = "factory",
    [switch]$Keep,
    [int]$DelayMs = 1500,
    [string]$Exe = "dotnet run --project src\TarkovMapCompanion",
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

# The fixture: a real Factory (PMC) frame. Its x/z are a genuine spot inside Factory's bounds; only
# the height is rewritten per step, because that is what decides the floor.
$fixtureName = "2026-10-18[15-16]_41.46, -1.13, -8.61_-0.00850, 0.99077, -0.07126, -0.11497_15.47 (0).png"
$fixture = Join-Path $root ("tests\screenshots\" + $fixtureName)

# Position carried over from that frame, so the walk stays on one real spot.
$x = 41.46
$z = -8.61
$quaternion = "-0.00850, 0.99077, -0.07126, -0.11497"

# Marker date. Escape from Tarkov's name format is strict -- date[HH-mm]_x, y, z_qx..qw_raidHours (n)
# -- so there is no room for a text marker; the date itself is the marker, set far enough into the
# future that nothing real can collide with it.
$marker = "2031-01-01"

# Factory's floor bands, from the vendored map data: Tunnels [-10000,-1], 2nd Floor [3,6],
# 3rd Floor [6,10000]. The gap between -1 and 3 is the ground level -- the reason a Factory walk can
# show all four states. Streets and the others have their own bands, so the heights below are
# Factory's alone.
$steps = @(
    @{ Y = 0.0;  Expect = "the ground layer is back on and no floor is ticked; the minimap badge (bottom left) reads the ground" },
    @{ Y = 4.5;  Expect = "the map switches to 2nd Floor, the ground checkbox goes OFF, the badge reads 2nd Floor" },
    @{ Y = 7.0;  Expect = "the map switches to 3rd Floor, still no ground, the badge reads 3rd Floor" },
    @{ Y = 0.0;  Expect = "coming back up: the ground layer returns by itself and no floor is ticked" },
    @{ Y = -3.5; Expect = "the map switches to Tunnels -- only visible because the ground layer went off" }
)

# ---- sanity checks, before anything is written --------------------------------

if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
    throw "screenshot folder not found: $Folder"
}

if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) {
    throw "fixture missing: $fixture  (the repository's tests\screenshots folder is the source for this)"
}

if ($Map -ne "factory") {
    throw "only -Map factory is scripted: the step heights are Factory's floor bands. Streets of Tarkov, " +
          "the other map with several levels, starts its lowest floor at y=10, so the same numbers would " +
          "all land on its ground level and demonstrate nothing."
}

$folderFull = (Resolve-Path -LiteralPath $Folder).ProviderPath
$sizeMb = [math]::Round((Get-Item -LiteralPath $fixture).Length / 1MB, 1)

Write-Host ""
Write-Host "floor-follow demo -- writes into: $folderFull" -ForegroundColor Yellow
Write-Host "This folder is the game's screenshot folder. $($steps.Count) copies of a real $sizeMb MB Factory frame" -ForegroundColor Yellow
if ($Keep) {
    Write-Host "will be dropped in, one every $DelayMs ms, and LEFT BEHIND (-Keep)." -ForegroundColor Yellow
} else {
    Write-Host "will be dropped in, one every $DelayMs ms, and removed again at the end." -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Before continuing, the app must already be running against THIS folder, reading screenshots," -ForegroundColor Cyan
Write-Host "with Factory selected. If it is not up yet:  $Exe" -ForegroundColor Cyan
Write-Host ""

$inv = [System.Globalization.CultureInfo]::InvariantCulture

# ---- build the names ----------------------------------------------------------

$plan = @()
for ($i = 0; $i -lt $steps.Count; $i++) {
    $step = $steps[$i]

    # One wall-clock minute per step, and the in-raid clock advancing at Tarkov's 7x rate over that
    # minute (420 game seconds = 0.1167 h). RaidSession.IsSameRaid accepts exactly that relation
    # (+/- its slack), so the app treats all five as one raid and keeps a single trail.
    $minute = 16 + $i
    $raidHours = 15.47 + ($i * 0.1167)

    $name = "{0}[15-{1:00}]_{2}, {3}, {4}_{5}_{6} (0).png" -f `
        $marker,
        $minute,
        $x.ToString("F2", $inv),
        $step.Y.ToString("F2", $inv),
        $z.ToString("F2", $inv),
        $quaternion,
        $raidHours.ToString("F2", $inv)

    $plan += [pscustomobject]@{
        Index  = $i + 1
        Name   = $name
        Path   = Join-Path $folderFull $name
        Y      = $step.Y
        Expect = $step.Expect
    }
}

# ---- dry run: say what would happen, write nothing ---------------------------

if ($DryRun) {
    Write-Host "DRY RUN -- nothing will be written." -ForegroundColor Cyan
    Write-Host ""

    foreach ($item in $plan) {
        Write-Host ("  {0}/{1}  y={2}" -f $item.Index, $plan.Count, $item.Y.ToString("F2", $inv))
        Write-Host ("        {0}" -f $item.Name)
        Write-Host ("        expect: {0}" -f $item.Expect) -ForegroundColor DarkGray
    }

    Write-Host ""
    Write-Host "Nothing was written. Re-run without -DryRun to perform the walk." -ForegroundColor Green
    return
}

# ---- the walk ----------------------------------------------------------------

$created = @()

try {
    for ($i = 0; $i -lt $plan.Count; $i++) {
        $item = $plan[$i]

        Write-Host ("  step {0}/{1}  y={2}  ->  {3}" -f $item.Index, $plan.Count, $item.Y.ToString("F2", $inv), $item.Name) -ForegroundColor Cyan
        Write-Host ("             expect: {0}" -f $item.Expect)

        # A byte-for-byte copy of the real frame every time: the app copies the pixels out for the
        # exit-panel OCR, and a half-written or synthesised image is the one way to make this fail
        # for a reason that has nothing to do with floors.
        Copy-Item -LiteralPath $fixture -Destination $item.Path -Force
        $created += $item.Path

        if ($i -lt $plan.Count - 1) {
            Start-Sleep -Milliseconds $DelayMs
        }
    }

    Write-Host ""
    Write-Host "Walk finished. The app should now be showing Factory's Tunnels with the ground layer off." -ForegroundColor Green
    Write-Host ("To check the same five names without the UI: run with -Keep, then  " +
                "dotnet run --project src\TarkovMapCompanion -- --floor-test `"$folderFull`" factory") -ForegroundColor DarkGray
}
finally {
    Write-Host ""

    if ($Keep) {
        Write-Host "Kept $($created.Count) file(s) in $folderFull (-Keep)." -ForegroundColor Yellow
    }
    else {
        $removed = 0
        $gone = 0

        foreach ($path in $created) {
            if (-not (Test-Path -LiteralPath $path)) {
                # The app's own cleanup (delete-after-read / keep-latest) may have taken it already.
                $gone++
                continue
            }

            # Only ever our own file: same folder, same marker date. A path that fails either check
            # is not something this run created, so it is left alone and reported.
            $leaf = Split-Path -Leaf $path
            $parent = Split-Path -Parent $path

            if ($parent -ne $folderFull -or -not $leaf.StartsWith($marker)) {
                Write-Warning "not deleting $path -- it does not look like a file this script created"
                continue
            }

            Remove-Item -LiteralPath $path -Force
            $removed++
        }

        Write-Host "Removed $removed synthesized screenshot(s)" -NoNewline -ForegroundColor Green
        if ($gone -gt 0) {
            Write-Host " ($gone were already gone -- the app's own cleanup had them)" -NoNewline
        }
        Write-Host "."
    }
}
