# Screenshot sandbox

Developer tooling. Nothing here ships. `BuildBlishHUDModule` in
`TaimisToolbench.csproj` packs `manifest.json`, `ref/**` and the build
output into the `.bhm`, and never anything under `tools/`.

The sandbox runs Blish HUD over a Microsoft Paint window instead of Guild
Wars 2, so module layout can be captured with no game running. It is the
only way an agent can see its own work.

## The scripts

| Script | What it does |
| --- | --- |
| `Start-Sandbox.ps1` | Starts Paint, sizes it, starts Blish HUD against it, and reports what it started. |
| `Stop-Sandbox.ps1` | Stops only the processes `Start-Sandbox.ps1` recorded. |
| `SandboxSession.ps1` | Window lookup and synthetic input, scoped to named process ids. Dot-source it. |
| `Set-SandboxUiScale.ps1` | Pins the interface scale. See below. |

```
powershell -ExecutionPolicy Bypass -File tools\sandbox\Start-Sandbox.ps1 -Bhm C:\Dev\Blish\gate-master\TaimisToolbench.bhm
powershell -ExecutionPolicy Bypass -File tools\sandbox\Stop-Sandbox.ps1
```

## Running it from a git worktree

Sandbox work has to run from the main checkout, not from an agent worktree
under `.claude/worktrees/`. One agent found `powershell.exe` and `cmd.exe`
refused there while another had no trouble, and the difference is not the
tooling.

The allow rule that permits `powershell.exe` lives in
`.claude/settings.local.json`. That file is gitignored, on line 348, so it is
untracked. A git worktree checks out tracked files only, so the file does not
exist in any worktree, and an agent working in one has no rule permitting
`powershell.exe`. An agent in the main checkout has it and runs unprompted.

Run the sandbox from `C:\Dev\Blish\TaimisToolbench`, or copy
`.claude/settings.local.json` into the worktree first. This is the same trap
that stranded the "pull requests are not reviewed" rule in an untracked file,
which is why CLAUDE.md now states that rule in a tracked one.

## Where these scripts live, and why here

These belong in the repository, and the two that mattered most have been
moved into it.

The launcher and its helpers used to live only in `C:\Dev\Blish\preflight`
and `C:\Dev\Blish\w81-gate`, outside any repository. Nothing reviewed them,
no test ran them, and no history recorded who changed them or why. Both faults
this file describes, a `Stop-Process` sweep over every Blish and a window
lookup by title, sat there through many milestones. They were found by agents
that read the scripts and declined to run them, not by any check.

`Start-Sandbox.ps1`, `Stop-Sandbox.ps1` and `SandboxSession.ps1` now sit
beside `Set-SandboxUiScale.ps1` under `tools/sandbox/`, so a change to them
goes through the same review as everything else. The out-of-repo copies at
`preflight\launch-sandbox.ps1`, `preflight\kill_targets.ps1` and
`w81-gate\launch.ps1` are now short wrappers that call these, so an existing
habit or script keeps working and cannot reach the dangerous versions.

