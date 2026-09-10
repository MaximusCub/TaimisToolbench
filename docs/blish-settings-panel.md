# Blish HUD's own settings panel

Blish HUD renders a control for every `SettingEntry` a module defines. Those
controls appear under Manage Modules, next to this module's entry, in a panel
this module does not draw and cannot lay out. This page records what that
panel does with a setting, because two of its behaviours are load-bearing for
`Services/ModuleSettingText.cs` and neither is obvious from the module side.

Everything below is read from Blish HUD 1.3.0, which is the version
`manifest.json` depends on and the version installed on the maintainer's
machine. Later Blish releases have changed this layout; see the last section.

## How wide a display name may be

A setting whose value is an `int` is drawn by
`Blish_HUD.Settings.UI.Views.IntSettingView`, which inherits its layout from
`NumericSettingView<T>`. In 1.3.0 that layout is fixed:

- the name `Label` is placed at `x = 5`, with `AutoSizeWidth = true`
- the value `TrackBar` is placed at `x = 185`, 277 wide

Neither control moves afterwards. `RefreshDisplayName` in 1.3.0 sets the
label text and nothing else, so a label wider than 180 pixels is drawn over
the slider. That is the defect this module shipped: five setting names ran
past 180 and collided with their sliders.

The budget in `ModuleSettingText.MaxDisplayNameWidth` is therefore **175
pixels**, which is 185 minus the label's own 5 pixel inset minus the 5 pixel
`CONTROL_PADDING` Blish puts between its own controls.

A `bool` setting is drawn by `BoolSettingView`, which puts the name inside
the `Checkbox` and has no neighbour to collide with. The budget is applied to
every visible name anyway. One rule is easier to keep than two, and no
current name needs the slack.

## How a name is measured

The label uses `Content.DefaultFont14`, which is Menomonia 14 regular. The
pixel width is built in two steps.

`MonoGame.Extended` 3.8.0 `BitmapFont.GetStringRectangle` walks the string:

    pen = 0; right = 0
    for each glyph g:
        right = max(right, pen + g.xOffset + g.width)
        pen  += g.xAdvance + letterSpacing
    width = (int)right

`letterSpacing` is `-1`. `Blish_HUD.ContentService.GetFont` sets it on every
font it hands out, so it applies to every label in the overlay. The XNB font
format carries no kerning pairs, so `BitmapFont.UseKernings` finds none.

`Blish_HUD.Controls.LabelBase.RecalculateLayout` then takes
`Math.Ceiling` of that width. The value is already an integer, so the ceiling
changes nothing and the label's width is the number above.

`docs/menomonia-14-metrics.txt` holds the per-glyph `width`, `xOffset` and
`xAdvance` this needs, read straight out of the shipped font by
`tools/dump-font-metrics.py`. `ModuleSettingDisplayNameTests` measures every
name against it and fails the build on one that does not fit.

## How a setting is hidden

Blish offers exactly one supported way to keep a setting out of the panel.
`SettingCollection.AddSubCollection(key, renderInUi: false)` creates a nested
collection, and `SettingView.FromType` returns `null` for a sub-collection
whose `RenderInUi` is false, skipping every setting inside it.

There is no per-setting flag. `SettingEntry.SessionDefined` is the only other
thing `SettingsView` filters on and its setter is `internal` to Blish, so a
module cannot reach it.

**A sub-collection changes where the value is stored.** Settings are
persisted as nested JSON objects under
`ModuleStates.<namespace>.Settings.Entries`, so a setting moved into a
sub-collection is written one level deeper under a new parent, even though
its own key is unchanged. `ModuleSettings.DefineHidden` carries the old
top-level value across on first run and then undefines the top-level copy.

## What this panel gets wrong, and what it costs

Two things worth knowing before adding a setting to it.

A slider has no range unless the module sets one.
`IntSettingView.RefreshValue` widens the track to fit whatever value is
already stored, so `LogMaxSizeBytes` at 2097152 produces a slider running
from 0 to 2097152 in which one pixel is roughly 7500 bytes. Blish's
`SettingComplianceExtensions.SetRange` fixes this; this module does not call
it yet.

Blish's panel writes a setting directly and does not tell an already-built
control anywhere else. The module's own Settings tab reads its values once
when it is built, so a change made here while that tab is open leaves the tab
showing the old figure, and its Save button then writes the old figure back.
`ClickSoundVolumePercent` is the exception: `SettingsTabContent` subscribes
to its `SettingChanged` and moves its slider to match.

## After a Blish upgrade

`NumericSettingView` has already changed upstream. Current Blish adds a
`NumberInput` box at `x = 220` and moves the slider to `x = 325`, and its
`RefreshDisplayName` pushes the slider right for a name wider than 220 - but
it does not move the number box, so a long name collides with that instead.
The budget above is the tighter of the two layouts and holds for both.

When `manifest.json` raises its Blish dependency, re-read
`NumericSettingView` for that version, re-run `tools/dump-font-metrics.py`
against the new install, and update `MaxDisplayNameWidth` if the geometry
moved.
