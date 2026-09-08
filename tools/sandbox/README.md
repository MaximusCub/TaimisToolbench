# Sandbox interface scale

Developer tooling. Nothing here ships. `BuildBlishHUDModule` in
`TaimisToolbench.csproj` packs `manifest.json`, `ref/**` and the build
output into the `.bhm`, and never anything under `tools/`.

## The problem

The screenshot sandbox runs Blish HUD over a Microsoft Paint window instead
of Guild Wars 2. No game means no MumbleLink, and Blish falls back to the
smallest interface scale. Every shipped geometry constant then renders at
0.810 of its written size: a 42 pixel row measures 34 pixels, a 32 pixel
icon measures 26. Measurements taken there mean nothing until they are
divided by 0.810, and that step has been skipped before.

## Where the scale comes from

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

## The override

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

## Which scale to run, and what it costs a measurement

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

## Usage

`Set-SandboxUiScale.ps1` writes the setting into the settings directory the
sandbox launches with. It rewrites one integer and leaves every other byte
of the file alone.

```
powershell -ExecutionPolicy Bypass -File tools\sandbox\Set-SandboxUiScale.ps1
powershell -ExecutionPolicy Bypass -File tools\sandbox\Set-SandboxUiScale.ps1 -UiScale Large
```

`-UiScale` defaults to `Normal`. `-SettingsDir` defaults to
`C:\Dev\Blish\blish-preflight-settings`, which is the directory
`preflight\launch-sandbox.ps1` passes to Blish as `--settings`. `-UiScale Game`
restores Blish's own behaviour, which with no game running is 0.810.

The script throws if that directory has no `settings.json`, or if the file
has no `UIScalingMethod` entry. Blish writes the entry itself on first run,
so the fix in both cases is to launch the sandbox once and re-run. Run
standalone through `-File` it then exits 1; called from another script it
stops that script too.

It also throws if Blish HUD is running. `SettingsService.Unload` calls
`Save(forceSave: true)`, which rewrites the whole file from memory, so a
write made while Blish is up disappears when Blish exits and nothing says
so.

## Confirming it

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

The +336, +21 offset lands outside the icon at `Large`. Anything that has to
click or crop the corner icon must locate it at the scale the sandbox is
running, not reuse a recorded number.

## Wiring it into the launcher

`preflight\launch-sandbox.ps1` is not in this repository. Give it a
`$uiScale` parameter, and call this script after its `Stop-Process` sweep
and before it starts Blish HUD. That order matters: the sweep is what makes
the running-Blish check above pass.

```powershell
param([string]$bhm = '...', [int]$width = 1900, [int]$height = 990,
      [ValidateSet('Game','Small','Normal','Large','Larger')][string]$uiScale = 'Normal')

& C:\Dev\Blish\TaimisToolbench\tools\sandbox\Set-SandboxUiScale.ps1 -UiScale $uiScale -SettingsDir C:\Dev\Blish\blish-preflight-settings
```

Do not wrap that call in `try`. A failed write must stop the launcher: a
sandbox that comes up at 0.810 anyway looks exactly like a launch at 0.897,
and that is the mistake this exists to stop.

Keep `$height` at 768 or more. Below that the aspect-ratio factor shrinks
the UI again and no scale setting compensates for it.