`preflight\gatekit.ps1` stays where it is for now, because a dozen other
preflight scripts dot-source it and this branch is not the place to move them
all. Its `Find` no longer returns the first of several same-titled windows: it
refuses an ambiguous match and clears its rectangle. Its foreground check
still admits any title containing "Blish" or "Paint", so prefer
`SandboxSession.ps1` for new work. Moving the rest of `preflight\` in is
worth doing and is not this branch's job.

## Why targeting is by process id

A window title is not an identity. The developer's live Blish HUD overlay
carries the title `Blish HUD`, at 0,0,3440,1440, over Guild Wars 2 at the
same rectangle. The sandbox's own window carries that same title.

The scripts these replaced looked the window up by that title and took the
first match. Measured 2026-09-07, with a second window titled `Blish HUD`
open alongside a running sandbox, the old lookup returned the wrong one:

```
old [GK]::Find('Blish HUD')  ->  hwnd 86508326  pid 68048  rect 2300,250
sandbox's actual window      ->  hwnd  8913092  pid 74224  rect 8,120
```

A click computed from that rectangle lands in whatever the other window is
sitting over. In the case this was found in, that is the running game.

Every lookup in `SandboxSession.ps1` matches on the owning process id, which
the sandbox knows because it started the process. A title, when passed, is an
extra condition rather than the identity. Two rules hold throughout:

- **Refuse rather than guess.** A lookup that finds no window, or more than
  one, returns nothing and leaves its rectangle zeroed. There is no
  first-match fallback anywhere.
- **Fail closed.** Input primitives compare the foreground window's process
  id against a set that starts empty, so an input call made before
  `Set-SandboxTarget` throws instead of typing into whatever holds focus. The
  check these replaced admitted any title containing "Blish" or "Paint",
  which the live overlay satisfies.

## Nothing is stopped that the sandbox did not start

`Start-Sandbox.ps1` starts processes and records their ids. It never runs
`Stop-Process` over a name or a pattern. The scripts it replaces opened with
a sweep over every process matching "Blish" and every `mspaint`, which killed
the developer's live session and any Paint window holding unsaved work.

`Stop-Sandbox.ps1` reads the session file the launcher wrote and stops only
the ids in it. Each one is checked against the start time recorded at launch
first, because Windows reuses process ids and a stale session file would
otherwise name a stranger.

A Blish HUD the launcher did not start stops the launch. There is no
override, and this is not only a safety preference: Blish HUD allows one
instance per machine. A second one logs `Blish HUD is already running!` and
exits within a second, whatever settings directory it is given. Measured
2026-09-07 against Blish HUD v1.3.0. So the sandbox cannot run beside another
Blish, and clearing the way by force is the thing this must not do. It stops
and says which process is in the way.

## Interface scale

### The problem

The screenshot sandbox runs Blish HUD over a Microsoft Paint window instead
of Guild Wars 2. No game means no MumbleLink, and Blish falls back to the
smallest interface scale. Every shipped geometry constant then renders at
0.810 of its written size: a 42 pixel row measures 34 pixels, a 32 pixel
icon measures 26. Measurements taken there mean nothing until they are
divided by 0.810, and that step has been skipped before.

### Where the scale comes from

`Blish_HUD.GraphicsService.Rescale()` runs once per `Update` and sets:

```
UIScaleMultiplier = GetDpiScaleRatio()
                  * GetScaleRatio(GameService.Gw2Mumble.UI.UISize)
                  * scaledMinimumGameResolution.GetAspectRatioScale(backbufferSize)
