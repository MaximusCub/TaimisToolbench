# Blish HUD's own settings panel

Blish HUD renders a control for every `SettingEntry` a module defines. Those
controls appear under Manage Modules, next to this module's entry, in a panel
this module does not draw and cannot lay out. This page records what that
panel does with a setting, because two of its behaviours are load-bearing for
`Services/ModuleSettingText.cs` and neither is obvious from the module side.

**This module draws no settings in that panel.** Every setting it defines sits
in the non-rendered sub-collection described below, and every one of them is
changed on the module's own Settings tab, or by the control that owns it. The
panel carries one line and an Open Settings button instead, built by
`Views/BlishSettingsHintView.cs`. The rest of this page still applies, because
turning one setting back on is a one-word change in
`Services/ModuleSettingText.cs`.

## Why nothing is shown there

Two reasons, and the second is the one that decided it.

The panel cannot be laid out by the module. Its geometry is Blish's, its
sliders have no range unless the module calls `SetRange`, and it writes values
without telling any control the module has already built. All three problems
are described further down, and none of them can be fixed from here.

A setting is either the module's to present or Blish's, and splitting them
leaves a player with two places to look and no rule for which. The module
already draws a Settings tab with grouped rows, validation and live apply, so
the whole set lives there.

## What the panel shows instead

`Module.GetSettingsView` returns `Views/BlishSettingsHintView.cs`, which is a
line of text and a button. Overriding `GetSettingsView` replaces Blish's own
setting list outright, so it is a second reason nothing is drawn there, and
the two other modules the maintainer runs both do the same: Pathing returns a
hint view with an Open Settings button, and Estreya's EventTable returns a
single button that opens its own window.

The button selects the Settings tab and then shows the window. That order
matters twice. `TabbedWindow2.OnTabChanged` calls `ShowView` whether or not
the window is visible, so the tab can be chosen while it is closed. And its
tab-swap sound plays only `if (this.Visible && e.PreviousValue != null)`, so
choosing the tab first means opening a closed window does not play one.

`WindowBase2.Show` calls `BringWindowToFront` before its own visibility check,
so the button also works when the window is already open on another tab.

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
`tools/dump-font-metrics.py`. `ModuleSettingTextTests` measures every visible
name against it and fails the build on one that does not fit. No name is
visible today, so that check runs over an empty set. The same class pins the
measurement itself against a name known to collide, which is what keeps the
budget honest while the set is empty.

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

`ModuleSettings.Define` picks the collection from the descriptor's
`ShownInBlishPanel` flag, so the flag is what a change has to edit. Turning a
setting back on needs the migration to run the other way: its stored value is
under `Internal` and the root has none, so a build that flips the flag without
carrying the value back gives that player the default.

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
when it is built, so a change made there while that tab is open leaves the tab
showing the old figure, and its Save button then writes the old figure back.
`ClickSoundVolumePercent` is the exception: `SettingsTabContent` subscribes
to its `SettingChanged` and moves its slider to match. Nothing reaches the
panel now, so no setting can be written behind the tab's back.

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
