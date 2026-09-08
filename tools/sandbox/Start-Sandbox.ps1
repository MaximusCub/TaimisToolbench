<#
.SYNOPSIS
    Starts the screenshot sandbox: Blish HUD drawing over a Microsoft Paint
    window, so module layout can be captured without Guild Wars 2 running.

.DESCRIPTION
    Replaces preflight\launch-sandbox.ps1 and w81-gate\launch.ps1.

    Those two opened with a Stop-Process sweep over every process matching
    "Blish", and over every mspaint. The developer's live Blish HUD session
    and any Paint window with unsaved work were inside that sweep. This
    script stops nothing. It only ever starts processes, records their ids,
    and hands them to Stop-Sandbox.ps1 for teardown.

    Those two also located the sandbox window by the title "Blish HUD". The
    live overlay carries that title too, so the lookup could return it and
    aim synthetic input at the running game. Every lookup here is scoped to
    the process id this script started. See SandboxSession.ps1.

    A Blish HUD this script did not start stops the launch, and is reported
    rather than cleared. There is no override, because Blish HUD is a single
    instance for the whole machine: a second one logs "Blish HUD is already
    running!" and exits within a second, whatever settings directory it was
    given. Measured 2026-09-07 against Blish HUD v1.3.0. Clearing the way by
    force is what this script exists not to do, so the only correct move is
    to stop and say so.

.PARAMETER Bhm
    The packed module to load.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\sandbox\Start-Sandbox.ps1 -Bhm C:\Dev\Blish\gate-master\TaimisToolbench.bhm