```

Each of the three factors, read off the installed `C:\Blish.HUD\Blish HUD.exe`
with `ilspycmd`:

1. `GetDpiScaleRatio()` returns 1.0 unless `DpiScalingMethod` is
   `UseGameDpi`, or is `SyncWithGame` and the game reports DPI scaling on.
   The sandbox has no game, so it returns 1.0.
2. `GetScaleRatio(UiSize)` maps Small to 0.810, Normal to 0.897, Large to
   1.0 and Larger to 1.103. `Gw2Mumble.UI.UISize` reads Gw2Sharp's MumbleLink
   client directly. With no MumbleLink that value is 0, which is
   `UiSize.Small`. **That is the 0.810.** It is not a Blish default and not a
   fallback constant; it is the zero value of the enum the game would have
   written.
3. `GetAspectRatioScale` compares a fixed 1024x768 against the backbuffer
   with `enlarge: false`, so it is 1.0 for any sandbox window at least
   1024x768. The launcher's 1900x990 clears that. A shorter window shrinks
   the whole UI again, which no setting undoes.

### The override

Blish already has one, so no MumbleLink writer is needed. The
`GraphicsConfiguration` setting `UIScalingMethod`, of type
`Blish_HUD.Graphics.ManualUISize`, replaces the MumbleLink value whenever it
is not `SyncWithGame`:

| `UIScalingMethod` | Stored value | Scale ratio |
|---|---|---|
| `SyncWithGame` | 0 | MumbleLink, so 0.810 with no game |
| `Small` | 1 | 0.810 |
| `Normal` | 2 | 0.897 |
| `Large` | 3 | 1.000 |
| `Larger` | 4 | 1.103 |

`Rescale()` runs every frame, so the value is read continuously. It still
has to be set before launch, because Blish loads `settings.json` at startup
and rewrites it on exit.

### Which scale to run, and what it costs a measurement

The default is `Normal`, ratio 0.897. Most players run Normal, so a sandbox
screenshot at Normal shows what a player sees. Judge appearance there.

`Normal` is not the scale to measure at. At 0.897 a measured pixel is no
longer the number in the code:

| Constant in code | x 0.897 | Expect on screen |
| --- | --- | --- |
| 32 | 28.7 | 28 or 29 |
| 42 | 37.7 | 37 or 38 |
| 300 | 269.1 | 269 |

The third column is a range because `Rescale()` sets `UIScaleTransform` to
`Matrix.CreateScale(UIScaleMultiplier)` and the whole UI is drawn through
it. An edge lands on a fractional pixel, so the count you read off a
screenshot can sit either side of the product.

Two ways to take a measurement, and only these two:

1. Divide the measured pixels by 0.897 and round. A 38 pixel row is a 42
   pixel constant. Treat a one pixel disagreement as agreement.
2. Re-run with `-UiScale Large` first. That ratio is exactly 1.0, so a 42
   pixel constant measures 42 pixels and no arithmetic is involved. Use this
   whenever the measurement has to be exact, or has to be compared against a
   number written in the source.

Do not carry a number measured at one scale over to the other. That mistake
is what produced the 0.810 confusion this file exists to stop.

### Usage

`Set-SandboxUiScale.ps1` writes the setting into the settings directory the
sandbox launches with. It rewrites one integer and leaves every other byte
of the file alone.

```
powershell -ExecutionPolicy Bypass -File tools\sandbox\Set-SandboxUiScale.ps1
powershell -ExecutionPolicy Bypass -File tools\sandbox\Set-SandboxUiScale.ps1 -UiScale Large
```

`-UiScale` defaults to `Normal`. `-SettingsDir` defaults to
`C:\Dev\Blish\blish-preflight-settings`, which is the directory
`Start-Sandbox.ps1` passes to Blish as `--settings`. `-UiScale Game`
restores Blish's own behaviour, which with no game running is 0.810.

The script throws if that directory has no `settings.json`, or if the file
has no `UIScalingMethod` entry. Blish writes the entry itself on first run,
so the fix in both cases is to launch the sandbox once and re-run. Run
standalone through `-File` it then exits 1; called from another script it
stops that script too.

It also throws when a Blish HUD holding this settings directory is running.
`SettingsService.Unload` calls `Save(forceSave: true)`, which rewrites the
whole file from memory, so a write made while that Blish is up disappears
when it exits and nothing says so.

Two details matter. The check reads each Blish process's command line rather
than matching on the process name, because only a Blish holding this
directory can clobber this file; the developer's live session runs on its own
directory and is no threat to it. A command line that cannot be read counts
as a conflict, since the safe answer under uncertainty is to refuse. And the
check runs only when a write is actually needed: setting the scale to what it
already is reports no change and touches nothing, so `Start-Sandbox.ps1` can
call it unconditionally.

### Confirming it

The numbers above are read from Blish's code, not from a screenshot. Confirm
on screen the first time, and after any Blish HUD upgrade.

`SearchBoxWidth` in `Views/MainView.cs` is 300, and the Snapshot tab draws
its search box at exactly that. Open the module window in the sandbox,
capture it, and measure that box. At `SyncWithGame` with no game it is 243
pixels wide. At `Large` it is 300. At the `Normal` default, 300 x 0.897 is
269; the 243 above is the same arithmetic and it matched a screenshot.

Measure a code constant like this one. Do not use the module's corner icon,
and do not carry an offset measured for it at one scale over to another. An
earlier version of this section said Blish anchors the corner icon strip in
game pixels, so it sits at the same physical offset at every UI scale. That
is wrong. The strip moves and grows with the interface scale.

Measured in the sandbox, with the Blish HUD window at the same position at
both scales:

| UI scale | icon left | icon right | offset from the window rectangle |
| --- | --- | --- | --- |
| `SyncWithGame`, 0.810 | 330 | 346 | +336, +21 |
| `Large`, 1.0 | 400 | 424 | +404, +17 |

The +336, +21 offset lands outside the icon at `Large`. Re-measured
2026-09-07 at `Large`, cropping 24x24 around each point on a live sandbox:
the +404, +17 point reads 153 distinct colours and is the module's icon,
while +336, +21 reads 11 and is empty window chrome.

Anything that has to click or crop the corner icon must locate it at the
scale the sandbox is running, not reuse a recorded number.

`Start-Sandbox.ps1` reports `CORNER_ICON` from the table above, keyed on the
`-UiScale` it launched with, and says nothing at all for a scale nobody has
measured. It then samples 24x24 at that point and warns if the region is
blank backdrop. That sample is a tripwire, not a locator: it catches an
offset that has landed on nothing, and it would not have caught the 11-colour
case above. Confirm the point on a capture before clicking it.

### Keeping the scale honest at launch

`Start-Sandbox.ps1` calls `Set-SandboxUiScale.ps1` before it starts Blish
HUD, and does not wrap the call in `try`. A failed write must stop the
launch: a sandbox that comes up at 0.810 anyway looks exactly like a normal
one, and that is the mistake this exists to stop.

The launcher refuses a `-Height` below 768. Below that the aspect-ratio
factor shrinks the UI again and no scale setting compensates for it.
