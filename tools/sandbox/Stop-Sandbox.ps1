<#
.SYNOPSIS
    Stops the processes Start-Sandbox.ps1 started, and nothing else.

.DESCRIPTION
    Replaces preflight\kill_targets.ps1, which ran
    Get-Process -Name "Blish HUD" | Stop-Process -Force and so killed the
    developer's live session along with the sandbox.

    This reads the session file the launcher wrote and stops only the ids in
    it. Each id is checked against the process start time recorded at launch
    before anything is stopped. Windows reuses process ids, so a stale
    session file can name an id that now belongs to something else; the start
    time is what tells those two cases apart.

.PARAMETER SessionFile
    Written by Start-Sandbox.ps1. Its -SessionFile default is the same path.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SessionFile = (Join-Path $env:TEMP 'taimis-sandbox-session.json'),
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SessionFile)) {
    "No session file at $SessionFile. Nothing to stop."
    return
}

$session = Get-Content -LiteralPath $SessionFile -Raw | ConvertFrom-Json
$entries = @($session.Processes)
if ($entries.Count -eq 0) {
    "Session file $SessionFile records no processes."
    return
}

$stopped = 0
$skipped = 0
foreach ($entry in $entries) {
    $process = Get-Process -Id $entry.Id -ErrorAction SilentlyContinue
    if (-not $process) {
        "GONE $($entry.Role) pid $($entry.Id) already exited."
        continue
    }

    # Round-trip both sides through the same format: the recorded string is
    # ISO 8601 and the live value is a DateTime with sub-tick differences
    # that a direct comparison would trip over.
    $recorded = ([datetime]$entry.StartTime).ToString('o')
    $actual = $process.StartTime.ToString('o')
    if ($recorded -ne $actual) {
        Write-Warning ("REFUSED $($entry.Role) pid $($entry.Id): started $actual, but the session recorded $recorded. " +
                       "The id has been reused, so this is not the sandbox's process. Leaving it alone.")
        $skipped++
        continue
    }

    if ($PSCmdlet.ShouldProcess("$($process.ProcessName) pid $($entry.Id)", 'Stop-Process')) {
        Stop-Process -Id $entry.Id -Force
        "STOPPED $($entry.Role) $($process.ProcessName) pid $($entry.Id)."
        $stopped++
    }
}

if ($Remove -and $skipped -eq 0) { Remove-Item -LiteralPath $SessionFile -Force }

"Stopped $stopped, refused $skipped."