#>
[CmdletBinding()]
param(
    [string]$Bhm = 'C:\Dev\Blish\gate-master\TaimisToolbench.bhm',
    [int]$Width = 1900,
    [int]$Height = 990,
    [int]$Left = 0,
    [int]$Top = 89,
    [ValidateSet('Game', 'Small', 'Normal', 'Large', 'Larger')][string]$UiScale = 'Large',
    [string]$SettingsDir = 'C:\Dev\Blish\blish-preflight-settings',
    [string]$BlishExe = 'C:\Blish.HUD\Blish HUD.exe',
    [string]$SessionFile = (Join-Path $env:TEMP 'taimis-sandbox-session.json'),
    [int]$TimeoutSeconds = 40
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SandboxSession.ps1')

if (-not (Test-Path -LiteralPath $Bhm)) { throw "No module at $Bhm." }
if (-not (Test-Path -LiteralPath $BlishExe)) { throw "No Blish HUD at $BlishExe." }

# Blish's own window is at least 1024x768 before the aspect-ratio factor
# shrinks the whole interface again, which no scale setting undoes. See
# tools/sandbox/README.md.
if ($Height -lt 768) { throw "-Height $Height is below 768, which shrinks the interface no matter what -UiScale says." }

# Detection is deliberately broad, so a differently named build still trips
# it. Nothing is stopped on the strength of it: this is the developer's live
# session, over the running game, and it is not this script's to close.
$foreign = @(Get-Process | Where-Object { $_.ProcessName -like '*Blish*' })
if ($foreign.Count -gt 0) {
    $lines = $foreign | ForEach-Object { "    pid $($_.Id) $($_.ProcessName), started $($_.StartTime)" }
    throw ("Blish HUD is already running and this script did not start it:" + [Environment]::NewLine +
           ($lines -join [Environment]::NewLine) + [Environment]::NewLine +
           "Blish HUD allows one instance per machine, so the sandbox cannot start beside it. Close that session yourself and re-run. This script never stops a process it did not start.")
}

# Not a try. A sandbox that comes up at 0.810 looks exactly like a correct
# launch, and that mistake is what this call exists to stop.
& (Join-Path $PSScriptRoot 'Set-SandboxUiScale.ps1') -UiScale $UiScale -SettingsDir $SettingsDir

# Every process this script starts is recorded here before anything else can
# fail, so teardown can find them even on a mid-launch throw.
$started = [System.Collections.ArrayList]::new()
function Register-Started {
    param([System.Diagnostics.Process]$Process, [string]$Role)
    # StartTime pins the identity: Windows reuses process ids, and a stale
    # session file that names a reused id would otherwise stop a stranger.
    $null = $started.Add([PSCustomObject]@{
        Role      = $Role
        Id        = $Process.Id
        Name      = $Process.ProcessName
        StartTime = $Process.StartTime.ToString('o')
    })
    Save-Session
}
function Save-Session {
    $session = [PSCustomObject]@{
        WrittenAt = (Get-Date).ToString('o')
        LauncherPid = $PID
        Processes = @($started)
    }
    $session | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $SessionFile -Encoding UTF8
}

# A launch that throws part way leaves its own Paint or Blish running. Only
# what this script started is stopped, by the same session file teardown uses.
try {
    $paint = Start-Process mspaint -PassThru
    Register-Started -Process $paint -Role 'paint'
    "PAINT pid=$($paint.Id)"

    $paintWindow = Wait-SandboxWindow -ProcessId $paint.Id -What 'Paint window' -TimeoutSeconds $TimeoutSeconds
    if ([SB]::IsIconic($paintWindow.Hwnd)) { $null = [SB]::ShowWindow($paintWindow.Hwnd, 9) }
    # 89 is where the sandbox has always put the Paint window, clear of the
    # taskbar and the shell's top edge on this display. It is a position that
    # suited one screen, not a derived constant, so it is a parameter.
    $null = [SB]::MoveWindow($paintWindow.Hwnd, $Left, $Top, $Width, $Height, $true)
    Start-Sleep -Milliseconds 900

    $paintWindow = Find-SandboxWindow -ProcessId $paint.Id -What 'Paint window'
    "PAINT rect=$($paintWindow.X),$($paintWindow.Y) $($paintWindow.Width)x$($paintWindow.Height)"
    if ($paintWindow.Width -lt 500) {
        throw "The Paint window is only $($paintWindow.Width) wide after the move. Refusing to capture against it."
    }

    # Blish HUD attaches to Paint by process id and window class, so it draws
    # over this Paint and no other.
    $blishName = [System.IO.Path]::GetFileNameWithoutExtension($BlishExe)
    $before = @(Get-Process -Name $blishName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $blishArgs = @('-g', '0', '--debug', '--module', $Bhm, '--pid', "$($paint.Id)",
                   '--window', 'MSPaintApp', '--settings', $SettingsDir)
    $blish = Start-Process $BlishExe -ArgumentList $blishArgs -PassThru
    Register-Started -Process $blish -Role 'blish'

    # One launch produces two Blish HUD processes, measured 2026-09-07: the one
    # Start-Process returns, which owns the overlay window, and a second that
    # does not. Usually the returned id is the one to target.
    #
    # If it exits, the survivor is looked for among ids that were not running
    # before this launch, narrowed to the one owning a visible window. Anything
    # other than exactly one candidate is ambiguous and throws.
    Start-Sleep -Seconds 3
    if (-not (Get-Process -Id $blish.Id -ErrorAction SilentlyContinue)) {
        $new = @(Get-Process -Name $blishName -ErrorAction SilentlyContinue |
            Where-Object { $before -notcontains $_.Id } |
            Where-Object { $null = [SB]::Find($_.Id, ''); [SB]::Matches -ge 1 })
        if ($new.Count -ne 1) {
            throw "Blish HUD pid $($blish.Id) exited and $($new.Count) new Blish processes own a window. Cannot tell which one is the sandbox's."
        }
        $blish = $new[0]
        Register-Started -Process $blish -Role 'blish'
    }

    # The second process is recorded so teardown stops it too. It is matched the
    # same way: new since this launch, and never anything that was already up.
    foreach ($extra in @(Get-Process -Name $blishName -ErrorAction SilentlyContinue |
            Where-Object { $before -notcontains $_.Id -and $_.Id -ne $blish.Id })) {
        Register-Started -Process $extra -Role 'blish-secondary'
    }
    "BLISH pid=$($blish.Id)"

    $blishWindow = Wait-SandboxWindow -ProcessId $blish.Id -What 'Blish HUD window' -TimeoutSeconds $TimeoutSeconds
    "BLISH rect=$($blishWindow.X),$($blishWindow.Y) $($blishWindow.Width)x$($blishWindow.Height) title='$($blishWindow.Title)'"

    Set-SandboxTarget -ProcessId @($paint.Id, $blish.Id)
    "TARGETS paint=$($paint.Id) blish=$($blish.Id)"

    # The corner icon strip moves and grows with the interface scale, so an
    # offset recorded at one scale does not locate it at another. These two are
    # measured; see the table in tools/sandbox/README.md. An unmeasured scale
    # reports nothing rather than a guess.
    $cornerOffsets = @{ Small = @(336, 21); Game = @(336, 21); Large = @(404, 17) }
    if ($cornerOffsets.ContainsKey($UiScale)) {
        $offset = $cornerOffsets[$UiScale]
        $iconX = $blishWindow.X + $offset[0]
        $iconY = $blishWindow.Y + $offset[1]
        # A tripwire, not a locator. It catches an offset that has landed on
        # blank backdrop, and nothing subtler: measured 2026-09-07 at scale
        # Large, the icon reads 153 distinct colours and the stale 0.810 offset
        # reads 11, so a threshold that separates them would be fitted to these
        # two numbers. Confirm the point on a capture before clicking it.
        # The module draws its corner icon some seconds after its window
        # appears, so a single sample here reads bare backdrop and warns about
        # a correct offset. Measured 2026-09-07: blank at the moment the
        # window resolves, 148 distinct colours five seconds later. So this
        # waits for the icon rather than reporting the gap as a fault.
        #
        # A screen read can throw on an off-screen rectangle. This is a
        # diagnostic, so it reports that and leaves the launch standing.
        try {
            $colours = 0
            $deadline = (Get-Date).AddSeconds(20)
            while ((Get-Date) -lt $deadline) {
                $colours = [SB]::DistinctColours($iconX - 8, $iconY - 8, 24, 24)
                if ($colours -ge 8) { break }
                Start-Sleep -Seconds 1
            }
            if ($colours -lt 8) {
                Write-Warning "CORNER_ICON $iconX,$iconY is still blank backdrop after 20s ($colours distinct colours). The offset is wrong for this window, or the module did not load."
            } else {
                "CORNER_ICON $iconX,$iconY (offset +$($offset[0]),+$($offset[1]) measured at $UiScale, $colours distinct colours)"
            }
        }
        catch {
            Write-Warning "Could not sample the corner icon at $iconX,$iconY : $($_.Exception.Message)"
        }
    } else {
        Write-Warning "No corner icon offset has been measured at -UiScale $UiScale. Locate it on a capture."
    }

    "FG pid=$([SB]::FgPid()) title='$([SB]::FgTitle())'"
    "SESSION $SessionFile"
}
catch {
    Write-Warning "Sandbox launch failed. Stopping the processes it had started."
    & (Join-Path $PSScriptRoot 'Stop-Sandbox.ps1') -SessionFile $SessionFile
    throw
}
