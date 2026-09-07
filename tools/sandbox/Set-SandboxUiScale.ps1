<#
.SYNOPSIS
    Pins the interface scale that the screenshot sandbox renders Blish HUD at.

.DESCRIPTION
    Blish HUD computes GameService.Graphics.UIScaleMultiplier every frame as
    GetDpiScaleRatio() * GetScaleRatio(GameService.Gw2Mumble.UI.UISize) * an
    aspect-ratio factor. Without Guild Wars 2 running there is no MumbleLink,
    so Gw2Sharp reports UiSize 0 (Small) and GetScaleRatio returns 0.810.
    Every shipped geometry constant then renders at 0.810 of its written size.

    Blish exposes an override. The GraphicsConfiguration setting
    UIScalingMethod, of type Blish_HUD.Graphics.ManualUISize, replaces the
    MumbleLink value when it is anything other than SyncWithGame. Setting it
    to Large makes GetScaleRatio return exactly 1.0, so a 42 pixel row
    measures 42 pixels on a screenshot.

    This script writes that setting into a Blish settings.json. It edits only
    the one value and leaves the rest of the file byte for byte unchanged.

.PARAMETER UiScale
    Game keeps Blish's default, which reads MumbleLink. Small, Normal, Large
    and Larger pin the scale to 0.810, 0.897, 1.000 and 1.103.

.PARAMETER SettingsDir
    The directory passed to Blish HUD as --settings. It must already hold a
    settings.json that Blish has written at least once.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\sandbox\Set-SandboxUiScale.ps1 -UiScale Large
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('Game', 'Small', 'Normal', 'Large', 'Larger')]
    [string]$UiScale = 'Large',

    [string]$SettingsDir = 'C:\Dev\Blish\blish-preflight-settings'
)

$ErrorActionPreference = 'Stop'

# Blish_HUD.Graphics.ManualUISize, read off the shipped Blish HUD.exe. The
# settings file stores the enum as its integer value.
$manualUiSize = @{ Game = 0; Small = 1; Normal = 2; Large = 3; Larger = 4 }

# Blish_HUD.GraphicsService.GetScaleRatio, same source. Game has no fixed
# ratio: it takes whatever MumbleLink reports, and 0.810 when nothing does.
$ratio = @{ Game = 'MumbleLink, 0.810 with no game'; Small = '0.810'; Normal = '0.897'; Large = '1.000'; Larger = '1.103' }

$settingsPath = Join-Path $SettingsDir 'settings.json'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "No settings.json under $SettingsDir. Launch the sandbox once so Blish HUD writes its defaults, then re-run."
}

# .NET keeps its own working directory, which is not the one the PowerShell
# provider is on, so a relative -SettingsDir would send the write below to a
# different file than the read above. Resolve once, here.
$settingsPath = (Resolve-Path -LiteralPath $settingsPath).ProviderPath

# Blish_HUD.SettingsService.Unload calls Save(forceSave: true), which rewrites
# the whole file from the values it holds in memory. A write made while Blish
# is running is therefore lost when it exits, and lost in silence.
if (@(Get-Process -Name 'Blish HUD' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw "Blish HUD is running and rewrites $settingsPath when it exits. Close it, then re-run."
}

$text = Get-Content -LiteralPath $settingsPath -Raw

# The key is written by Blish itself, so it is present in any settings dir
# the sandbox has been launched against. Inserting it by hand is not worth
# the fragility, so an absent key is a hard failure with the fix in it.
$expression = [regex]'("Key":\s*"UIScalingMethod",\s*"Value":\s*)(\d+)'
$match = $expression.Match($text)
if (-not $match.Success) {
    throw "$settingsPath has no UIScalingMethod entry. Launch the sandbox once so Blish HUD writes it, then re-run."
}

$current = [int]$match.Groups[2].Value
$target = $manualUiSize[$UiScale]

$currentName = ($manualUiSize.GetEnumerator() | Where-Object { $_.Value -eq $current } | Select-Object -First 1).Key
if (-not $currentName) { $currentName = "unknown ($current)" }

if ($current -eq $target) {
    "UIScalingMethod already $UiScale (scale ratio $($ratio[$UiScale])). No change."
    return
}

if ($PSCmdlet.ShouldProcess($settingsPath, "Set UIScalingMethod to $UiScale ($target)")) {
    # Blish writes this file with CRLF, no BOM and no trailing newline. The
    # one-match Replace and a BOM-free writer keep every other byte as it was.
    $updated = $expression.Replace($text, '${1}' + $target, 1)
    [System.IO.File]::WriteAllText($settingsPath, $updated, (New-Object System.Text.UTF8Encoding $false))
    "UIScalingMethod $currentName -> $UiScale (scale ratio $($ratio[$UiScale])) in $settingsPath"
}
