# Architecture: the essential complexity

This document exists because several pieces of this module look, on first
read, like over-engineering for a Blish HUD addon. They are not - each one
is a direct, evidence-backed response to a real constraint (a missing
`SynchronizationContext`, a bug in the vendored Blish HUD binary, a race
between two independently-scheduled callbacks, and so on). This is the
durable "why" for each of those pieces: what it is, why it exists, and
where it lives. It intentionally does not repeat the full investigation
narrative (root-cause traces, live-verification transcripts, dated PASS
records) - that history is preserved in
[`docs/KNOWN-ISSUES.md`](KNOWN-ISSUES.md) (the current-state tracker: the
numbered catalog, the open list, and a ledger pointing into
[`dev/archive/known-issues/`](../dev/archive/known-issues/), where the full
milestone records live one file each) and the pre-M38 fix-pass diary this
document distills (internal history, not published in this repository).
Each section below names the KNOWN-ISSUES
item number(s) it is drawn from so you can go read the original
investigation.

This is a living map of *mechanisms*, not a tour of every file. For a map
of *files* - which folder holds what, and the handful worth opening first -
see [`docs/README.md`](README.md). Designs that were proposed and
deliberately not built live in [`docs/DECISIONS.md`](DECISIONS.md), so this
document can describe what exists rather than argue against what does not.
See `docs/gw2e-parity-spec.md` for the normative behavior the solver
targets, and `CONTRIBUTING.md` for build/test/style basics.

---

## 1. No `SynchronizationContext`: `MainThreadMarshal` and `FrameTicker`

**What:** Two small primitives that get code back onto Blish HUD's main
(UI) thread, for two different shapes of problem.

- `MainThreadMarshal.Run` (`Views/MainThreadMarshal.cs`) - queues a single
  one-shot action onto the main thread via
  `GameService.Overlay.QueueMainThreadUpdate`.
- `FrameTicker` (a private nested `Control` in
  `Views/CraftingPlanView.cs`) - drives a callback once per real engine
  frame via `Control.DoUpdate`, for work that must span multiple frames
  (scroll verify, resize-settle debounce, wheel-wrap correction verify).

**Why:** Blish HUD's XNA host installs no `SynchronizationContext`, so an
`await` continuation resumes on a ThreadPool thread by default; any code
that touches a Blish HUD control after an `await` must marshal back onto
the main thread first, or it corrupts control state from a non-UI thread.
`QueueMainThreadUpdate` looks like it should also work for multi-frame
work (call it again from inside its own callback to "wait a frame"), but
it does not: empirically confirmed via a live trace during M30, a
re-queued callback drains again **within the same frame** instead of
waiting for the next real `Update()` tick (400 same-frame re-queues
observed in one drain). `FrameTicker` exists because `Control.DoUpdate` is
documented to fire at most once per real frame, which `QueueMainThreadUpdate`
cannot guarantee under re-entrant re-queuing.

**Where:** `Views/MainThreadMarshal.cs`; `FrameTicker` in
`Views/CraftingPlanView.cs` has FOUR live instances (measured): `_scrollVerifyTicker`
(scroll verify), `_resizeDebounceTicker` (resize-settle debounce),
`_wheelWrapVerifyTicker` (wheel-wrap correction verify, driving
`ApplyWheelWrapCorrection` - see section 2), and `_spinnerTicker` (the W3B
status-strip spinner tick - see `ArmSpinnerTicker`/`SpinnerTick` and
`Services/PlanStripTickDecision.cs`). All four are canceled/nulled together
by `StopLiveTickers` (see `docs/KNOWN-ISSUES.md`'s `CraftingPlanView`
hazard row for the tab-switch race this class of field is exposed to).
Scroll restore itself is applied synchronously, not via a ticker - see
section 3.

**Verified: `Build()` itself also runs off the main thread.** Every one of
this module's `_mainWindow.Tabs` entries (`LogTabContent`, `MainView`,
`SettingsTabContent`, `AboutTabContent`, `CraftingPlanView`, and the Plan
History/Crafting Ranker placeholders - see Module.cs's `Initialize()`) is
wrapped in `Views/ViewAdapter.cs`, whose `Build(Container)` override is
called by Blish HUD's own view-loading pipeline, not by this module. Decompiling
the shipped Blish HUD v1.3.0 binary (`Blish HUD.exe`, via `ilspycmd`)
confirms the exact call chain and why it lands on a ThreadPool thread:
`Blish_HUD.Controls.TabbedWindow2.OnTabChanged` (fired from the `SelectedTab`
setter, synchronously on the main thread on a tab click) calls
`WindowBase2.ShowView(view)`, which does
`view.DoLoad(progress).ContinueWith(BuildView)`; `BuildView` calls
`CurrentView.DoBuild(this)`, and `View<TPresenter>.DoBuild` calls the
protected `Build(buildPanel)` method every view (including `ViewAdapter`)
overrides. `View<TPresenter>.DoLoad` is `async Task<bool>` and, for the base
`Load`/`NullPresenter.DoLoad` implementations this module's views use,
completes without any genuine `await` suspension - but `Task.ContinueWith`
called without `TaskContinuationOptions.ExecuteSynchronously` and with no
ambient `SynchronizationContext` schedules its callback onto
`TaskScheduler.Default` (the ThreadPool) regardless of whether the antecedent
task is already complete at the point `ContinueWith` is called. So `Build()`
reliably runs on a ThreadPool thread, never inline on the main thread that
triggered the tab switch - the same "no `SynchronizationContext`" constraint
this section's `MainThreadMarshal` exists for, just reached via Blish HUD's
own internals instead of this module's own `await`s. (`TabbedWindow2`'s
`Tabs`/`SelectedTab` machinery is what `Views/ResizableTabbedWindow.cs`, this
module's `_mainWindow`, derives from.)

**Also verified: a tab switch detaches, it does not dispose.** A liveness
check shaped like `control.Parent != null` (this module's
`LogTabContent.IsLive`, and the inline `_headerPanel`/`_contentPanel`/
`_coinPanel`.`Parent == null` guards in `MainView.cs`) only detects that
`control` has been **disposed** - it does NOT detect that `control`'s tab was
merely switched away from, even though several of this module's own comments
previously claimed otherwise. Decompiling `WindowBase2.ShowView`/`ClearView`
shows `ClearView()` calls `Container.ClearChildren()` on the WINDOW itself
(`while (_children.Count > 0) { _children[0].Parent = null; }`) - detaching
only the outgoing view's top-level `ViewAdapter` panel, not anything below
it - and `CurrentView.DoUnload()`, whose `Unload()` call detaches nothing:
`ViewAdapter.Unload()` (`Views/ViewAdapter.cs:250`) unsubscribes the
window's `Resized` handler and leaves the control tree exactly where it is.
Only `Control.Dispose()` nulls a control's own `Parent`
(`Parent = null;` inside `Control.Dispose(bool disposing)`), and nothing on
the tab-switch path calls it - that only happens when `Module.Unload()`
disposes `_mainWindow`. Net effect: after a plain tab switch, every control
below the outgoing `ViewAdapter`'s own top-level panel (e.g.
`LogTabContent._contentPanel`, `MainView._headerPanel`/`_contentPanel`) keeps
a non-null `Parent`, so a `Parent != null`/`IsLive`-shaped guard does NOT
trip for that case - only for the module actually being unloaded. A
`MainThreadMarshal.Run` tail that lands after the user has already switched
away therefore still executes its render into a detached,
unreachable-but-not-disposed tree: wasted work (rebuilding rows or content
nobody will ever see), not a crash and not a correctness bug - but a call
site whose comment claims the guard catches that case is asserting something
false, which is its own defect (KNOWN-ISSUES #36).

**Also verified: `Container.Children` is lock-guarded - the hazard a
marshaled `Build()` tail actually closes is the compound dispose-then-add
sequence, not `Children` itself.** A tempting shorthand for why a
dispose-then-add `Build()` tail (`MainView.Build`'s
`UpdateCoinDisplay`/`ApplyStatusDisplay`/`RebuildContent`,
`LogTabContent.RebuildRows`) needs marshaling is "two threads would mutate
the same `Children` collection concurrently, corrupting it" - decompiling
`Blish_HUD.Controls.ControlCollection<T>` (`packages/BlishHUD.1.3.0/lib/
net472/Blish HUD.exe`, via `ilspycmd`) shows this is not actually why:
`ControlCollection<T>` holds a private `ReaderWriterLockSlim _listLock` and
takes it on every operation - `Add`/`Remove`/`AddRange`/the indexer setter
all `EnterWriteLock`; `Count`/the indexer getter `EnterReadLock`; and
`GetEnumerator` `EnterReadLock`s and releases it from its
`ControlEnumerator`'s `Dispose()`. `Container.AddChild`/`RemoveChild` build
their `ChildChangedEventArgs` from a `_children.ToList()` snapshot and then
call the locked `_children.Add`/`_children.Remove`. So concurrent `Children`
mutation cannot corrupt the collection's own internals the way an
unsynchronized `Queue<T>` can (LogTabContent's field crash above) - unlike
that crash, this module has never actually needed to guard against
`Children` itself being corrupted. The real hazard in a "dispose old
children, then add new ones" tail is that the sequence is a non-atomic
COMPOUND operation: `Children`'s own lock protects each individual
`Add`/`Remove` call, but nothing holds a lock across the whole
"dispose-every-old-child, then add-every-new-one" sequence, so two
interleaved rebuilds can each finish disposing before either starts adding,
and both survive - duplicated content, e.g. the doubled "No log entries
yet." placeholders `LogTabContent` hit live on 2026-07-23, and - on the
second path, where `Module.cs`'s `TabChanged` handler called `Refresh()`
on the main thread while `Build()`'s own tail was still running on a
ThreadPool thread - two threads enqueuing into `_renderedRows` at once,
crashing with "Destination array was not long enough" inside
`Queue<T>.SetCapacity`. Marshaling the whole
tail onto the main thread still closes this correctly, just for the right
reason: it prevents two rebuilds from interleaving AT ALL (a single thread
cannot run two call stacks at the same instant), rather than relying on a
lock inside `Children` that was never the thing missing. A call site whose
comment instead claims `Children` itself would have been corrupted is
asserting something the decompiled source disproves - its own defect
(KNOWN-ISSUES #36).

### Tab changes have no pre-change hook: the unsaved-Settings prompt

`Module.cs`'s `PromptForUnsavedSettings` asks whether to keep or drop unsaved
Settings edits only after the user has already left the tab, because Blish
1.3.0 has nowhere to put the question earlier. Measured from the vendored
binary: `TabbedWindow2.SelectedTab`'s setter assigns the backing field via
`SetProperty` and only then calls `OnTabChanged`, which itself calls
`ShowView` (tearing down the old view) BEFORE raising the public `TabChanged`
event. There is no pre-change event, nothing the handler can set to veto, and
the one virtual member in the chain already runs after the assignment - so by
the time any module code is reached the tab has changed and cannot be changed
back without triggering a second switch. KNOWN-ISSUES #51 records the
alternatives that were measured and rejected.

The prompt still has the user's text to save because of the detach-not-dispose
behaviour above: `ClearChildren` unparents the outgoing view's controls
without disposing them, so the Settings `TextBox`es still hold what was typed
and Save persists exactly what was on screen.

Only the tab path is hooked. The window's own `Hidden` event deliberately is
not: measured in the vendored 1.3.0 binary, every `WindowBase2` subscribes to
`Gw2Mumble.PlayerCharacter.IsInCombatChanged` and
`Gw2Instance.IsInGameChanged`, both of which call `Hide()` when the user has
Blish's "hide windows in combat" or "hide during loading" overlay options on,
so entering combat with an edited field would pop a modal over gameplay.
Closing the window leaves the edits in the live `TextBox`es exactly as it
always has: nothing tears the view down, so reopening the window shows the
typed text again.

### The logging-channel rule for these guards

Every one of these primitives ends in a catch that swallows rather than
propagates - `MainThreadMarshal.Run` (an unhandled exception in a queued
callback would take down Blish HUD's update loop), `FrameTicker.DoUpdate`,
`TooltipFacility.ResolveContent` (a deferred builder runs inside Blish's
own mouse-moved handler), and `ResizeSettleDebounce`'s `_onError`. That
makes the choice of log channel load-bearing, so it is a rule and not a
preference:

> **Anything a user could plausibly report goes to
> `ModuleLog.Shared.Write`. Blish HUD's `Logger` is additive - never the
> sole channel.**

The module ships its own diagnostic surface (`Services/ModuleLog.cs`,
`Views/LogTabContent.cs`) with a Copy-to-clipboard button, and that button
is what a bug report will actually contain. A failure written only to
Blish's own file log is invisible there, which is the worst possible place
for exactly these symptoms to land: "the tooltip does nothing on that row",
"the plan strip froze on a phase", "I clicked Generate and nothing
happened". Before this rule was written down the split was 39 `Logger`
calls to 37 `ModuleLog` calls with no stated convention, so every new catch
block was a coin flip.

Two riders:

- **`Logger` stays.** It carries the full stack; `ModuleLog` entries are
  one ring-buffer line each and keep only the exception's type and message.
  A catch that discards recoverable state (`CraftingPlanView.
  RollBackFailedPlanRender`) writes both, plus enough plan identity to
  reproduce - never item ids, which stay internal-only, log tab included.
- **The log system's own failures never route back into the log system.**
  `ModuleLogStore`'s IO-failure callback goes straight to `Logger`
  (Module.cs, `Initialize`), because writing into the sink whose write just
  failed is unbounded recursion. `MainThreadMarshal` is the subtle case:
  `LogTabContent` rebuilds its rows THROUGH `MainThreadMarshal.Run`, so a
  rebuild that throws would write the entry that schedules the rebuild that
  throws. It carries a `[ThreadStatic]` re-entrancy guard for the
  synchronous half and suppresses consecutive duplicate signatures for the
  asynchronous half; `Logger` still gets every occurrence.

**Full history:** KNOWN-ISSUES items 1, 12, 13, 36
(the internal history, after the WP-27 split).

---

## 2. The shipped-Blish `WheelDelta` sign-unwrap bug: `WheelDeltaSanitizer`

**What:** `Services/WheelDeltaSanitizer.cs` classifies a raw Blish HUD
wheel delta as either genuine or corrupted by a real defect in the
vendored `Blish HUD.exe` binary, and corrects it when corrupted.

**Why:** Decompiling the shipped Blish HUD v1.3.0 binary
(`Blish_HUD.Input.MouseEventArgs.WheelDelta` getter) shows it extracts a
signed 16-bit Windows mouse-wheel delta as *unsigned*, then "corrects" the
sign only when the unsigned value exceeds the single-notch step (120).
That heuristic is wrong the moment Windows coalesces 2+ up-notches into
one hook message: a genuine `+240` (two up-notches) reads as unsigned
`240`, which is `> 120`, so the getter "un-wraps" a value that was never
wrapped, turning `+240` into `240 - 65536 = -65296`. This reproduces the
exact live-measured histogram from a 2026-07-21 instrumented trace
(`N*120 - 65536` for `N` coalesced up-notches). Down-notches never trigger
it (their unsigned representation is already above the threshold for a
legitimate reason). This is not a dev-harness artifact - both of Blish
HUD's mouse-hook backends feed the same buggy getter, so a real player
fast-flicking the wheel upward hits it. The module cannot patch Blish
HUD's own binary, so it classifies and corrects the value on the way in
instead.

The decompiled getter, verbatim:

```csharp
int num = Convert.ToInt32((MouseData & 0xFFFF0000u) >> 16);
if (num > SystemInformation.MouseWheelScrollDelta) num -= 65536;
return num;
```

**The `-60000` threshold, derived:** a wrapped-positive event's raw value
is `N*120 - 65536` for an intended up-notch count `N >= 2`, i.e. the band
`[-65416 .. -60016]` (`N=46`, an already-absurd flick), falling further for
larger `N`. A genuine down-delta never comes near it: the largest measured
is `-840` (7 coalesced down-notches), and an implausible 40-notch down-flick
is only `-4800`. `-60000` sits between the two, so `raw <= -60000` selects
exactly the corruption. A single up-notch (`N=1`, unsigned 120) sits *at*
the threshold, not above it, so the vendored getter leaves it alone - which
is why single notches in both directions are clean.

**Why 120 is hardcoded** in the sanitizer and in
`CraftingPlanView.ApplyWheelWrapCorrection`'s `intendedDelta / 120.0`
notch arithmetic, while `MouseWheelScrollLines` is read live: 120 is Win32's
`WHEEL_DELTA`, the fixed unit a low-level mouse hook reports for one notch.
`SystemInformation.MouseWheelScrollDelta` is Microsoft's managed accessor
for that same constant and is not user-configurable, unlike
`MouseWheelScrollLines`, which is.

**The second Windows defect, `MouseWheelScrollLines = -1`:** Windows' "one
screen at a time" mouse-wheel setting (Control Panel / Settings mouse wheel
option) reports `SystemInformation.MouseWheelScrollLines` as `-1`, not a
usable line count. Blish's own `Scrollbar.HandleWheelScroll` has the
identical defect - its `Math.Sign(...) * -30 * MouseWheelScrollLines`
scrolls the *wrong direction* for every wheel event, wrapped or not, under
that setting - and this module cannot fix Blish's arithmetic.
`WheelDeltaSanitizer.SanitizeScrollLines` substitutes Windows' documented
out-of-box default of 3 lines whenever the raw value is not a usable
positive count (covering `-1` and any other non-positive or unexpected
value defensively), which at least keeps *this module's* correction
pointing the right way. It deliberately does not try to reproduce Blish's
own step size under that setting, since Blish's step is itself wrong there:
direction-correctness is chosen over an unreachable exact-step match for
this one OS setting value.

**Where:** `Services/WheelDeltaSanitizer.cs` (pure, Blish-free,
unit-tested); consumed by `CraftingPlanView.ApplyWheelWrapCorrection`.

**Full history:** KNOWN-ISSUES item 12 (reopened/root-caused in M36).

---

## 3. Scroll preserve/restore/verify

**What:** Every mutation that can change section content height (a
decision-pill click, Expand/Collapse All, a resize) wraps its rebuild in
`CraftingPlanView.PreserveScrollAcross`, which snapshots the current
scroll offset, lets the rebuild run, and then re-asserts that offset for
several subsequent real frames (`StartScrollVerify`) - because Blish's own
`Panel`/`Scrollbar` machinery resets scroll to zero on certain content
changes, and there is a window during which a user's own wheel input can
arrive and must **not** be overwritten by the restore.

**Why:** This is a genuine contest, not a one-shot fix. The scrollbar
offset is read via reflection (`PanelScrollbarField`) because Blish HUD
does not expose it any other way. Two hard-won invariants make the
contest safe rather than janky:

- Container heights (section bodies, recipe-tree child containers) are
  finalized **synchronously at build time** (see `PlanContentHeightMath`
  below) instead of relying on Blish's `FlowPanel` `AutoSize`, which only
  converges one nested level per real frame - the old fluctuating-height
  window was the actual root cause of a reopened fast-wheel-up bug (a
  wheel notch landing during that window used to be silently overwritten).
- The verify loop yields immediately the moment it observes a real wheel
  event, rather than requiring the content height to have stopped
  changing first - so a user scrolling during a live restore is never
  contested.

**Why the restore refreshes the scrollbar first:** Blish's `Scrollbar`
caches `_scrollbarPercent` (viewport height over content height) and
refreshes it only inside its own `RecalculateLayout`, which also assigns
`ScrollDistance = 0` whenever that percent has moved. `ScrollDistance`'s
setter calls `Invalidate()`, and `Invalidate` reaches `RecalculateLayout`
SYNCHRONOUSLY - so a restore written while the cache is stale resets itself
to zero inside its own assignment statement. A rebuild leaves the cache
stale: `Panel.UpdateContentRegionBounds` only re-writes the scrollbar's
`Height`/`Top`/`Right`, all three unchanged by a content rebuild, so
`SetProperty` short-circuits and no layout pass runs. Both restore paths
(`ApplySavedScrollSynchronously` and `PreserveScrollAcrossResize`)
therefore call `scrollbar.RecalculateLayout()` first, letting the expected
reset happen while nothing is riding on it. This is the "toggle a
decision and the view jumps to the top" defect reported in game: the currency table
gaining or losing rows is precisely a content-height change.

**Why the correction is computed in pixel space:**
`Services/ScrollMath.ApplyPixelDelta` converts a scrollbar ratio to pixels,
applies the delta, and converts back, rather than working in ratio space
directly. Blish's own `Scrollbar.HandleWheelScroll`/`ScrollAnimated` operate
in pixel space (a fixed per-notch pixel step added to the current pixel
offset), so a correction expressed in ratios would not compose the same way
across a changing scrollable range.

**Where:** `Views/CraftingPlanView.cs`, region "Scroll preserve/restore/verify"
(`PreserveScrollAcross`, `StartScrollVerify`, the `PanelScrollbarField`
reflection handle).

**Full history:** KNOWN-ISSUES items 1, 12, 14, 19 (root-cause and fix
narrative for the reopened fast-wheel-up regression is the most detailed
single item in the history).

---

## 4. `PlanContentHeightMath`: the synchronous height contract

**What:** `Services/PlanContentHeightMath.cs` is pure, Blish-free,
unit-tested arithmetic that computes the exact pixel height of any section
body or recipe-tree subtree from row counts/types and expansion state
alone - no layout pass, no waiting for convergence.

**Why:** Every row height in the plan view is a fixed constant (nothing
wraps; only single-line ellipsis truncation), so the total height of any
container is knowable up front. `CraftingPlanView` uses these same
constants both to size containers explicitly (replacing Blish's
`FlowPanel` `AutoSize`) and to size the individual row `Panel`s it
creates, so the two paths cannot drift apart. This synchronous contract is
what closes the multi-frame flash/stutter window described in section 3
above - without it, "wait for layout to settle" would need to reappear
somewhere, reopening the same race.

**Where:** `Services/PlanContentHeightMath.cs`; mirrored by
`Services/PlanRelayoutMath.cs` for the width-dependent counterpart (column
anchors, cost-tile geometry) used by both the build path and the
in-place resize relayout path.

Two sections own extra arithmetic of the same kind, in the same shape
(pure, Blish-free, called from both the build path and the relayout
closures, so the two cannot drift):
`Services/SummarySectionLayoutMath.cs` (the Total Cost section's body
height, its promoted cost band, and the currency table's column edges) and
`Services/TreeCostColumnMath.cs` (the recipe tree's per-denomination cost
sub-columns and the whole-tree pre-scan that sizes them).

The same shape covers the non-scrolling top strip:
`Services/TopRegionLayoutMath.cs` holds its Y offsets, read by the initial
`Build`, the item-row add/remove reflow and the resize handler. Its rows
are not all unconditional - the Recipe Tree toolbar row appears only for a
plan that has a tree - and the invariant it guarantees is that a hidden row
costs exactly zero, so the strip with no toolbar is byte-identical to the
strip before the row existed.

### 4.1 Header bands: three tiers, one factory

Chrome that names something is banded; chrome that holds an interactive
control is not. There are exactly three tiers, and
`Views/Rendering/HeaderBands.cs` is the only place two of them are built:

- **Tier 1 - tab title.** One per tab, `PlanContentHeightMath.
  TabTitleBandHeight` tall, `UiFonts.SectionTitle`, wearing GW2 asset
  1032325 over an opaque base. It is the ONLY place a tab is named:
  `Blish_HUD.Controls.Tab.Draw` renders the tab's icon and nothing else,
  its `Name` is a hover tooltip, and `TabbedWindow2` never sets the
  window's `Subtitle`. `Views/ViewAdapter.cs` draws it instead of setting
  `Panel.Title`, because Blish's own header is pinned to 36px and
  `DefaultFont16` by literals inside private layout methods. A tab
  therefore does NOT repeat its own name in its content.
- **Tier 2 - section title.** No band: `UiFonts.SectionTitle` over a 2px
  rule at `SectionHeaderRowHeight - 3`. If tier 1 and tier 2 wore the same
  chrome the tab title would stop reading as the top of the hierarchy, and
  the Crafting Plan tab stacks six tier-2 headings in one scroll.
- **Tier 3 - column header.** `ColumnHeaderRowHeight` (32) of opaque base
  plus the same texture. 32 is the asset's native height, so this is the
  one surface in the module where it maps 1:1 with no vertical stretch.

The base colour and the texture are private to `HeaderBands`, so a call
site cannot hand-roll a band from them - the reason the class is a factory
and not the bag of constants it replaced, which eight sites borrowed a
colour from and seven of which built their own panel.

One rung further out, the same rule governs the container those heights
are measured against. `Views/ViewAdapter.cs` derives the hosted view's
container size from `Services/PanelChromeMath.cs` - a Blish-free mirror of
`Panel`'s own content-region arithmetic, fed Blish's public `Panel`
constants - rather than reading `Panel.ContentRegion` back after resizing
that panel. A `Panel` writes its `ContentRegion` only from
`RecalculateLayout`, and `Control.UpdateLayout` skips `RecalculateLayout`
entirely while the control's PARENT is layout-suspended, which a parent is
for the whole of its own layout pass. A window that resizes itself from
inside that pass - `Views/ResizableTabbedWindow.cs`'s minimum-size clamp
is called from `RecalculateLayout` and writes `Size` - therefore reaches
its `Resized` subscribers with the child panel's region still describing
the previous size. Sizes computed from it are wrong and stay wrong, since
nothing reads the region again. The window's own `ContentRegion` is
exempt and is still read directly: `WindowBase2.OnResized` assigns it from
the new size synchronously, before raising `Resized`, so it is never a
layout pass behind.

### 4.2 What the height constants absorbed

**The convergence the arithmetic replaced.** These containers used to rely
on Blish's `FlowPanel` `HeightSizingMode.AutoSize`, which converges only
one nested level per real engine frame: `Container.DoUpdate` sizes a
container from its children's *current* bounds before recursing into those
children's own `Update` for that same frame. That is the root cause of
KNOWN-ISSUES #12/#14's multi-frame flash/stutter window. Because every row
height in the plan view is a fixed constant, the whole tree of heights is
knowable synchronously instead, and `CraftingPlanView` uses the same
constants for the AutoSize-replacement containers and for the individual
row `Panel`s - the "one source of truth" shape `ShoppingColumnMath`
already has.

**`CostTileLabelToValueGap` used to be a residual.** Both Total Cost bands
bottom-anchored their amount inside a fixed row height, so the space under
the caption was whatever the height arithmetic happened to leave over - 1px
on the profit band, which read as cramped in game. Anchoring the
amount *under* the caption instead makes that gap one named constant at
every band, and the two bands can no longer drift apart. Its value, 8, is
derived from the caption tier's own metrics rather than chosen by eye; the
derivation is in the constant's own doc comment, because the number is the
thing a caller has to get right.

**`MultiRootTreeFlowHeight` and the multi-root render.** gw2efficiency
renders N independent top-level recipe trees, its synthetic wrapper node
never surfacing (`docs/gw2e-parity-spec.md`), and this module matches that.
Each requested item's own root node already *is* a full
icon/name/quantity/pill/cost row - the same `CraftingTreeNode` shape
`TreeNodeHeight` sizes for a single-item plan - so the multi-root height is
that same per-root arithmetic summed across every root, plus one divider
per adjacent pair. The one column header sits above every root rather than
per root: the tree's right-hand columns would otherwise be unlabelled,
unlike every other column-header table in the plan.

**`PanelChromeMath` belongs to this section too**, and its "why" is the
last part of 4.1 above: a panel's `ContentRegion` cannot be read back after
a resize that happened inside a layout pass, so the arithmetic is mirrored
Blish-free instead. The class's own doc comment states the hazard and
points here rather than restating the mechanism.

**The viewport's bottom, and the band at its top.** The chain from the
window's height down to the panel a tab renders into has two ends that moved
independently. At the bottom, the viewport used to stop 74px above the window
while its top sat flush under the title bar: the content region `Module.cs`
hands Blish was authored window-region-relative, and Blish reads it as
absolute texture coordinates (KNOWN-ISSUES #66). At the top, the module sets
no `Panel.Title`, so Blish reserves no 36px header and the tab's name is drawn
on the module's own taller band inside the content region instead - which is
the chain `ViewAdapter` really builds, and the one
`tests/TaimisToolbench.Tests/Services/PanelChromeMathTests.cs` sweeps, at
window sizes from the module's floor to a 4K-tall client. Its budgets are
written as literals so that shrinking the band back cannot quietly move the
assertions with it.

**Full history:** KNOWN-ISSUES items 12, 14, 19, 65, 66.

---

## 5. The relayout/re-ellipsis registry, `ISectionRelayoutSink`, and the section-renderer decomposition

**What:** When the plan window is resized, every row that has
width-dependent content (an ellipsized name, a right-aligned coin column)
needs to re-measure in place, without a full dispose+rebuild. Each section
builder registers a same-signature closure into one of two registries
(`_relayoutActions`, `_reellipsisActions`) at build time; a resize replays
every registered closure. A DEBUG-only assertion checks that a section
builder registered at least one relayout closure, so a future section
cannot silently opt out of resize support.

`ISectionRelayoutSink` (`Views/Rendering/ISectionRelayoutSink.cs`) is the
seam that let this registry be reached by section-renderer classes
extracted out of `CraftingPlanView` during M38, without those renderers
holding a reference to the view itself: `AddRelayout`/`AddReellipsis` are
a direct pass-through to the same two `List<Action<int>>.Add` calls the
inline builders always made, so every existing invariant (the DEBUG
must-register check, the scroll-neutral assert, `ReplayRelayout`'s own
foreach) sees a sink-registered closure exactly as it would have seen one
added inline.

The registry's hard rule is that a replayed closure may never change a
row's height - that is what lets the settle pass skip scroll preservation
entirely. The Plan Notes section is the one place where the right answer
at a new width genuinely is a different height (it spends one fixed-height
row per WRAPPED LINE, so a width that changes a note's line count changes
the section's height). Rather than weaken the rule, its re-ellipsis
closure calls `RequestRerenderAfterSettle`, and `ResizeSettleStep` runs a
single `PreserveScrollAcross(() => RenderPlan(...))` once the pass has
finished - deferred because `RenderPlan` clears the registry the pass is
iterating.

**The M38 decomposition:** `CraftingPlanView` was originally a single
~4,800-line class covering navigation, layout, six content sections, the
recipe tree, and scroll/resize/wheel handling. M38 (WP-21, WP-23a-d,
WP-24, WP-25) extracted:

- Six stateless per-render section renderers under `Views/Rendering/`:
  `DisciplinesSectionRenderer`, `UsedMaterialsSectionRenderer`,
  `ShoppingListSectionRenderer`, `CraftStepsSectionRenderer`,
  `RecipesSectionRenderer`, `SummarySectionRenderer` - each pushes closures
  into the sink instead of the view's private fields, and is freshly
  constructed on every render (they own no state across renders).
- `TreeSectionController` (`Views/Rendering/TreeSectionController.cs`) -
  the one component that is **not** stateless: it owns the recipe tree
  render state and the interactive override loop
  (`_nodeOverrides`/`_ignoredItemIds`/`_nodeExpansion`/`_treeNodeStates`),
  which must survive a local pill-click re-solve (a pill click never
  resets the user's overrides). Because of that, it is constructed once in
  `CraftingPlanView`'s own constructor and held as a persistent field,
  unlike the per-render renderers above. (`NotesSectionRenderer` joined
  them on 2026-08-16 for the Plan Notes section, making **seven** today -
  same stateless, freshly-constructed-per-render shape.)
- Tier-1 static rendering primitives with no instance state
  (`CoinCurrencyRenderer`, `RarityColors`, `IconControls`, `LabelHelpers`)
  also moved to `Views/Rendering/`.

**Dependencies point one way.** `CraftingPlanView` and `MainView` call into
`Views/Rendering`; nothing in `Views/Rendering` references either. A
renderer that needs a view-private helper extracts the helper into
`Views/Rendering` rather than widening the view's surface, and what a
renderer needs from the view arrives through a narrow interface the view
implements explicitly - `ISectionRelayoutSink` for relayout registration,
`ITreePlanHost` (`Views/Rendering/ITreePlanHost.cs`) for everything
`TreeSectionController` needs beyond it - or, where null is itself a
meaningful value, a constructor delegate. This was violated once - a `GetPillColors`
`private -> internal` bump on `CraftingPlanView` - and reverted for this
reason; the rule is stated here, once, so it does not have to be restated
at each call site. `MainView -> Views/Rendering` (e.g.
`CoinCurrencyRenderer.AddSegmentSpec`) is a forward call and fine.

`TreeSectionController` is constructed once, in `CraftingPlanView`'s own
constructor (`Views/CraftingPlanView.cs` ~743), and lives as long as the
view: one owner, one lifetime. That is what lets a pill click re-solve
locally without resetting the user's overrides. The constructor takes the
two sinks, the view-model builder, the solver callback and two optional
hooks; it used to take fourteen positional arguments, ten of them bare
delegates, two of which shared the type `Action<PlanViewModel>` with
opposite meanings, so transposing them compiled. Splitting it into a
stateful/stateless pair was proposed and rejected - see
[`docs/DECISIONS.md`](DECISIONS.md). New tree-row and pill features grow
the `Services/` side of the boundary instead, under `CONTRIBUTING.md`'s
STANDING RULE.

**What stayed, and why (WP-26 cut):** The scroll/resize/wheel controller
move (bundling `PreserveScrollAcross`, the wheel-wrap correction, and the
`FrameTicker`s then in the class into their own collaborator class) was
scoped as WP-26 and explicitly **cut** on 2026-07-23. It was the single
riskiest remaining move with zero functional payoff: the guarantees
involved (frame-timing, subscription order, synchronous-registration) are
asserted by construction and by the invariants in sections 1-4 above, not
by any automated test, so a regression would only surface in live use, and
a reliable synthetic drag-resize verification was not achievable. The five
completed extractions took `CraftingPlanView` from a ~4,802-line
plan-authoring baseline down to ~2,802 lines at the time WP-26 was cut -
real progress, even though short of the plan's own 2,000-line target - so
the remaining scroll/resize/wheel machinery stays in `CraftingPlanView.cs`,
fully region-mapped with KNOWN-ISSUES anchor comments at each region head.

The file then grew back past its own pre-decomposition baseline - 5,281
lines on 2026-08-25, +2,156 in the 33 days after the decomposition
landed - with nothing in CI to notice. Its current size is the entry in
`docs/file-budgets.txt`, which CI enforces, rather than a number restated
here that goes stale the moment the file moves; it was 5,185 against the
~4,802 above when this paragraph was last checked. `Views/Rendering/` holds
about 12,100 lines across 46 files, so the split of plan-tab code is roughly
70% outside the view -
a ratio that can move in either direction, unlike the one-off before/after
figure. Both numbers, and the date, so a later reader can re-run the two
commands rather than take a characterization on trust.

Two things changed on that date so the regrowth cannot repeat quietly.
`docs/file-budgets.txt` pins every tracked `.cs` file to its size that
day and a CI step fails when a file exceeds its entry, so growth now
costs a visible line in a checked-in file rather than nothing. And the
view's `#region` markers, which had numbered eight responsibilities but
shipped twenty-three disjoint blocks with eleven headers reading
"(continued)", were renamed: each marker now names its own block and no
two names repeat. The numbering went rather than the code, because making
it true would mean reordering exactly the scroll/wheel/ticker machinery
the WP-26 cut above is about.

**Where:** `Views/Rendering/ISectionRelayoutSink.cs`,
`Views/Rendering/ITreePlanHost.cs`,
`Views/Rendering/TreeSectionController.cs`, the seven
`Views/Rendering/*SectionRenderer.cs` files (the six M38 ones plus
`NotesSectionRenderer`), `Views/Rendering/PlanHeaderRenderer.cs` and
`Views/Rendering/EmptyPlanStateRenderer.cs` (the plan title and the
no-plan state, moved onto the same sink on 2026-08-25),
`Views/ItemInputRowStrip.cs` (the multi-item request editor - in `Views/`
rather than `Views/Rendering/`, because its controls are `Views` types
and the dependency points one way), and the surviving
`_relayoutActions`/`_reellipsisActions` registries plus scroll/resize/wheel
machinery in `Views/CraftingPlanView.cs`.

**Full history:** KNOWN-ISSUES items 13, 19 (registry); the WP-21 through
WP-25 entries and the WP-26 cut-decision entry (M38 section, near the end
of the history).

---

## 6. `StatusUpdateGuard`

**What:** `Services/StatusUpdateGuard.cs` is a single pure function,
`ShouldApply(tickGeneration, currentGeneration, currentGenerationStatusClosed)`,
that decides whether a queued plan-generation status update should still
be written to the status label.

**Why:** A generation's trailing progress tick and that same generation's
completion write are two independently-scheduled main-thread callbacks
with no FIFO guarantee between them - `Progress<T>`'s default
`SynchronizationContext` hop (used for every progress tick) takes one
extra ThreadPool round-trip versus the task-continuation path the
completion write rides, so in practice the completion write reliably
reaches the main-thread queue and drains **before** an earlier-queued
trailing tick from the exact same generation. A simple
"does this tick belong to the current generation" guard cannot catch this
race, since both callbacks belong to the same generation and pass that
check. The fix is to also track whether that generation's own completion
status has already been written, and refuse to overwrite it - checked at
the moment each tick's callback actually *runs*, not when it was queued,
which is what closes the race regardless of drain order.

**Where:** `Services/StatusUpdateGuard.cs`; consumed by
`CraftingPlanView.TriggerGenerate`'s progress callback.

**Full history:** KNOWN-ISSUES item 20.3 (M34-B1 #4).

### 6.1 Out-of-order phase events: `PhaseOrdinalGuard`

`Services/PhaseOrdinalGuard.cs` mirrors `StatusUpdateGuard`'s shape and
spirit - a pure, Blish-free function `CraftingPlanView` calls at the
moment each event actually drains - for a second race the guard above
cannot see.

`Progress<T>` with no `SynchronizationContext` installed (section 1: this
module has none) posts every `Report` through an independent
`ThreadPool.QueueUserWorkItem`, so two phase events reported milliseconds
apart - a warm recipe/price cache, a small plan - can be executed out of
order by different worker threads before either reaches the main-thread
queue. `StatusUpdateGuard` alone cannot catch that: both events belong to
the SAME generation, so its `myGen` check passes for both, and the
later-draining older event can overwrite a newer phase's text.

The fix rests on `PlanPhase`'s declaration order being the pipeline's
actual emission order, so its int ordinal is a reliable monotonic sequence
per generation. That order is `BuildingTree` -> `FetchingPrices` ->
`SolvingDecisions` -> `FetchingItemDetails` -> `CheckingLearnedRecipes` ->
`BuildingDisplay`, and `CraftingPlanPipeline`'s `phaseTracker.Start` call
sites fire strictly in it on both the single-item and multi-item paths.
An event whose ordinal is not strictly greater than the last one applied
is therefore stale and dropped, regardless of drain order.

### 6.2 The pull-based status board

`Services/PlanStripStatusBoard.cs` holds the Crafting Plan tab's status
state at module level rather than in the view (KNOWN-ISSUES #45, "tab
switch strip freeze / lost completion status").

`CraftingPlanView`'s status-strip fields and its `_statusLabel` control
are rebuilt every time the tab's `Build()` runs, so a completion callback
that writes directly to a control can target a since-discarded label or be
skipped by a view-liveness check - and nothing about the next `Build()`
cycle would know a finished generation's status text ever existed to
restore. The board inverts that. Every write only ever updates pure state,
so it can never be skipped by a liveness check and never race a rebuild;
the strip is a pull consumer, reading the board every tick while armed and
reading a fresh `Snapshot()` on every rebuild.

It is constructed once by `Module` and passed into `CraftingPlanView`'s
constructor, so the state outlives any single view build cycle - the
module-level-state ownership pattern used for exactly this class of bug.

Threading: writers run on whichever thread they naturally land on - the
main thread for `Begin` (`TriggerGenerate`, before any await), a
ThreadPool thread for `UpdatePhase` (the pipeline's
`IProgress<PlanPhaseEvent>` callback) and for `Finish` (the pipeline's
success/cancel/failure continuation). None of them marshal onto the main
thread first, because nothing here touches a Blish HUD control. Only the
pull side - the spinner ticker's `FrameTicker.DoUpdate` step, and `Build()`
itself - runs on the main thread, and it is the only place a Blish control
is ever touched from this board's data.

`Finish` is rejected through the same `StatusUpdateGuard` `UpdatePhase`
uses rather than a raw sequence-only check, which would otherwise accept a
second `Finish()` for the same generation - silently overwriting the
first-recorded wording - and would accept a `Finish(0, ...)` on a virgin,
never-`Begin()`'d board. That last case is unreachable today only because
the caller's `myGen` is always `++_generateSequence` and therefore never
0, which is not an invariant this class should rely on its caller to hold.

`SeedRestored` is the one write that deliberately bypasses both guards: it
is the board's own one-time initial seed at module load, at sequence 0,
which the view's `++_generateSequence` convention can never produce. Its
own `_sequence == 0 && !_inFlight` check is enforcement, not decoration.
`Module.LoadAsync`'s restore drain can lag well behind the module's
`Update()` loop starting to tick - `LoadAsync` awaits a full
account-snapshot network refresh after arming the restore flag but before
returning, and Blish HUD does not call a module's `Update()` until
`LoadAsync`'s Task completes - so a user can open the window and click
Generate while `LoadAsync` is still in flight, and have that generation's
`Begin(1)`/`UpdatePhase(1, ...)`/`Finish(1, ...)` all land *before* the
seed finally runs. Unconditionally stomping `_sequence` back to 0 in that
window would silently reject every subsequent write for that in-flight
generation and freeze its spinner on the next tick
(`PlanStripTickDecision.Decide` sees Sequence 0 != myGen 1 and stops) -
exactly the lost-completion-status bug the board exists to prevent. The
`_sequence == 0` half alone would suffice, since `Begin` only ever moves
`_sequence` away from 0 and never back; the `_inFlight` half is kept as a
self-documenting belt-and-braces guard rather than relying on that.

`ClearRestoredSeed` undoes a seed whose downstream render then failed, and
carries the same guard for the same reason.

---

## 7. Merged-ceil vendor batching

**What:** When a plan needs a vendor-sold item at more than one tree
position (or a vendor sells it only in fixed-size batches), the solver
must compute one true per-item cost across all occurrences, rounding batch
purchases up **once** for the combined total rather than once per
occurrence. This lives in `Services/VendorBatchSolver.cs`
(`EvaluateVendorOffers`, `FinalizeVendorBatches`,
`AllocateVendorNodeCosts`, `MergeVendorCurrencyCosts`, `VendorBatchesEqual`,
`ScaleCostLines`), injected into `PlanSolver` as a collaborator.

**Why:** Rounding per-occurrence instead of per-total overstates cost.
The canonical regression case: needing 179 of a vendor item sold in
batches of some size that should round up to a total of 180, not 186 -
the bug that motivated pinning this arithmetic against change without
proof (see `docs/KNOWN-ISSUES.md`'s policy note). The
class also carries the Astral Acclaim / Wizard's Vault seasonal purchase
cap (`SeasonalCap`, independent of the pre-existing daily/weekly cap
fields) and the Homestead Refinement efficiency-tier discount, both
threaded through the exact same merged-batch machinery rather than as
separate paths.

**Where:** `Services/VendorBatchSolver.cs`. This arithmetic is
documented-essential and pinned by expensive evidence: WP-11
and WP-15 restructured the *shape* around it (an out-param bundle became
a result struct; the whole engine moved out of `PlanSolver` into its own
class) but never touched the arithmetic itself - both moves are diffable
as pure code motion. A change to the arithmetic itself is permitted when
it carries characterization tests of current behavior, the standard
adversarial review pipeline, and an explicit improved/regressed-nothing
statement, per `docs/KNOWN-ISSUES.md`'s policy note.

### 7.1 Offer tiering, in full

`EvaluateVendorOffers` splits offers on their non-coin cost lines, and two
kinds of line obey one rule: a non-coin wallet currency line, and a
*barter* line - an `Item` cost line whose item has no Trading Post price,
which is what an account-bound vendor token is. An `Item` line that does
have a TP price is money, not a barter line: it folds into the offer's
real coin cost and never consults a valuation.

An offer is **comparable** (competes with TP/craft coin costs in
`PickCheapest`) when it has no non-coin lines at all, or every one of them
has a valuation. Its comparison value is then coin part +
`sum(count * copperPerUnit)` over those valued lines, reported via
`VendorOfferEvaluation.BestComparableValue`. The winning comparable
offer's real coin part and (if any) currency lines are reported
separately, via `BestComparableCoinCost` and `BestComparableCurrencyCosts`;
the valuation affects comparison only, never the amounts committed to the
plan. A barter line's own scaled quantity rides on
`BestComparableItemCosts` with a null `GoldValue`, for the same reason.

An offer with at least one non-coin line that has **no** valuation
(including when it is mixed with other, valued lines) is incomparable with
coin costs and is reported only as a **fallback**. Fallback offers are
ranked on three keys, most significant first: whether the offer carries a
barter line, then its coin part, then unit count. A fallback coin-part tie
is broken by unit count only when both offers cost the same single
non-coin line, kind included; ties across different lines keep the
first-listed offer, because ranking across them has no exchange rate and
their unit counts must never be compared.

The barter key comes first because a barter line contributes nothing to
the coin part, so ranking on coin alone reads an unpriceable cost as a
cheap one. Three Secrets of the Obscure materials are each sold for a flat
250 map currency and are each also listed against several account-bound
zone reward chests. Every chest offer scored a coin part of 0 and beat the
flat price, and the plan told the player to acquire chests the module can
neither price, buy nor craft. Both offers stay selectable; only the order
changed.

That coin part is a **partial** accounting of the offer, and how partial
depends on which kind of line was left unvalued. An unvalued wallet
currency has no coin equivalent by invariant - the module refuses to
invent an exchange rate - and every competing route omits one the same
way, so ranking on the coin part is the only rule available. An unvalued
**barter** line is different: it is an item acquisition, exactly the thing
this solver exists to cost, and its omission is missing data rather than a
deliberate refusal. `PlanSolver.Evaluate`'s terminal fallback branch
therefore refuses to let an offer carrying a barter line win its
comparison against a craft route, whose real cost accounts for every
priceable component in its subtree. The offer stays reachable -
`CanBuyVendor`, the VENDOR pill and a manual override are all unaffected -
it simply cannot win on a price that omits most of itself. See section 8's
barter-offer rule and `docs/KNOWN-ISSUES.md` item 44.

An offer whose comparable-tier comparison value **overflows** demotes to
the fallback tier rather than being dropped from both: its coin part is
still real, and discarding it reported "no vendor route" for a route that
exists, purely because a user-supplied valuation was absurd. That is the
same treatment the per-line valuation accumulation beside it already gave.

A `DailyCap`/`WeeklyCap`/`SeasonalCap` never excludes an offer or affects
its tier - gw2efficiency only ever surfaces a cap as a post-solve notice,
never re-routing the tree - so both tiers carry the raw caps through for
`FinalizeVendorBatches` to check once against aggregate demand.

### 7.2 Why the ceil is merged, and why a conflict is not

The sum of independently-ceil'd per-occurrence costs overstates the true
cost whenever an item is needed via 2+ occurrences and bought via a bulk
offer, so `FinalizeVendorBatches` re-derives the cost from the aggregate
quantity and ceils once. It only does so when every occurrence resolved to
the identical winning offer: re-deriving one "true" cost across genuinely
different offers has no principled answer, so a `Conflict` step keeps
`AggregateStep`'s sum of real per-occurrence purchases - a deliberately
conservative fallback.

### 7.3 Allocating a corrected total back to occurrences

Without `AllocateVendorNodeCosts`, `CraftingTreeNode.SubtreeCost` (via the
public `Decisions` dict) kept showing the stale, per-occurrence-overcounted
sum after `FinalizeVendorBatches` had corrected only the merged
`PlanStep`/`currencyMap` view.

It touches only the stepKeys that method actually corrected -
`step.VendorOfferOutputCount > 0`, which is only ever set inside its
single-winning-offer branch and is 0 for the conflict/mixed-offer case.
Where occurrences disagreed on the winning offer, each occurrence's own
memo `TotalCost` is already individually correct (a genuinely different
real purchase), so redistributing a uniform rate across them would replace
correct values with a wrong blended one - the same reasoning
`FinalizeVendorBatches` itself applies to `step.TotalCost`.

The allocation is largest-remainder (Hamilton) apportionment, proportional
to each occurrence's own `Quantity` share of the step's total demand:
`floor(step.TotalCost * quantity / totalQuantity)` per occurrence, then the
leftover copper(s) - `step.TotalCost` minus the sum of floors, always fewer
than `occurrences.Count` - go one each to the occurrences with the largest
fractional remainder (numerator mod `totalQuantity`), ties broken by
first-seen (DFS) order for determinism. The allocated shares always sum to
precisely `step.TotalCost` (no drift, no invented precision) and any two
occurrences of equal quantity diverge by at most 1 copper. The multiply
widens to `decimal` so this holds unconditionally - no `long` overflow is
possible for any `step.TotalCost`/`Quantity` pair. A "last occurrence
absorbs the remainder" shape is not acceptable here: it dumps the entire
batch-overrun cost, unbounded for equal-quantity occurrences, onto
whichever occurrence lands last in DFS order.

A component leaf's raw `VendorItemCosts`/`VendorCurrencyCosts` are captured
pre-merge, per occurrence, and are *not* re-derived here - they can
disagree with the corrected share whenever a step merges 2+ occurrences.
The caller reads this method's outputs afterward to mark which decisions
must suppress component-leaf display, in
`FlagUnreliableVendorComponentCosts`.

**Full history:** KNOWN-ISSUES items 20.1, 20.2, 28, 33.

### 7.4 Cost lines are solved, not displayed

**What:** A vendor offer's `Item` cost line with no Trading Post price gets
a per-unit acquisition cost from the same `PlanSolver.Evaluate` a recipe
ingredient gets, run over a quantity-1 subtree, and folds into the offer's
real coin cost by the same `unit x count` multiplication a TP-priced line
already uses. `Services/VendorCostLineSubtrees.cs` holds the subtrees,
`Models/CostLineUnitValue.cs` is one line's answer,
`PlanSolver.ResolveCostLineUnitValue` is the recursion, and
`CraftingPlanPipeline.ExpandVendorCostLinesAsync` builds the inputs.

**Why:** the tree expanded recipe INGREDIENTS into solved nodes but vendor
COST LINES into unpriced display leaves, so a cost line was never itself
solved. The same components were therefore costed on the craft path and
free on the vendor path - an asymmetry in the data model, not a pricing
bug. Lyhr, in the Wizard's Tower, is a convenience vendor: his offers are
the craft or Mystic Forge recipe plus a fee, confirmed on the wiki at
three levels of the Obsidian armour chain, and 40 of his 132 offers charge
exactly 10 Globs of Ectoplasm on top. The Obsidian Heavy Breastplate was
recommended as a 2g95s10c purchase, that fee being the only part of the
offer anything costed. gw2efficiency never meets this because it has no
separate "vendor offer" concept at all - `recipe-calculation`'s
`src/static/vendorItems.ts` is an empty object above the comment "we now
manage vendor items via custom recipes" - so a merchant exchange is a
recipe there and is priced by the one code path. Unifying the COSTING
without unifying the display vocabulary is the same idea at this module's
seams: a vendor purchase must not start reporting itself as a craft.

**Displayed as a leaf, costed as a subtree.** A cost line's subtree never
enters the plan tree. What the user sees is the cost-component leaf that
already existed (KNOWN-ISSUES #47), now carrying the computed price where
it used to render a blank cell. Full expansion in the UI was measured
before being rejected: on item 101521 the cost-line graph closes at 77
item ids and 4,215 nodes against an 842-node plan tree, so showing it
would have multiplied a 340-node plan by roughly six and buried the plan
in acquisition chains for components the player is buying precisely so
they need not think about them.

**A side table, not a field on `RecipeNode`.** That type is reachable from
`Models/PersistedPlan.cs`, so hanging subtrees off it would bump the plan
schema version and discard every saved plan on the version it shipped in.
`PlanSolveContext` snapshots the resolved VALUES instead of the subtrees -
a few dozen small rows rather than several thousand `RecipeNode`s, on a
path that re-serializes the whole context on every override click. The
persisted graph gained additions only, so `CurrentSchemaVersion` stayed at
3; a plan saved before the change restores with no values and re-solves as
the solver did before expansion existed.

**Termination, and why the work is linear.** The cost-line graph is
genuinely cyclic - 86094 and 91232 buy each other, among at least twelve
cycles. Three independent bounds hold, any one of which suffices. A
`Visiting` set of item ids refuses re-entry, which cuts a cycle rather than
following it. Every id is written to the memo the first time it is asked
for - a value, or Unresolved when the attempt was cut - so no id is
evaluated twice and the total number of subtree evaluations is at most the
subtree count, which is also what the budget is set to. A depth cap bounds
a long acyclic chain the same way.

A cut answers Unresolved rather than a partial figure, and that answer is
memoized. Both halves matter. A partial figure looks like money and could
win a comparison it should lose, whereas Unresolved leaves the line a
barter line - the pre-expansion treatment, which section 8's barter-offer
rule stops any route winning on. Memoizing it is what keeps a cycle from
re-resolving its members combinatorially. The precision given up is real -
an id cut once stays uncosted for the rest of that solve - and is given up
in the safe direction.

**The two prices stay two prices.** A subtree's decision-only remainder
(its `ComparisonValue` above its `TotalCost`, which is what a valued wallet
currency under the line produces) rides in `valuationCopper` with every
other decision-only figure, so it can move a comparison and can never reach
a coin total. A subtree that carries an unvalued cost of its own leaves the
offer fallback-tier, exactly as an unvalued line always did: its coin part
is now real, but it is still not the whole story.

**A unit price, not a scaled solve.** The subtree is built at quantity 1
and multiplied by the line's count and `unitsNeeded`, which linearizes away
any batch-ceil non-linearity beneath the line. That is the same
approximation the TP-priced path has always made - a cost line has always
been `unit price x count` - and the change is only which sources may supply
that unit price.

**Superset domination**
(`Services/VendorOfferDomination.cs`). An offer charging a craftable
recipe's own ingredients plus a fee cannot be the cheaper route, and saying
so needs no prices at all: 104 of the 59,414 shipped offers are that shape.
Such an offer is barred from the comparable tier and cannot beat a craft
route in the terminal fallback, but is never dropped - it stays reported,
clickable, and still the answer when it is the only one. Every arm of the
check fails closed, and it answers false whenever competency is unknown:
"a recipe exists" is not "this account can use it", and only the second
makes the vendor redundant. An offer charging EXACTLY the ingredients and
nothing more is not dominated - it is a real alternative that skips the
discipline at no extra cost. This is a second, independent line of defence
rather than the fix: costing the cost lines already prices such an offer
above the craft it mirrors, and the two agree without either depending on
the other.

**Measured** on item 101521 over the shipped corpus, warm, 20 solves each,
with Globs of Ectoplasm at 2,916c as the only priced input (the same
single input the original report was measured with): plan tree 842 nodes;
77 cost-line subtrees totalling 4,215 nodes, built in 18ms; solve 2.1ms ->
13.6ms; an override re-solve from the snapshotted values 2.4ms, which is
the interactive path. The forced vendor route went from 29,160c - the
ectoplasm alone - to the craft route's cost plus exactly 29,160c, which is
what the wiki says the offer is.

**Gates this model still does not have** are recorded in
`docs/KNOWN-ISSUES.md` item 44.

### 7.5 The plan's non-coin price

**What:** `CraftingPlan` reports three costs, not one:
`TotalCoinCost`, `CurrencyCosts` (per wallet currency) and
`BarterItemCosts` (per untradeable vendor token). All three are summed
across the whole plan from the same merged, aggregate-then-ceil vendor
steps section 7 derives, never from the per-occurrence decision lines.
`PlanViewModel.NonCoinCostTotals` and the Total Cost section's table are
the display side, and they are one list projected from one set of rows so
the plan-level figure and the table a reader checks it against cannot
drift.

**Why the barter half was missing.** A barter line's units ARE the price -
nothing of it folds into any coin figure (section 8.3) - so before this,
its only record anywhere in the plan was `PlanStep.VendorHasBarterItemCost`,
a bool, plus per-node display leaves the tree suppresses whenever a merged
step's component costs are unreliable. Measured over the shipped corpus,
Legendary Rune (91536) buys 6 of its 7 vendor steps for no coin at all; a
plan of that shape presenting one gold figure presents a fraction of its
own price.

**Never folded together.** A currency total, a barter total and a coin
total are three quantities in three units, reported side by side. The
module holds no exchange rate between them and must not invent one: a
`CurrencyValuation` moves a comparison and never a committed total
(section 8.3), and `Gw2Constants.CoinCurrencyId` is excluded from
`CurrencyCosts` precisely so coin is never double-reported as a currency
line. The two id spaces stay apart for the same reason `BarterItemCost` is
a separate type from `CurrencyCost`: item 24 and currency 24 are unrelated
things.

**What the coin total still leaves out.** A cost line resolved through a
subtree (section 7.4) contributes only its `RealCoin` to the offer above
it; whatever CURRENCY that subtree spends is not carried up, so it reaches
neither `CurrencyCosts` nor any other plan-level total. `CostLineUnitValue`
records only that such a cost existed (`HasUnvaluedCost`), which is enough
to keep the offer fallback-tier but not to report the quantity. Closing
that would mean carrying cost lines up through `CostLineUnitValue` and
de-duplicating them against the main tree's own demand, which is why it is
recorded here rather than done in passing.

**The floor disclosure.** Because a plan can carry a real cost the coin
total counts as zero - an unpriceable node, or a barter line - the Total
Cost section marks its tiles and states that the totals are a floor. The
gate is that condition, not `TotalCoinCost == 0`: it had been the latter,
which silenced the sentence on exactly the priced plans where a reader is
most likely to mistake the coin figure for the whole answer (Legendary
Rune: 49 unpriceable components under a five-figure silver total, with the
disclosure suppressed). The zero-total case keeps its own narrower
consequence, suppressing the profit band, which the widened gate does not
touch.

---

## 8. Solver decision rules

**What:** `Services/PlanSolver.cs` decides, per node, whether to craft,
buy from the Trading Post, buy from a vendor, or fall back to "unknown
source" - echoing gw2efficiency's own `cheapestTree` behavior rather than
inventing a new one. The load-bearing rules:

- **TP buy is the baseline and wins every tie.** Craft beats buy only when
  *strictly* cheaper; a missing buy price counts as "beats buy" (force-craft
  - there is nothing else to compare against). Vendor follows the identical
  rule against buy. When both craft and vendor beat buy, the numerically
  cheaper of the two wins; an exact craft/vendor tie keeps vendor.
- **Buy-order vs sell-listing basis** is a caller-supplied price basis
  threaded through every comparison, matching whichever basis the user
  selected in the UI - but it is *preferred per item*, not force-applied
  regardless of data: `PlanSolver.GetUnitPrice` tries the basis-preferred
  TP side first, and only when that SAME item has no listings on its
  preferred side does it fall back to that same item's other TP side
  rather than treating the item as unpriceable (see KNOWN-ISSUES.md,
  "AUDIT ROW 20/38"). This is a per-item same-item substitution: no
  single item is ever priced on a mixed basis, and an item with listings
  on its preferred side never touches the other side. A total summed
  across items - e.g. a craft cost built from several ingredients - can
  still combine sides when a fallback fires on one of them, so the
  guarantee is scoped to "no single item," not "no comparison anywhere
  in the tree." Currencies (as recipe ingredients) contribute to the
  craft-vs-buy *decision* via an optional per-unit valuation, but never to the
  displayed real coin cost - an unvalued currency never has an invented
  exchange rate.
- **Barter offers:** a vendor offer's `Item` cost line is money only when
  that item has a Trading Post price - it is then folded into the offer's
  real coin cost as before. An item with NO TP price is a *barter line*: an
  account-bound token whose units are the cost. Measured over
  `ref/vendor_offers.json` on 2026-08-28, 654 of the 1,032 distinct item
  ids used as vendor costs have no TP price at all, covering 49% of item
  cost-line usages, so this is the common case rather than the exotic one.
  A barter line obeys exactly the same rule a non-coin currency line does:
  with a valuation it folds into the offer's comparison value (never into
  the committed coin cost); with none it makes the offer fallback-tier.
  Either way the offer survives - dropping it reported "no vendor route"
  for items that are genuinely purchasable, just not with gold. Curated
  per-item defaults live in `Models/BarterItemDecisionDefaults.cs`, the
  Item-keyed twin of `CurrencyDecisionDefaults`, and an item with no entry
  there simply stays unvalued and fallback-only. A committed decision that
  is paid partly in barter is flagged `VendorHasBarterItemCost` on both
  `PlanStep` and `CraftingTreeNode`: it is the twin of a non-empty
  `VendorCurrencyCosts`, and every consumer that reads a coin figure as
  the whole cost must check both. **A barter line is never worth zero.**
  An offer carrying one is never ranked on its coin part against a craft
  route in the terminal fallback branch - that part omits the barter line
  entirely, so it is a partial accounting being compared with a complete
  one, and the offer would win on a price missing most of itself. It stays
  offered and manually selectable; it just cannot win that comparison.
  Section 7.1 has the currency/barter asymmetry this rests on. Since
  section 7.4 a cost line is a barter line only when NOTHING can price it,
  not merely when the Trading Post cannot, so this rule now covers what
  pricing genuinely cannot reach rather than the common case.
- **Craft/vendor comparability parity:** a recipe with an unvalued
  Currency-type ingredient is fallback-tier - never comparable with a real
  TP/vendor coin price in `PickCheapest` - exactly like a vendor offer
  carrying an unvalued non-coin currency line already is
  (`VendorBatchSolver.EvaluateVendorOffers`). Still offered (`CanCraft`
  stays true) and used as a last resort when nothing comparable exists at
  all. See KNOWN-ISSUES' "Craft/vendor comparability parity fix" entry.
- **Mystic Clover-style EV pricing:** fractional-output Mystic Forge
  recipes have their ingredient quantities pre-scaled upstream (by
  `RecipeService`, kept in sync by `InventoryReducer`) to the expected
  number of forge attempts needed at the recipe's success rate. `PlanSolver`
  does not re-apply any ratio on top of that - doing so would
  double-amortize the cost.
- **Force-craft:** a node with a recipe but no buy price always crafts
  (there is no buy cost to lose to), matching gw2efficiency's
  `isCheaperToCraft = craftPrice-defined && (!buyPrice || decisionPrice < buyPrice)`.

**Where:** `Services/PlanSolver.cs` (`Evaluate`, `SelectBestRecipes`,
`PickCheapest`); the normative spec these rules echo is
`docs/gw2e-parity-spec.md`. Whole-result goldens for these decisions live
in `tests/TaimisToolbench.Tests/Goldens/plan-solver/` - a difference
there is a finding to investigate, never a file to re-baseline.

**Full history:** KNOWN-ISSUES items 20, 21, 24, 25, 26 (the M33-M37
parity waves); `docs/gw2e-parity-spec.md` for the researched gw2efficiency
behavior itself.

### 8.1 Achievement-bit ingredient dedup

`Services/AchievementBitDedupPrePass.cs` echoes gw2efficiency's own
two-part mechanism (`initialTreeChecks` plus `calculateTreeQuantity`'s
`achievement_bit` check -
[`docs/research/m37-r3-achievement-dedup.md`](research/m37-r3-achievement-dedup.md)
sections 1.1/1.2) for a small handful of real recipes: the WvW "Infinite
[siege weapon] Blueprint" achievement rewards, whose ingredients name a
specific achievement *bit* - a one-time reward item that must never be
counted twice just because it also happens to be needed directly
elsewhere in the same plan. The rule itself is ported 1:1 from the
ground-truth gw2e unit test quoted in that report (section 1.4) and is
stated in the class's own doc comment.

**Zeroing clears `Recipes`, not just `Quantity`.** Unlike gw2e's nested
tree - which stores a small per-edge ratio and resolves every absolute
quantity in one downstream pass - this module bakes each `RecipeNode`'s
absolute `Quantity` once, at tree-build time
(`RecipeService.BuildNodeAsync`, report sections 3.3/4.2). Zeroing a
duplicate occurrence here therefore also clears that occurrence's own
`Recipes`, mirroring `InventoryReducer.ReduceNodeSourced`'s identical
"`Quantity <= 0` -> `Recipes.Clear()`" treatment of a genuinely
fully-owned node, so `PlanSolver.Evaluate` has no craft path left to
consider and the ordinary zero-quantity Buy/Have collapse takes over
cleanly. Without clearing `Recipes`, a duplicate occurrence with no
TP/vendor price but a real craft recipe could still resolve to Craft -
using its own, un-deduped children's costs - purely because nothing
cheaper competed, re-introducing exactly the double count this pass
exists to remove. That is a deliberate departure from literally "zeroing
hits `Quantity` only".

**It runs once and never again.** The pass fires right after the tree is
built, before inventory reduction and before Solve
(`CraftingPlanPipeline`), and never again for that tree's lifetime - not
even across local override/Ignore re-solves, which reuse the same tree
object. gw2e's own equivalent interactive-update path (`updateTree.ts`)
does not re-run its classification pass and can let a "shared with a
normal occurrence" dedup silently un-zero itself after a manual pill click
(report section 1.5, an upstream fragility). Running once and never again
avoids that class of bug entirely, which is strictly safer than upstream
rather than a parity gap.

**Both walks descend only the primary option.** `CollectItemIdsForDedup`
and `ZeroDuplicateBitOccurrences` each follow `node.Recipes[0]` only,
mirroring `InventoryReducer.ReduceNodeSourced`'s precedent for the
identical ambiguity: `PlanSolver` has not run at pre-pass time, so which
of a node's alternate `RecipeOption`s will actually be chosen is
unknowable here, and gw2efficiency's own nested tree never has this
ambiguity at all (recipe-nesting resolves exactly one recipe per node
before pricing). Walking every option - the pre-fix behaviour - could
classify an achievement-bit occurrence living only in an option
`PlanSolver` never chooses as "seen", corrupting the zeroing decision for
a sibling option's occurrence of the same id that *is* on the solved path.
On the zeroing side the stake is higher still: zeroing an occurrence in a
never-chosen option would silently discard that option's true cost from
`Evaluate`'s comparison (which sums each option's own ingredient costs
independently), making an objectively worse option look artificially
cheap enough to be picked over the honest primary one.

Descending stops at a zeroed occurrence. Everything below one is dead
weight the ordinary zero-quantity path already hides, and - per the
verified 7-recipe/28-ingredient dataset this pass targets - never itself
contains a further achievement-bit id needing independent zeroing.

The pass is pure, Blish-free and does no I/O; it mutates the passed-in
tree in place, the same seam `OwnedMaterialsForceBuyPrePass` occupies
conceptually, though this one needs no `NodeId`s and no throwaway solve.

### 8.2 Owned materials: decision-guided reduction

`Models/OwnMaterialsMode` picks how the plan values materials the player
already owns. An owned unit is always consumed first, at zero acquisition
cost, in either mode - the enum never makes an owned unit *cost*
anything. What `Valued` adds is three things: a zero-owned decision pass
before the real solve (reusing the force-buy pre-pass baseline) that
excludes a node from crafting when buying it outright costs less than 85%
of what its own components would cost to buy fresh, gw2e's
`getCheaperToBuyItemIds`; a decision-*guided* rather than merely gated
reduction; and a deduction of owned materials' trading-post sell
opportunity cost from `CraftingProfit`, computed from the decision-guided
`UsedMaterials` list. `Free` falls back to the legacy
primary-recipe-option heuristic unchanged. All of it is inert unless an
account snapshot actually drove reduction.

**What "decision-guided" buys.** In `InventoryReducer.ReduceNodeSourced`,
when the guide contains a node's `NodeId`, only the recipe option whose
`Source == Craft && RecipeId` matches may let its descendants consume the
pool; every sibling option is left at full zero-owned cost. If the node's
zero-owned decision was anything other than Craft, no option consumes the
pool for its descendants - an un-crafted branch never demands its own
ingredients, mirroring `PlanSolver.Evaluate`'s own `ignoredItemIds`/
`Quantity == 0` handling. Since discounting only ever lowers a cost, and
only along the path the zero-owned pass already declared the winner,
owned ingredient stock further down the tree can never pull the real
post-reduction `Solve()` toward a *different* recipe option than the
guide chose for that node.

Without a guide, the legacy heuristic is true only along the single
chosen-recipe-candidate chain: the root, then recursively each node's
primary option. Which option the solver will actually choose is unknowable
at reduction time, and walking every option while letting each drain the
shared pool would let an option the solver never picks steal owned stock
from a branch that *is* chosen. Once an option's descendants are excluded
they stay excluded for the whole subtree below, however deep - the branch
is hypothetical, or provably not the winning path, from there down.

Every option's `CraftsNeeded`/ingredient `Quantity` is still rescaled
regardless of `consumeFromPool`. That arithmetic reflects the node's own
already-decided, pool-independent `Quantity` and is required for
`PlanSolver`'s cost comparison across recipe options to stay internally
consistent, since every ingredient of every recipe is always evaluated -
even one the solver ultimately does not choose.

**The residual (KNOWN-ISSUES #20, not guarded or tested).** None of this
guarantees the guide's own Craft-vs-Buy decision for a node still holds
after reduction. The guide is computed on the unreduced tree, but a node's
own `Quantity` can still shrink from owned stock of its own item id - and
because craft cost is non-linear in quantity (`ComputeCraftsNeeded`'s
ceiling division, `VendorBatchSolver`'s per-batch math), shrinking it can
raise the effective per-unit cost enough to flip the real solve's decision
away from what the guide assumed, after that node's ingredients were
already discounted into `UsedMaterials` against a Craft assumption. It
takes a node with owned stock of itself plus owned stock of its own
ingredients, and a recipe or vendor batch whose output count exceeds 1.

`OwnedMaterialsForceBuyPrePass`'s competency-blind second evaluation
carries a residual of the same shape. "Competency-blind" applies only at
the node's own recipe choice; ingredient costs stay the normal
competency-resolved figures, which can only inflate the raw craft cost and
therefore only *add* nodes to `CompetencyIndependentForceBuyNodeIds`,
never drop them. The risk left is a parent whose untrained recipe would
survive a true blind evaluation being pulled in by an inflated child
contribution, falsely excluding a real training opportunity at the parent
- the child's own opportunity is still reported at the child's node.

### 8.3 Decision-only valuations: currencies and barter items

Two curated tables answer "what is this non-coin cost worth, for
comparison purposes only": `Models/CurrencyDecisionDefaults.cs` for wallet
currencies and `Models/BarterItemDecisionDefaults.cs` for untradeable
barter items. They are separate tables because a GW2 item id and a GW2
currency id are different id spaces that collide numerically - currency 39
and item 39 are unrelated things - so a single int-keyed map would answer
the wrong question for one of them. `Models/CurrencyValuation.cs` holds
the user's own overrides and clears over the top of both.

The currency table's first block is adapted from gw2efficiency's
`CURRENCY_DECISION_PRICES` (`@gw2efficiency/recipe-calculation`,
`src/static/currencyDecisionPrices.ts`, MIT, Copyright (c) 2016
queicherius / David Reess). Shipping it as defaults is an explicit,
one-time waiver of the repo's "do not invent data" rule for that table
only: every value in that block is sourced and attributed to the upstream
MIT package rather than invented, and the permission notice the licence
requires is reproduced verbatim in the source file itself. Research notes live in
[`docs/research/gw2e-currency-decision-prices.md`](research/gw2e-currency-decision-prices.md).

gw2efficiency's table stops at id 70 and never gained a row for the
Secrets of the Obscure, Janthir Wilds or Visions of Eternity currencies,
so a second block in the same file carries values derived here. Its rule:
the most coin one unit converts into through an **uncapped** vendor offer
in `ref/vendor_offers.json` whose cost is that currency plus at most a
minor coin component, priced at the live trading-post sell listing; or,
where the game sells the same goods at the same counts for an
already-valued sibling currency, that sibling's value. Capped offers are
excluded for the same reason the barter table only counts repeatable
exchanges: a weekly-capped conversion cannot absorb a stock of the
currency, so it does not price the marginal unit. Erring high is the safe
direction: an over-valued currency can lose a comparison it should have
won, never win one it should have lost.

| id | Currency | Value | Derivation (prices fetched 2026-08-29) |
|---|---|---|---|
| 30 | PvP League Ticket | 3770 | League Vendor sells 10 Shard of Glory for 1 ticket, uncapped; Shard of Glory sells at 377c against 1.2M listings. |
| 66 | Ancient Coin | 197 | Chin-Hwa sells `Recipe: Harrier's Monastery Shoes` for 5, uncapped; TP sell 987c over 2,010 listings. The same vendor's other recipes imply 40-197, and Leivas' Antique Summoning Stone (10 coins, TP sell 13,792c) implies 1,379 but is capped at one per week. |
| 76 | Ursus Oblige | 125 | Maw of the Volcano sells Potent Standard Sharpening Stone for 7 plus 120c, uncapped; TP sell 995c over 8,798 listings. Its other low-coin routes there imply 87-125. |
| 77 | Gaeting Crystal, the current expansion's raid currency | 3600 | Its only two live offers are one uncapped exchange, 1 crystal for 1 Magnetite Shard (currency 28, 3600), at `Scholar Glenna (Mount Balrior)` and `Titan Specialist Tante`. Corroborated by the tables those vendors used to run: each of the 80 remaining crystal-priced cost lines has an exact twin - same output item, same output count, same complete cost line set with 28 substituted for 77 - among the Mount Balrior shard-priced offers (`Raid Expert's Portable Magnetite Shard Exchange/Mount Balrior` carries all 80, `Scholar Glenna (Mount Balrior)` 8 of them), which are the same Janthir Wilds raid's vendors after the conversion moved them from crystals to shards. 40 output items are priced in both currencies at identical counts; zero divergences. |
| 82 | Testimony of Castoran Heroics | 135 | At the Notary of Heroics the same items cost the same counts in Castoran, Desert (36) or Jade (65) Heroics, at 1, 6, 10, 100, 250 and 500. Both siblings are 135 upstream, so any other figure would contradict the block above. Corroborated independently: 6 buy a Siege Golem Blueprint, TP sell 791c, giving 132. |

Currencies left deliberately unvalued, with the reason each resists a
single defensible figure:

- **63 Astral Acclaim** (127 offers). Settled by
  [`dev/proposals/research-aa-spending-consensus.md`](../dev/proposals/research-aa-spending-consensus.md):
  Wizard's Vault deal quality varies per item and per price tier, so one
  implied rate misrepresents a supply curve as a point. The successor idea
  is a ranked deal table, not a table row.
- **72 Static Charge, 73 Pinch of Stardust, 75 Calcified Gasp** (161
  offers). The Wizard's Tower and Gobbler converters sell one catalogue for
  25 units of *any* map currency, so these are 1:1 with currencies the
  block above values at 9 (Trade Contract), 70 (Ley Line Crystal), 310
  (Tyrian Defense Seal) and 320 (Imperial Favor). Inheriting a sibling
  gives a 35x spread with no way to choose, and none of the three has a
  trading-post-tradable output to anchor on.
- **78/79/80 Rift Essences** (111 offers). Their only cross-currency anchor
  prices all three tiers identically, which contradicts the tiering; no
  route ends in a tradable item.
- **70 Legendary Insight** (148 offers) and **58 War Supplies** (114
  offers). Marked `undefined` upstream, and no vendor route turns either
  into anything tradable.
- **81 Antiquated Ducat, 83 Aether-Rich Sap** (54 offers). Visions of
  Eternity map currencies with no tradable output and no cross-currency
  offer anywhere in the seed.
- **47 Racing Medallion** (35 offers). Its two anchors are a 16c bottle of
  wine and a single-listing 400 gold cosmetic, 333x apart with nothing
  liquid between them.
- **59 Unstable Fractal Essence** (29 offers), **46 PvP Tournament
  Voucher** (14), **52 Red Prophet Shard** (9), **54 Blue Prophet Crystal**
  (5). Low impact, and no uncapped anchor. Red Prophet Shard is the
  instructive one: the three Eye of the North Emissaries sell the same item
  for the same 2 units of Red, Green (3500) or Blue (300) Prophet Shard, so
  inheriting a sibling would mean choosing between two upstream values that
  are already 12x apart.

**Rolling raid currencies, and the one row dropped from upstream.** The
live API gives two wallet currencies the same name, "Gaeting Crystal".
Currency 39 is the Path of Fire one: it was retired on 2022-07-19, every
held balance force-converted into Magnetite Shards (currency 28), and no
account has been able to hold one since. It is absent from
`CurrencyDecisionDefaults` even though gw2efficiency's upstream table
values it at 3600 - a deliberate divergence, not drift, annotated at the
row in the research notes above. Its item form, item 86094, is absent from
`BarterItemDecisionDefaults` for the same reason. Nothing in
`ref/vendor_offers.json` charges currency 39 at all, and every offer that
charges item 86094 belongs to one merchant the wiki marks historical,
`Scholar Glenna (Gaeting Crystal)`.

Currency 77 is the live one, and it is a *rolling* currency rather than a
stable one. Each expansion its vendors are switched over to Magnetite
Shards and every held balance is converted, but the id itself carries the
role forward instead of being replaced the way 39 was: id 77's own
`/v2/currencies` description still names Janthir Wilds while the wiki
records the current content as Castora and flags the description as stale.
So what a Gaeting Crystal buys - and therefore what one is worth - has a
shelf life of one expansion. Any hardcoded valuation for id 77 is a
snapshot of one expansion, not a constant, and is due a re-derivation
whenever the next one ships. Measured evidence for all of the above,
including the API and wiki captures:
`dev/records/gaeting-crystal-duplicate-ids.md`.

The barter table has no upstream to adapt - gw2efficiency values wallet
currencies only - so each entry is derived here under a single stated
rule, recorded per entry in the file: the cheapest repeatable vendor
exchange in `ref/vendor_offers.json` whose entire cost is coin or a
currency that already carries a `CurrencyDecisionDefaults` value, divided
by that offer's output count.

That rule is deliberately conservative in one direction. It can only ever
name a route we can see, so it is an *upper* bound on what the item really
costs to obtain, and an over-valued barter token makes its offer look
dearer than it is: such a token can lose a comparison it should have won,
but can never win one it should have lost. An item whose cheapest visible
route bottoms out in another untradeable item, an RNG chest, or a
time-gated daily craft is absent on purpose. Absent is a supported state,
not an unfinished one - the offer still reaches the user, as an honestly
unranked fallback (section 8's barter-offer rule).

**Where a decision value may appear.** The original rule was "may tip a
comparison, never reaches a displayed total". It now has a second permitted
use, and the boundary moved from *what may consult a valuation* to *what
kind of number may carry one*:

- A decision value may weight a **ratio** - a dimensionless 0..1 figure the
  module prints as a percentage. The Crafting Ranker's materials gate is
  the one such reader (`Services/RankerReadinessCalculator.cs`,
  `ScoreMaterials`): the gate is the plan's whole bill in copper, so the
  same valuation stands above and below its line and a wrong rate moves
  both halves together.
- A decision value may **never** reach a coin total the module presents as
  money. `CraftingPlan.TotalCoinCost`, the Ranker's Remaining column and
  every Total Cost row stay coin the user will actually spend.

An unvalued currency is not zero under either rule. The solver demotes its
offer to the fallback tier (section 7.1); the materials gate drops it from
both halves, reports it in
`RankerRowMetrics.MaterialsUnpricedCurrencyIds`, and the Ready hover names
it. The Ranker's separate currencies gate scores it in its own units
regardless, which is why dropping it from a copper ratio loses a weighting
rather than a barrier. MEASURED over the shipped corpus: 61 distinct
non-coin currencies appear as a vendor cost, 45 of them carry a value, and
820 of the 33,849 offers with a non-coin currency cost - 2.4 percent -
charge one that does not.

---

## 9. Data pipeline: seeds, wiki scrapes, dev-only caches

**What:** The module reads several JSON files under `ref/` at runtime
(recipes, item names, vendor offers, Mystic Forge recipes, acquisition
hints) - all produced **ahead of time** by offline tools under `tools/`
and committed to the repo. Nothing under `Services/`/`Views/` fetches from
gw2efficiency or the GW2 Wiki at runtime; `gw2efficiency` is research-only,
consulted at dev time to write `docs/gw2e-parity-spec.md` and never called
from module code.

- `tools/TaimisToolbench.RecipeSeeder` queries the official GW2 API
  (`api.guildwars2.com`) to build `ref/recipes_seed.json` and
  `ref/recipe_search_seed.json`.
- `tools/VendorOfferUpdater` scrapes the GW2 Wiki's Semantic MediaWiki
  `action=ask` API (`WikiSmwClient`) for vendor-sold items, resolves
  currency names via the official GW2 API, and writes
  `ref/vendor_offers.json`. It also seeds vendor purchase caps
  (daily/weekly/seasonal) and Homestead Refinement tier data from the same
  wiki properties.
- `tools/MysticForgeSeeder` scrapes the wiki for Mystic Forge recipes to
  build `ref/mystic_forge_recipes.json`.

Two of the files these tools produce as **intermediate working state**
(`ref/wiki_vendor_cache.json`, `ref/item_id_cache.json`) are dev-only
inputs to `VendorOfferUpdater`'s own incremental-scrape workflow, not
consumed by the shipped module at all; they are gitignored rather than
committed (see `docs/RELEASING.md` for the packaging implication of a
dev machine still having them on disk locally).

**Staleness policy (recipe cache):** the shipped recipe seed plus the
runtime overlay under `<dataDir>/recipe_cache/` are kept forever - a game
build change never invalidates either. Learned negatives are not stored
at all: "no recipe outputs this item" is derived at lookup time from the
corpus the module holds, and licensed as exact by a once-per-build
background probe of the `/v2/recipes` id list that fetches and folds in
any recipes the corpus lacks. The build id in the overlay manifest is
provenance and a probe cheap-out, not a wipe trigger; the manual route
out of a bad overlay is Clear Cache. Where:
`Services/Recipes/CompositeRecipeCacheStore.cs` (derived negatives),
`Services/Recipes/RecipeCorpusVerifier.cs` (the probe),
`Services/Recipes/OverlayRecipeCacheStore.cs` (keep-forever overlay).

That probe answers "does a recipe exist", not "what does it consume": it
only ever fetches ids the corpus lacks, so a recipe whose id never moved
but whose ingredients changed in place would be served stale forever.
Recipe 14025's rift essences turning from items into wallet currencies
(KNOWN-ISSUES #48) is the case that actually happened. A second, lazy
phase closes it - `Services/Recipes/RecipeCorpusRefresher.cs` refetches
the content of every held positive recipe once per game build, in batches
of 200 with a pause between them, and stores what comes back rather than
diffing to decide whether to believe it. The response IS the current
shape; the one comparison in that class decides only whether the row needs
*writing*, which keeps the overlay from becoming a 10 MB duplicate of the
shipped seed. It walks ids ascending after a priority pass over the recipes
reachable from the Ranker watchlist, the restored plan and plan history
(`Services/Recipes/PriorityRecipeIds.cs`), so an interrupted sweep resumes
from a single cursor in the overlay manifest and has already repaired what
the user was most likely to hit. Nothing waits on it: the verifier licenses
negatives, this repairs positives, and plan generation uses the best corpus
it has while this improves it underneath.

**Item-id resolution in `tools/MysticForgeSeeder`** (`TryResolveId`). Each
output and ingredient name has up to two candidate item ids: the id the
name itself resolves to on the wiki, and the id the wiki's recipe subobject
asserts. The name-resolved id wins wherever it succeeds, because a page
that declares its own item id is stating the id of the item that page is
about. The wiki states an asserted id explicitly only when the recipe
template carries an `output item id` parameter; otherwise it derives one by
name lookup, which picks an arbitrary member of a same-name pair - GW2
ships several, e.g. `Recipe: Satchel of Mighty Embroidered Armor` is both
9960 and 9962 - so it is the weaker source wherever both exist.

The asserted id is nonetheless what makes multi-variant equipment
resolvable at all. A page like `Ardent Glorious Armguards` covers an
ascended and a legendary item, holds no page-level `Has game id`, and names
its recipe's output `Ardent Glorious Armguards (legendary)`, which is no
page at all. Every id it has lives on an `equipment variant table row`
subobject, and the recipe template's explicit `output item id` is the
wiki's own statement of which row the forge produces.

**Where:** loaders - `Services/VendorOfferLoader.cs`,
`Services/Recipes/RecipeCacheSerializer.cs`,
`Services/Recipes/ItemNameSeedData.cs`; wiki
scraper - `tools/VendorOfferUpdater/WikiSmwClient.cs`.

**Full history:** KNOWN-ISSUES items 24, 28, 33; `CONTRIBUTING.md`'s
"Where seed/reference data comes from" section for the day-to-day workflow.

---

## 10. Post-solve annotation passes

**What:** four pure, Blish-free calculators run after the display tree
(`CraftingTree`/`MultiItemRoots`) is built: `CompetencyOpportunityCalculator`,
`ExcessCraftOutputCalculator`, `RecipeSheetSavingsCalculator`, and
`SeasonalVendorTipCalculator`. Each writes exactly one `CraftingPlanResult`
list (`CompetencyOpportunities`/`ExcessCraftOutputs`/
`RecipeSheetSavingsOpportunities`/`SeasonalVendorTips`) - one collection
each, never another pass's, never `Plan` or a total. `SellSideEconomics`
sits adjacent (same "pure, post-tree" shape) but is **not** a member: it
writes displayed totals the Total Cost section renders directly
(`NetSaleValue`/`CraftingProfit`), not an advisory Notes list.

All four are wired at three producer call sites, by name -
`CraftingPlanPipeline.GenerateStructuredAsync` (single-item),
`GenerateStructuredMultiAsync`, `ResolveWithOverrides` - plus one consumer
edit site, `PlanViewModelBuilder.BuildNotesSection`, which reads all four
lists to render their Notes rows. A fifth pass means touching all four
sites; there is deliberately no `ApplyAll` seam collapsing the three
producer calls into one, because the four calculators do not share a
signature (differing inputs - `learnedRecipeIds`, `vendorOffers`,
`characterDisciplines`). Rejected as premature; see
[`docs/DECISIONS.md`](DECISIONS.md).

The call order at each producer site - `SellSideEconomics` first,
`CompetencyOpportunityCalculator` last - is convention (kept identical
across all three sites for readability), not a data dependency: every
pass here reads only the already-built display tree, never another
pass's output, so any order between them is byte-identical.

**Where:** `Services/CompetencyOpportunityCalculator.cs`,
`Services/ExcessCraftOutputCalculator.cs`,
`Services/RecipeSheetSavingsCalculator.cs`,
`Services/SeasonalVendorTipCalculator.cs`; wiring in
`Services/CraftingPlanPipeline.cs`; consumer in
`Services/PlanViewModelBuilder.cs` (`BuildNotesSection`).

**Full history:**
[`dev/archive/known-issues/2026-08-17-annotation-detection-post-solve-advisory-list.md`](../dev/archive/known-issues/2026-08-17-annotation-detection-post-solve-advisory-list.md)
(the mutation-testing gap that produced these four passes); each
calculator's own class doc comment for its individual rationale.

---

## 10a. Account data a plan reads

**What:** which parts of the account snapshot a plan generation actually
consults, established from `Services/CraftingPlanPipeline.cs` rather than
assumed. `Services/StaleAccountDataWarning.cs` is the executable copy of
this table, and it decides whether a failed refresh is worth a dialog.

| Snapshot member | What the plan does with it | When it is read |
| --- | --- | --- |
| `Items` | `AccountItemIndex` feeds `InventoryReducer.Reduce`, which subtracts owned stock from the tree. Also the source of the display-only owned amounts on vendor item cost lines. | Only when Use Own Materials is on, which is the same thing as the pipeline being handed a non-null snapshot. |
| `Wallet` | `AccountCurrencyIndex` fills `CraftingPlanResult.OwnedCurrencyAmounts`, the "you have N" figure beside a currency cost. Never fed back into a decision. | Only when the plan has a non-coin currency cost, a barter trade-up currency, or a vendor offer currency cost line. |
| `CharacterDisciplines` | `CraftCompetencyEvaluator.BuildBestRatingByDiscipline` gates whether a Craft decision may win automatically. | Whenever the plan crafts. Passed independently of Use Own Materials, so it is read even when the plan ignores holdings. |
| `CoinCopper` | Nothing. | Never. The solver folds coin costs into `Plan.TotalCoinCost` and never asks what the account holds. The Crafting Ranker reads it; a plan does not. |
| `LegendaryArmoryEquipped` | Nothing. | Never. It names who wears an armory item; the armory's counts arrive in `Items` under the `LegendaryArmory` source. |
| `CapturedAt`, `CharacterCount`, `IncompleteCharacterCount` | Status text only. | Never by the solve. |

`Items` is one flat list, and the only record of which part of the account
holds what is the `Source` string on each entry
(`Services/AccountItemIndex.cs` owns that vocabulary). So "does material
storage hold anything this plan needs" is answered from what material
storage held at the last successful capture. That is exact for what the
plan subtracted, and blind to anything the account acquired since - which
is the limit the dialog's wording respects.

**What this cannot say:** whether refreshing a source would have changed
the plan. Knowing that needs the read that failed. The dialog therefore
names what went unread and what in the plan depends on it, and claims
neither that the plan is wrong nor that it is fine.

**Where:** `Services/StaleAccountDataWarning.cs` (the decision and the
wording), `Services/AccountDataSource.cs` (the source vocabulary),
`Services/PlanItemIds.cs` (what the plan needs),
`Views/CraftingPlanView.cs` (`RaiseStaleAccountDataDialog`).

---

## 11. Typography: the measured type ramp

**What:** `Services/TypeRampMetrics.cs` holds the measured Menomonia glyph
metrics behind every vertical constant in the plan view, and names which
ramp tier each chrome role sits in (section title, column header, status,
body, caption). It is pure and Blish-free; `Views/Rendering/UiFonts.cs`
is the only place that resolves an actual `BitmapFont`.

**Why:** the numbers are measured, not chosen, and two of them are
measured *defects* that a later contributor would otherwise rediscover the
expensive way, in a live sandbox session:

- **18-regular is unusable for prose.** Its space glyph advances 4px,
  against 7 at 16-regular and 9 at 18-bold, so any multi-word string at
  18-regular renders with collapsed word gaps. The status line is
  therefore bold at 18, not regular.
- **22-regular is metrically a 24** - same line height, cap height and
  advances as 24-regular, different file bytes - so there is no
  regular-weight step between 20 and 24, and 22-regular must never be
  loaded. 22-bold is a genuine intermediate.
  `TypeRampMetrics.HasUsableRegularFace` refuses exactly these two sizes,
  and `UiFonts.Regular` refuses them again at the seam.
- **Two pixels of clearance under a descender, never one.** Blish's
  UI-scale transform is non-integer, so a single-pixel margin can be lost
  in the rounding. Every band height and divider clearance is a statement
  about `TypeRampMetrics.InkBottom`, derived from the lowest ink of any
  printable ASCII glyph at that size.
- **Some Blish controls are locked to Font14** (`Checkbox`,
  `StandardButton`, `TextBox`, `Dropdown`). Measure them in the caption
  tier; do not try to restyle them.

The metrics come from parsing the installed
`Content/fonts/menomonia/menomonia-{size}-{style}.xnb` files directly -
uncompressed MonoGame XNB containers holding one `BitmapFontReader` asset
(lineHeight, then nine int32 per glyph region). Widths follow
MonoGame.Extended's own `MeasureString` rule, which is what a Blish `Label`'s
autosize calls. The parse reproduces, glyph for glyph, the figures published
in [`docs/research/minimum-window-width.md`](research/minimum-window-width.md).
The atlas covers 8-36 regular and 8-24 plus 36 bold, reached via
`ContentService.GetFont`, not only the five Blish defaults.

**Where:** `Services/TypeRampMetrics.cs` (metrics, tier seats, and the
`InkBottom`/`BaselineAlignedY` arithmetic every constant is written in);
`Views/Rendering/UiFonts.cs` (font resolution);
`Services/PlanContentHeightMath.cs` (the band heights those metrics feed).
[`.impeccable.md`](../.impeccable.md) at the repo root carries the
tool-facing design summary and points back here.

## 12. Plan persistence compatibility: the request/result split

**The contract.** A plan file written by *any* shipped build must remain
loadable by *every* later build, with graceful degradation of the parts
that cannot be read. Concretely, in the order the guarantees weaken:

1. The **request** - the items, quantities, `UseOwnMaterials`,
   `PriceBasis`, `ValueOwnMaterials` and ignored ids - always survives.
   It is versioned by `PersistedPlan.RequestSchemaVersion`, and that
   version has never been bumped.
2. The **result** - `Result` and `NodeOverrides`, the whole solved tree
   with its prices, offers and metadata - survives while
   `PersistedPlan.SchemaVersion` sits inside
   `[PersistedPlan.MinimumReadableSchemaVersion, CurrentSchemaVersion]`.
   Anything else discards the result and keeps the request.
3. Nothing is ever *partially* restored. A degraded result is discarded
   whole; the module never renders half a plan.

A schema bump therefore costs a user nothing on its own. It costs one
click - the tab comes back with the items and settings, and Generate Plan
re-solves at current prices - only on a bump that also moves the floor.
Before the split, a bump cost every saved plan on every user's disk, which
is why the version had been left stale at 2 for the whole of a ~275-line
graph change; before the range, a bump still cost every saved *result*.

**Why the layers can be read apart.** The document is one JSON object
with one set of property names - there is no second file and no second
on-disk shape. `PlanStoreHelpers.DeserializeRequestLayer` binds
`PersistedPlan` through a contract resolver that marks every member in
`ResultGraphMembers` as ignored, so Json.NET *skips those tokens without
binding them to a type at all*. That is the whole trick, and it is why
the split is not tolerant deserialization: the result graph is not read
leniently, it is not read.

`NodeOverrides` sits with the result rather than the request because it
is keyed by solver `NodeId`, which is meaningful only inside the tree
that produced it. `IgnoredItemIds` is keyed by item id and so belongs
with the request - the same line `PlanHistoryEntry` already draws
between its index row and its blob.

**Plan History was already split** along the same line, across two files
rather than within one: the index row in `plan_history.json` carries the
request identity, and the expensive result lives in a per-entry blob. A
`PersistedPlan` bump therefore already discarded blobs and kept rows, and
now discards a blob only when it moves the floor past that blob's version
- `PlanHistoryBlobStore` reads through the same `DeserializePersistedPlan`
and inherits the range with no code of its own.

**The index answers the same contract by a different mechanism.** It is a
*collection*, so its compatibility unit is the row, not a layer - there is
no cheaper half to fall back to, and a user who loses 200 saved plans is
no happier than one who loses one. Two things make a row survive:

- `PlanHistoryStore.Load` accepts any file stamped in
  `[PlanHistoryIndex.MinimumReadableSchemaVersion, CurrentSchemaVersion]`
  and `Save` restamps it. Exact-match rejection was what made a bump cost
  the whole history; a range costs nothing, and needs no migration code
  because there is nothing to migrate.
- That range is only safe because the row graph is **additive-only**.
  `PlanHistoryIndex.SchemaShapeHash` is what holds it to that: a rename,
  removal or retype anywhere reachable from `PlanHistoryIndex` moves the
  hash and cannot land without editing the line next to both version
  constants. An addition is free - Newtonsoft leaves an absent member at
  its default, so every existing row still loads.

`PlanHistoryIndex.MinimumReadableSchemaVersion` is therefore one of the
two constants in the module whose value *is* the amount of user data a
release destroys; `PersistedPlan.MinimumReadableSchemaVersion` is the
other. It is 1, and it is pinned twice - by
`PlanHistorySchemaMemberSetTests` and by the CI corpus step - so raising
it is a deliberate, reviewed act and never a side effect of a merge. A newer-than-current file is still discarded:
this build cannot know what a later one wrote, which is the same answer
the plan gives to the same question.

**What enforces it.** `tests/shared/plan_fixtures/` holds one serialized
plan per shipped schema version and one index per shipped index version,
captured from the real serializers, plus a hostile fixture whose entire
result graph has been renamed out from under the loader.
`tests/TaimisToolbench.Tests/Services/PlanCompatibilityFixtureTests.cs`
loads every one of them through the real `PlanStore`; the "Saved plans
from older builds still load" step in `.github/workflows/tests.yml`
checks the corpus is complete. Adding a fixture is one command, described
in that directory's own `README.md`.

**Where:** `Models/PersistedPlan.cs` (the two versions and which member
belongs to which layer), `Models/PersistedPlanLoad.cs` (what a read
returns), `Services/PlanStoreHelpers.cs` (both readers and the resolver),
`Services/PlanStore.cs` (the two severities and the "nothing restorable"
case), `Views/CraftingPlanView.cs` (`ApplyRestoredRequest`, the
request-only restore), `Models/PlanHistoryEntry.cs` and
`Services/PlanHistoryStore.cs` (the index's readable range and the
additive-only row graph behind it).

---

## 13. Popout windows

A player who has generated a plan wants the Shopping List or the Crafting
Steps on screen while they play, with the module window shut. The popout
windows serve that and nothing else.

### The table is the plan tab's table

`Views/PopoutTable.cs` calls `Views/Rendering/ShoppingListSectionRenderer.cs`
and `Views/Rendering/CraftStepsSectionRenderer.cs`. It does not reimplement
either. Both were already written against `ISectionRelayoutSink` (section 5),
which was the only thing tying them to `Views/CraftingPlanView.cs`, so a
popout implements that interface and gets the pinned column header, the
click-to-sort cells, the resize ellipsis, the row rules, the icon hovers and
the wiki right-click for free. Nothing had to be lifted out of the tab.

One window type hosts either section. The row shapes differ because two
renderers draw them, not because the surfaces differ.

### Where the tick column goes

The plan tab draws the same rows and must not grow tick boxes, so a tick
cannot live inside a row. The renderer's flow is inset by
`PopoutLayout.CheckColumnWidth` and the boxes are laid down the gutter beside
it, at offsets `PopoutLayout.CheckboxYOffsets` derives from the same per-row
heights `PlanContentHeightMath.SectionBodyHeight` sums. Both are children of
one panel inside the scroll region, so they scroll together.

A tick is stored against a row identity (`Services/PopoutRowKey.cs`), never
against a screen position, so a tick follows its row through a sort. The
Shopping List renderer sorts its own rows, so `PopoutTable` re-derives the
same order from the same pure `PlanTableSorter.Sort` call against the same
state rather than guessing at it.

### What a Refresh can and cannot change

It syncs the account and then subtracts. It does not solve.

It can: remove a row whose item the player has acquired since the last sync,
reduce a partly-acquired row's outstanding quantity and its money columns pro
rata, and clear every tick.

It cannot: change a craft-or-buy decision, reprice anything, reorder the
list, add a row, or bring a removed row back. A row with no item id of its
own - a currency row - is never touched.

Progress is measured as a delta against the holding at the previous sync, and
the baseline moves forward each time. An absolute count would resurrect a
finished shopping row the moment its materials were spent on the very craft
they were bought for. A delta cannot: a fall in the holding contributes
nothing.

The button is not held back by `Services/SnapshotRefreshPolicy`'s freshness
windows. Those exist for refreshes the module decides to run on the player's
behalf. This one is a deliberate press, like the Account Snapshot tab's
Refresh Now.

### Window chrome

A popout is dressed in the module window's own art and texture-space regions
(`Views/ModuleWindowArt.cs`, named in `Services/WindowSizing.cs`). Blish
scales a window background so the window region maps onto the control's own
bounds, so one pair of regions serves a 1378px module window and a small
popout alike. The chrome those regions imply - 46px left of the content box,
40px above it, 15px below - is the module window's and is already named, so
the popout retypes none of it.

`WindowBase2` draws a window's `Title` 78px in, which is the seat a
`TabbedWindow2`'s tab sidebar and emblem fill. A popout has neither, so the
word read as indented. The popout leaves Blish's `Title` unset and paints its
own in `PaintBeforeChildren`, on the same left rule the table under it starts
at.

### Why a stored opacity has to be re-imposed every frame

`WindowBase2.Show` sets `Opacity` to 0 and resumes a tween that animates it
to 1; `Hide` reflects the same tween back down to 0. The tween is private, so
an opacity written once before `Show` was overwritten 0.2 seconds later: the
setting persisted, the window stopped honouring it the first time it was
closed and reopened. `PopoutWindow.UpdateContainer` caps `Opacity` at the
stored value every frame, through `Services/PopoutOpacity.CapToStored`.

Capping and not assigning is the part that matters. `Hide`'s completion
callback only makes the window invisible if `Opacity` has reached 0, so a
window pinned to its stored value in both directions would never close.

### Lifetime

The windows are parented to the sprite screen, never to the module window,
which is what lets one outlive it. `Views/PopoutWindowHost.cs` owns them and
is held by `Module`, disposed in `Unload` beside the tickers and suggestion
panels that are screen-parented for the same reason. Ticks and sort survive
closing and reopening a popout within a session, and do not survive a
restart, because a sync clears them anyway.

---

## V. Views: relocated design narrative

The `Views/` tree carries a lot of hard-won reasoning: decompiled Blish HUD
1.3.0 behaviour, pixel simulations, bug post-mortems, and the arguments
behind choices that look arbitrary from the outside. That reasoning is
worth keeping, but a forty-line XML doc comment stops being read. This
section is where the derivations live. Each member's own doc comment keeps
the part a caller can violate - the invariant, the measured constant, the
Blish quirk you need to know to use it - and points here for the rest.

The subsections below are ordered by file, top-level `Views/` first, then
`Views/Rendering/`.

### V.1 `AboutTabContent`: rebuilt per visit, and the SemVer reflection

The About tab is the same shape as `LogTabContent`: one
`FlowPanel(CanScroll)`, a `Build(Container)` that populates it once, and no
relayout registry. Nothing on it is interactive beyond plain
selectable/copyable text, so there is no state worth keeping "sticky"
across a tab revisit and the rebuild-per-visit cost buys correctness for
free. `MainView` carries the cross-cutting note on that rebuild policy.

The manifest read cannot fail under normal operation: `ModuleParameters.Manifest`
is the exact object Blish HUD itself already parsed and validated in order
to load this module at all. The hand-parse of the packaged `manifest.json`
exists for the cases where it somehow does anyway - a null `Manifest`, an
unexpectedly blank `Name`, or any exception - and mirrors the
try/catch-with-graceful-fallback shape `Module.Initialize()` already uses
four times for seed files.

`Manifest.Version` and a dependency's `VersionRange` are typed
`SemVer.Version` / `SemVer.Range`, from the external "SemVer" NuGet package
Blish HUD embeds via Costura at runtime. This project has no compile-time
reference to it, so a direct property access does not compile. Reflection
(`ToString()` only) avoids adding a package reference for a two-field,
display-only read.

### V.2 `ApiAccessDialog`: why not a generalized `ModalDialog`

It shares `DialogWindow` and `Services/DialogLayoutMath` with `ModalDialog`
- a 1x1 pixel background stretched to the window's own size, `TopMost`, a
stable `Id`, `Show()`/`Hide()` semantics, and one content-driven sizing pass
- but is a separate class rather than a generalization of it. `ModalDialog`
is one short sentence under a fixed "Confirm" title, centred, with a
caller-named confirm button and an optional second seat; this dialog is a
multi-paragraph numbered checklist under a different title, left-aligned,
with a fixed Retry/Close pair. What was duplicated between them was the
geometry, and that is now in one place.

It deliberately skips `ModalDialog`'s settings-backed drag position
persistence. This is a rare error-path dialog, not a workflow a user
repeatedly opens and repositions, so it simply centers on every `Show()`
and needs no new `ModuleSettings` entries.

The failure it explains is real and was reported: at character select,
Blish has not yet resolved the game's Mumble identity, every account data
source call fails with an invalid or missing API key, and the Snapshot
tab's Refresh Now used to show only the unhelpful "Refresh Failed -
{time}".

### V.3 `FocusRelease`: how Blish's focus slot gets orphaned

`TextInputBase.Focused`'s setter assigns
`GameService.Input.Keyboard.FocusedControl = this` on every change, a
change to `false` included. Blish itself soft-unfocuses in two places: the
click-away handler (`Focused = _mouseOver && _enabled`) and
`DisposeControl`. The second runs after `Control.Dispose` has already
cleared `Parent`, so a box disposed while focused leaves that one global
slot holding an orphan whose `GetAncestors()` is empty - which
`KeyboardHandler`'s ancestor-visibility sweep can therefore never heal.

A slot naming one box while another still holds focus is what the user
feels. Escape is consumed clearing the slot instead of the box, and
re-clicking the live box cannot repair it, because the setter's
change-detection skips the assignment when `_focused` is already true. That
is why the release has to go through `UnsetFocus()` and why it retries.

### V.4 `ItemInputRowStrip`: why it is not under `Views/Rendering/`

Its controls are `AutocompleteTextBox`, `SuggestionPanel` and
`FocusRelease`, all of which are `Views` types. Putting the strip under
`Views/Rendering/` would make that folder reference `Views` and reverse the
one-way dependency section 5 above states.

### V.5 `LogTabContent`: the three-column row split, and the follow poll

`LogRow` splits each entry into four controls (panel plus three labels)
where the previous shape used three, and pays one more fit per row per
refit. Both are bounded by the ring cap (2000) and by what the filter
admits, every loop that writes a row's size is `SuspendLayout`-wrapped, and
on a resize the fitting half runs once per drag rather than once per drag
event.

The message column WRAPS rather than ellipsizing, so a row's height is a
function of its own text and the rows are not on a fixed pitch - the flow
panel positions each by its own height, and `LogRowLayout.RowHeight` is the
one place that height is derived. Three things follow. The wrap is capped
at `LogRowLayout.MaxMessageLines` so one pasted stack trace cannot own the
viewport, with the tail ellipsized into the last line. The per-row memo is
exact width equality rather than the narrowing-only asymmetry an ellipsized
column gets, because widening a wrapped column changes its answer too;
scrolling changes no width and so re-wraps nothing. And a resize settle can
now change the panel's total content height, which Blish's `Scrollbar`
answers by zeroing the scroll position a frame later (KNOWN-ISSUES #55) -
accepted, not defended against, on the same grounds `MainView` accepts it
below: this tab carries no scroll-restore machinery, an append already
moves the same height every time one arrives, and the snap costs one drag
rather than one frame.

One divergence is accepted rather than fixed: timestamps do not align
pixel-for-pixel between an `[INFO]` row and a `[DEBUG]` one, because the
level word and the stamp share the Time label. Fixing it costs a fifth
control per row on the module's heaviest render path. The Tag and Message
columns - the two a reader actually scans - do align.

`PollForUpdates` is the "plus a poll" half of the refresh design in
[`dev/proposals/d2-log-system.md`](../dev/proposals/d2-log-system.md)
section 4.3, layered on top of the `TabChanged`-driven `Refresh`. That
design is also what calls for the append-only incremental update rather
than a full-rebuild `Refresh()` on every version bump.

### V.6 `MainView`: Clear Cache, Refresh Now, status, and the result repack

Interposing a confirm dialog in front of Clear Cache opens a window in
which a refresh can start, which the old single-click version could not:
Refresh Now disables Clear Cache for its whole duration, but not the
reverse. Both buttons are therefore disabled for the dialog's lifetime, and
because `Build()` recreates them on every tab visit - which would re-enable
them mid-dialog - the confirm also bumps `_clearGeneration`.

`RefreshNowAsync` is a method rather than an inline lambda because the
`ApiAccessDialog`'s Retry button invokes it too. Both entry points are
Blish UI event handlers (a `Click`, or the dialog's own `Click`-driven
Retry callback), so both always start on the main thread - the same
argument `CraftingPlanView.TriggerGenerate` makes about its own
confirm-modal callback.

`ApplyStatusDisplay`'s parentheses around the elapsed time are the method's
own, and they are what keep the age from reading as part of the timestamp
beside it ("Updated - Aug 15, 2026 3:41 PM (2m ago)"). The `_statusLabel`
now lives in its own full-width `_statusPanel` row beneath the header
rather than in the header's shared, button-crowded run, which is why the
composed text is not truncated: a full-width row is far less likely to run
out of space, and truncating a status message is worse than letting a rare
long one reach the edge.

`RefitResultRows` keeps the scroll position across a repack that KEEPS the
column count - the grid panel's width moves, its height does not. A repack
that CHANGES the column count writes a new grid-panel height, and Blish's
`Scrollbar` zeroes the scroll position a frame after any content-height
change (measured: KNOWN-ISSUES #55, "The grid panel holds its unfiltered
height"), so the list snaps to top. That is not defended against: this tab
has no scroll-restore machinery - `CraftingPlanView.PreserveScrollAcross`
is the module's only one - and a column-count change re-flows every row
anyway, so there is no old position left to hold.

### V.7 `ModalBackdrop`: what it is for, and what it must not block

Before it existed, a confirm was only visually on top: with the Clear Cache
confirm open, a click on the Crafting Plan tab's "+" add-row button behind
it still registered.

It covers the module window rather than the screen because a capturing
control also stops the GAME from seeing the click. A screen-wide blocker
would mean a confirm left open swallows every click in Guild Wars 2, which
is not a trade a HUD overlay should make for a two-button confirm. The
finding is about the surface the dialog belongs to, so that is exactly what
is blocked.

The Z-order arithmetic behind the lazy construction: a window's effective
`ZIndex` is `5 + Screen.WINDOW_BASEZINDEX + its rank among windows ordered
by (TopMost, LastInteraction)`, so it is not a compile-time constant and a
`TopMost` dialog can land exactly one above a non-`TopMost` module window.
On the tie that arithmetic can produce with the blocked window, the
sibling-index tiebreak in `Container.TriggerMouseInput` decides - which is
why the backdrop is constructed on the first `Show()`, after every window
exists, so it is always the later child.

### V.8 `SettingsTabContent.EnsureCurrencyRowIcon`: two different readiness tests

A currency row is held to the currency LIST having resolved; a barter item
row is held to the ITEM ID it resolved. The asymmetry is deliberate,
because the two fetches answer different questions. Once the currency list
has resolved, every currency row gets an icon control, and a currency the
list carries with no icon URL of its own gets `IconControls`' empty-slot
placeholder - which is the state it really is in. The item fetch answers
per id, so an id absent from the reply is one nobody has an icon for yet,
not one the API says has none; holding a barter row to "the fetch happened"
would draw the placeholder over the first case.

### V.9 `CraftingPlanView.ApplyRestoredPlan` and `RollBackFailedPlanRender`

`ApplyRestoredPlan` mirrors `TriggerGenerate`'s success-path shape: it adopts
the restored `result` as the override loop's baseline, restores the user's
prior decision-pill overrides, reseeds the request inputs (rows, checkboxes,
price basis) that produced the plan, resets section expansion, rebuilds the
view model, and seeds the status board with the staleness banner text.
`RestoreOverrides` is not optional there - see V.17 for what a restored
session loses without it.

Two narrow try/catches guard the restore. `PlanStoreHelpers`' tolerance gate
is only structural, so a degraded `plan.json` can still throw inside the view
model build or inside `RenderPlan` - the builder copies the tree by
reference, so a null child is only dereferenced when `RenderPlan` walks it.
`RollBackFailedPlanRender` is shared with `Build()`'s guarded tail so a
poisoned view model can never be committed on either path.

Each thing the rollback restores is on the list for its own reason:

- The tree controller's override/ignore/expansion baseline
  (`ResetForNewPlan(null)`) and its per-render tree state, because the
  restored result was adopted as `_lastResult` before the render was
  attempted.
- `_lastDebugLog` / `_currentPlan` / `_planGeneratedAt`, because a committed
  view model that cannot render would re-throw out of `Build()`'s tail on
  every later tab visit.
- `_contentPanel`'s children, because a mid-build exception can leave a
  partially-built plan parented in a live panel; `ResetContentPanelToEmpty`
  sweeps it.
- The status board's seeded staleness banner and its painted label text -
  both skipped when `ClearRestoredSeed` reports a real Generate has raced
  in, so a superseding generation's status is never clobbered.

The catch is `catch (Exception)` on purpose: the rollback is the load-bearing
part, and narrowing it would trade a vanished plan for a crash on every later
tab visit.

### V.10 `ApplyWheelWrapCorrection`: why cancel-then-direct-write

Verified against the decompiled vendored Glide: `TweenerImpl.Tween` registers
a new tween in the by-target dictionary synchronously, before returning - so
by the time this handler runs, the wrong duration-0 tween is already
registered and `TargetCancel` finds it immediately. `Tween.Cancel` nulls the
`"ScrollDistance"` lerper slot synchronously, so even an `Update()` that runs
before removal skips the write: the wrong step never lands, rather than being
canceled one frame late.

That is why the shape is cancel-then-direct-write rather than a counter-tween
or a deferred correction, either of which would add a wrong frame this
mechanism does not have. (`Scrollbar` itself never calls `TargetCancel`;
rapid `ScrollAnimated` calls overwrite each other via `Tween`'s default
overwrite parameter, an internal-only path.)

Section 2 above covers the vendored `WheelDelta` defect this corrects.

### V.11 `PlaceTreeToolbarRow`: the collapsed row, and publishing the cluster

The strip's arithmetic collapses the toolbar row entirely when it is hidden,
which puts its Y exactly on the status row. A full-height panel left there
would sit over the top few pixels of the scrollable content area, so a hidden
row is given zero height as well as `Visible = false` and cannot intercept
anything even if Blish's hit-testing ever stopped honouring `Visible`.

Publishing where the button cluster starts matters because the two clusters
share one row and only this method knows its width. A left cluster laid out
without that number is a left cluster laid out over the buttons - which is
exactly what the chips did before `TreeChipStripLayout.Fit` existed.

### V.12 `PreserveScrollAcrossResize`: why the reset lands a frame late

Confirmed by decompiling the vendor assembly
(`packages/BlishHUD.1.3.0/lib/net472/Blish HUD.exe`,
`Blish_HUD.Controls.Scrollbar` and `Panel`):

`Scrollbar.RecalculateLayout` caches
`_scrollbarPercent = ContentRegion.Height / containerLowestContent` and zeroes
`ScrollDistance` (and, via `UpdateAssocContainer`, `VerticalScrollOffset`)
whenever that ratio differs from the previously cached value.
`RecalculateLayout` runs from two places:

1. Synchronously, nested inside `Panel`'s own `"Height"` `PropertyChanged`
   handler - `UpdatePanelScrollbarOnOwnPropertyChanged` sets
   `_panelScrollbar.Height`, itself a `Control.Height` write that
   invalidates/recalculates the scrollbar. But .NET's `PropertyChanged` event
   fires BEFORE `Control.Size`'s own
   `OnPropertyChanged("Height", invalidateLayout: true)` call to
   `Invalidate()`, so this nested call runs before `Panel`'s own
   `RecalculateLayout` has refreshed `ContentRegion` for the new size, reads
   the STALE (pre-resize) `ContentRegion.Height`, and sees no change.
2. Once every real engine frame, unconditionally, from `Scrollbar.DoUpdate`'s
   own `Invalidate()` call. By the time THAT runs, `ContentRegion.Height` has
   already been refreshed - the panel's own `RecalculateLayout` ran
   synchronously earlier in the same `Height`-setter chain - so it now sees a
   genuine change and resets.

Net effect: the reset lands on a later real frame, typically the next one,
not synchronously inside the tick's `Size` write. This is the same
delayed-reset window `ApplySavedScrollSynchronously`'s class doc describes
for rebuilds, and the reason `StartScrollVerify` exists there.

A per-tick verify window is deliberately not used: it would spawn (or
cancel-and-replace) a `FrameTicker` on every single drag frame, and the
per-tick synchronous write already keeps each tick visually correct without
one. The bounded window is armed once, at drag settle.

### V.13 `ReplayRelayout` and `ResizeSettleStep`: the drag-frame budget

`SuspendLayout`/`ResumeLayout` around the replay is about comparison cost.
For a long shopping list or a deep tree, replaying dozens of per-row closures
in a single tick without it would trigger that many redundant full sibling
reflows in the same frame - an `O(rows^2)` risk. The
coalesced reflow is a no-op for vertical position anyway, because these
writes only ever touch `Width`/`X` - row heights stay fixed, and
`SingleTopToBottom` flow positions children from cumulative `Height`.

The perf caveat on `ReplayRelayout` is real and stated inline: this shape
replaced a ONE-TIME dispose+rebuild 150ms after the drag settled with a full
replay of `_relayoutActions` on EVERY real drag frame. That is a genuine
change in perf character, not just a different trigger, and the mitigation
above is reasoned rather than measured - no live drag-resize check on a
large, fully-expanded plan (deep tree plus long shopping list) has been
performed against a running Blish instance.

`ResizeSettleStep` defers only the MEASURE half, and only because
`MeasureString` is comparatively expensive to run on every tick across a long
list or deep tree. The visible cost of deferring it is small: truncated text
stays unchanged mid-drag and is corrected once the drag settles.

### V.14 `TriggerGenerate`: why the resolution await lives in the wrapper

The await could have gone inside `GenerateFromResolvedRows`, and that is the
version this replaced. It cannot: `IItemSearchProvider` may complete
asynchronously, Blish's host installs no `SynchronizationContext` (section 1
above), and everything after such an await would therefore run on a
ThreadPool thread - while the generate body touches controls from its first
line. Keeping the await in a thin wrapper puts exactly one marshal hop
between the resolution and a body that has no async seams of its own.

### V.15 `SetGenerateInputsEnabled`: the run it was added for

The Generate button used to be the only control disabled for the length of a
run, which left "Use Own Materials" clickable while a plan was still
generating - and its confirm callback starts another generation. Two runs
then shared one `ItemMetadataService`, which is a data race, and
`_generateSequence` does not help: it makes the last result win, it does not
stop the redundant work. `ItemMetadataService` is now internally locked
(`_cacheLock`), so this is no longer a crash guard; it is the single-flight
rule that stops the redundant run from starting at all.

### V.16 `SpinnerTick`, `CreateSectionHeader`, `CreateRequiredRecipesSection`

`PlanStripTickAction.RenderFinalAndStop` is what makes "the board reports
finished, so render final status and stop" true without any separate
completion-callback write into this control ever being needed. The tick reads
the board; nothing writes back into the tick.

`CreateSectionHeader`'s `suppressToggle`/`suppressPress` pair has exactly one
remaining user: Required Recipes' "Hide Unlocked" checkbox, the only
interactive control left in any section header now that the Recipe Tree's
five buttons moved to the non-scrolling strip (see `TreeToolbarCommands`).

Toggling that checkbox re-renders through `RenderPlan(_currentPlan)` - the
same full rebuild path a pill click's local re-solve and a fresh Generate
both already use - rather than inventing a second, parallel relayout
mechanism for one section.

### V.17 `DisciplinesSectionRenderer`: one column X for the whole section

A per-row X, varying with each discipline name's width, could never line up
with a single header position - which is why the character-availability
column had none. Computing one column X for the whole section fixes that
without touching `rowHeight`, `PlanContentHeightMath` or `PlanRelayoutMath`:
`NameMaxWidthBeforeColumn`'s existing 20px floor still clamps the ellipsis
width on narrow panels exactly as it did before.

The "Characters" header is added only when at least one row actually has
availability text under it. In practice a section is never both null and
non-null (see `BuildCharacterAvailabilityText`'s own doc comment), but the
check walks all rows rather than assuming that.

### V.18 `FeedbackButton`: what `StandardButton` measurably does not do

Measured from the vendored Blish HUD 1.3.0 binary with `ilspycmd`:

- **Hover works and is left alone.** `OnMouseEntered`/`OnMouseLeft` tween
  `AnimationState` 0 to 8 over 0.25s, stepping through the
  `common/button-states` atlas. The override paints from the same atlas via
  the same public `AnimationState`.
- **Press does nothing.** There is no `OnLeftMouseButtonPressed` override and
  no pressed frame in the atlas walk, so the button looks identical held down
  as hovered.
- **Sound is silently dead.** `OnClick` calls
  `PlaySoundEffectByName("audio\\button-click")`, but `ContentService`'s audio
  reader is already rooted at `ref.dat`'s `audio` folder, so the lookup
  becomes `audio/audio/button-click.wav`, fails the `FileExists` check, and
  returns silently. `Checkbox` and `GlowButton`, which pass the unprefixed
  `"button-click"`, are audible.

The press and sound gap is supplied by `PressFeedback`. If a later Blish
release fixes the double-prefixed path, this button will play the sound twice
on a completed click and the `PlayClick` call in `PressFeedback.Wire` is what
to drop.

**Why the paint is overridden rather than the control replaced.** The four
limits all live in `StandardButton.Paint` and `RecalculateLayout`, both
virtual. Everything ABOVE them - the hover tween, the click event and its
`Enabled` gate, the tooltip plumbing every one of this module's buttons
relies on, focus, opacity, and the whole `Container`/`Control` lifecycle - is
inherited free, and is the part that would have to be rebuilt, and kept
rebuilt, by deriving from `Control` instead. The button art is Blish's own
(`common/button-states` and `button-border`, both reachable through the
public `GameService.Content.GetTexture`), so painting it costs two texture
handles and no fidelity.

The four limits in full:

1. **No `Font`.** `StandardButton` draws in `DefaultFont14` and exposes no way
   to change it, so a button could not sit on this module's type ramp and
   could not carry a glyph from the shipped glyph font (`ref/glyphs.fnt`) at
   all.
2. **Text colour is forced.** `Paint` assigns `_textColor` on EVERY frame, so
   a colour written from outside is overwritten before it is ever drawn.
3. **Icon is blitted untinted**, onto button art whose face samples about
   (200,193,175). Blish's own white affordance textures - 733269/733270, the
   matched X pair - are therefore invisible on a button, which is the measured
   reason Plan History reached for a `Checkbox` instead of a button wearing an
   icon.
4. **An icon-only button's icon is off centre by construction.** With no text,
   `StandardButton` seats it at `Width / 2 + 8 - iconWidth - 4` - the `+8` is a
   text gap being paid for when there is no text - so it sits 4px right of
   centre at every width.

### V.19 `GlyphFont.Merged`: why one font rather than two labels

The sort indicator is part of the header's own `Label.Text`, which is what
lets every right-aligned header keep tracking its column: the relayout
closures right-align off a width that already includes the indicator. A
separate glyph `Label` beside the title would have meant re-deriving nine call
sites' worth of column arithmetic. One merged font means every existing
`MeasureString` keeps measuring the whole string correctly and no call site
learns anything new - which is worth the handful of extra texture switches per
frame the split texture pages cost.

### V.20 `HeaderBands`: why a band, and why a factory

Four of the plan tab's five original headers already drew a band, so unifying
the other way would have rewritten the majority to match the minority. Every
table row in this module also carries a 2px divider and, in most tables, an
icon, so an unbanded header in a lighter grey reads as a faint first data row
rather than as a header.

The factory shape is a response to a measured failure. `HeaderBands`'
predecessor exposed the band colour as a constant and let eight call sites
each build their own `Panel` from it; seven of the eight did, and only one
went through a shared renderer - the same opt-in-helper failure the module
already paid for on icon sizes.

### V.21 `HoverChainResync`: the clicks it does and does not fix

Every click in the plan view that rebuilds what it was clicked on hits the
frozen hover chain: a decision pill re-solves and rebuilds its own row, a sort
header re-renders the table it labels, a caret rebuilds the subtree under it.
The replacement control lands under a stationary cursor with
`MouseOver == false` and no `MouseEntered` fired, so the pill the user is
pointing at reads as un-hovered until they jiggle the mouse.

What this type is NOT is a way to answer "is a pill under the cursor". That
question used to be asked of `Control.MouseOver` too, and the resync was what
kept the answer honest - which held only while the resync's own hit test was
honest. It is not, on a full rebuild: a freshly created row is added to its
`FlowPanel` with no `Location` of its own, and Blish defers
`FlowPanel.RecalculateLayout` to the next draw, so at the instant the click
handler calls the resync every new row still sits at its container's origin.
The resync then sets `MouseOver` on whichever row won the sibling tiebreak
there, the pill genuinely under the cursor never gets it, and the row's
expand/collapse handler - which defers to that flag - answered the NEXT click
by expanding the node. `Services/TreeRowPillHitTest` removes the dependency
rather than trying to make the flag correct: the guard reads the pills'
rectangles against `RelativeMousePosition`, which is derived from live
`AbsoluteBounds` at click time and cannot be stale in that window. The resync
stays for what it does fix - the visible hover WASH on a rebuilt control.

A LOST click is a different, also-measured mechanism, and this section is the
one place it is stated. `MouseHandler` buffers exactly ONE pending mouse event
(`_mouseEvent`, overwritten by the hook thread and consumed once per
`Update`), and `Control.OnLeftMouseButtonReleased` only raises `Click` when
that same control INSTANCE was primed by its own press. A frame long enough to
contain both halves of the next click drops the press, so the release finds
nothing primed. The answer to that is to make the rebuild frame short, which
is `TreeSectionController.TryRefreshInPlace`'s job - not this type's.

### V.22 The `Views/Rendering` seams

Shared row-construction helpers with several callers (`TextRowRenderer`,
`ColumnHeaderRowRenderer`, `RowRelayoutHelpers`, `IconNameRowHelpers`) take
`ISectionRelayoutSink` as a method parameter rather than as a
constructor-injected field, because none of them is itself a section renderer
and none of them has a per-render lifetime to hang a field on.

`ITreePlanHost` is one named interface rather than a list of constructor
delegates because the callbacks are semantically one collaborator, and because
a named member is the only thing that makes a particular swap unexpressible:
two of them used to share the type `Action<PlanViewModel>` with opposite
meanings (render vs. assign-field), so transposing them compiled. It also
gives a new tree feature one place to grow instead of four. `CraftingPlanView`
implements it explicitly, the same way it implements `ISectionRelayoutSink`,
so nothing there widens that class's public surface.

### V.23 The icon contract: `IconControls`, `ItemIconTooltip`, `IconNameRowHelpers`

All three halves of `CreateItemIcon`'s no-defaults rule exist because all
three were opt-in before and all three silently drifted: eleven call sites
each chose their own pixel size, a call site with no rarity to hand looked
identical to one that had looked and found none, and an icon with no hover
looked identical to one that had decided against showing one.

`ItemIconTooltip` is the same treatment `ItemIconFrame` gives the frame colour
and `ItemIconTier` gives the size. A trailing default could be omitted, and
omission looked exactly like a deliberate decision not to show one - which is
how the Plan History tab shipped with item icons that answered nothing at all.
A factory name is what a diff shows.

There is deliberately no eager or plain-text twin of
`ApplyRichDeferredToIconTree`, because either would be a way to give an icon a
hover without saying so at the call site.

`IconNameRowHelpers.CreateIconAndEllipsizedName` threads
`rightEdge`/`qtyWidth`/`nameGap` into `PlanRelayoutMath.NameMaxWidthBeforeColumn`
exactly as each pre-extraction caller computed them inline; the helper changed
where that arithmetic is called from, not the arithmetic.

### V.24 `InlineSpinner`: the decompiled `LoadingSpinner`

Measured from the vendored Blish HUD 1.3.0 binary
(`packages/BlishHUD.1.3.0/lib/net472/"Blish HUD.exe"`, decompiled with
`ilspycmd`): `Blish_HUD.Controls.LoadingSpinner` is a plain public `Control`
with a parameterless constructor whose only body is `Size = 64x64`, and whose
`Paint` hands its own bounds straight to
`LoadingSpinnerUtil.DrawLoadingSpinner`. That helper draws one 64x64 source
frame of the `spinner-atlas` texture (4096x64 in `ref.dat`, i.e. 64 frames)
into whatever destination bounds it is given, so the control scales to any
size. The frame index is
`GameService.Overlay.CurrentGameTime.TotalGameTime.TotalSeconds * 21.333 % 64`
- global game time, not per-control state, so the animation costs no ticker
and starts mid-cycle rather than at frame 0.

### V.25 `ItemStatWarmer`: the gap it closes

Warming is what closes the gap between "this row knows its name, icon and
rarity" and "this row shows the same tooltip the game does". Without it a tab
handed only the pure-read cache accessor shows the identity-only fallback for
every item no earlier plan happened to touch.

### V.26 `LabelHelpers`: the row-divider scissor derivation, and `WithDescenderClearance`

The 1px-to-2px change came first. Blish applies its UI scale (for example the
"Normal" GW2 UI size's 0.897) as a real GPU scale matrix, not an
integer-pixel-snapped one, so a 1px-tall quad rasterizes to 0.897 physical
pixels - guaranteed physical coverage `floor(0.897) = 0`, i.e. it can
disappear entirely depending on scroll-offset sub-pixel alignment
(KNOWN-ISSUES #23). At 2px, `floor(2 * 0.897) = 1` guarantees at least one
covered physical scanline for the divider's OWN quad-vs-scissor math analyzed
in isolation.

That isolated argument turned out to be necessary but not sufficient. The row
panel is itself a `Container`, and every `Container.Paint()` performs a SECOND,
independent floor/ceil round trip: it unscales the physical scissor it was just
given back to logical space (`ScaleBy(1/UIScaleMultiplier)`) before
re-intersecting and re-scaling it for its own children (`Container.cs:377-381`,
`Control.cs:1176-1177` in the decompiled Blish HUD binary). That round trip can
shrink the clip rectangle propagated to the divider by exactly 1 logical pixel,
but provably only at the row's BOTTOM edge - the reconstructed START never
exceeds the true start, since `floor(floor(Y*s)/s) <= Y` for any positive scale
`s`.

Whether that 1px shrink actually deletes the divider depends on `rowHeight`.
Simulation across every `rowHeight` then in the file and all four GW2 UI Size
scale factors (0.81 / 0.897 / 1.0 / 1.103) showed the pre-tier-2 44px rows
(`CraftStepRowHeight` of the day) and 32px rows (`DisciplineRowHeight` of the
day) vanish completely - 0 physical scanlines - at about 10.2% of scroll phases
at the default scale, while the pre-tier-2 36px rows were immune at every
tested scale.

The fix is `bottomClearance`: an extra logical pixel of gap between the divider
and `rowHeight`, so `Location.Y = rowHeight - 2 - bottomClearance`. That moves
the divider's own interval entirely inside the worst-case-shrunk clip window,
which simulation confirms is immune (0/5000 vanishes) for every
(`rowHeight`, scale) pair tested - proven, not merely observed clean at one
scale.

The tier-2 re-run, after the tier-2 icon change grew the plan tab's icon-led
rows to 45px (Used Materials / Shopping / Required Recipes: flush tier-2 frame
plus divider) and 52px (Crafting Steps): the simulation, re-derived from the
decompiled `ScaleBy` floor/ceil semantics and validated by reproducing the
numbers above, shows BOTH new heights are in the vulnerable class at clearance
0 (45px: 18.0% of phases at 0.81, 7.0% at 0.897; 52px: 10.3% at 0.897) and
immune at clearance 1 at all four scales. One padded height later replaced
those two. Every icon-led plan row is now
`PlanContentHeightMath.IconLedRowHeight`, which is 50. The icon frame sits at
y=2, so `2 + 42 + 3 + 2 + 1 = 50` puts the divider at 47..48. A reader sees 3px
of clear space over the frame and 3px under it. The row still absorbs the
clearance pixel in its own derivation, so the sweep covers 50 at clearance 1.
The proof is executable - `RowDividerScissorSimulationTests` sweeps
every shipped (`rowHeight`, `clearance`) pair at all four scales and fails on
any vanish - so a future height change re-runs it by construction.

That proof is a transcription of the decompiled 1.3.0 paint pipeline rather
than an invention, and each step names its source: `RectangleExtension.ScaleBy`
floors X/Y and ceils W/H after a float32 multiply; `Control.Draw` sets the
physical scissor to `Intersect(logicalScissor, bounds).ScaleBy(uiScale)`;
`Container.Paint` unscales that physical scissor back to logical space with
`ScaleBy(1/uiScale)` before re-intersecting and re-scaling it for each child
(the second, independent round trip named above); and the GPU rasterizes a
physical scanline of the divider quad only when the scanline's center lies
inside the quad's scaled interval. A divider "vanishes" at a scroll phase when
no rasterized scanline survives the scissor.

The model earns its authority over the shipped geometry by first reproducing
the measured past: the vulnerable 44px and 32px rows at the 0.897 "Normal"
scale and the then-30px section header at the 0.81 "Small" scale (the scale of
the live pixel scans), at the same ~10.2% vanish rates published
above, and the immune 36px rows. A model that cannot reproduce the
live-verified past has no authority over the present.

`WithDescenderClearance` pins `VerticalAlignment` to `Top` for a related
reason. `Blish_HUD.Controls.Label.VerticalAlignment` is a public settable
property whose default this module does not control; if it were `Middle`,
growing a box by 2 would push its glyphs down by 1 while an unswept sibling on
the same row stayed put, and a ragged baseline inside one sentence ("Craft 12x
" plus an item name) is worse than the clip the sweep fixes.

### V.26.1 `ClipCutoff`: the viewport's hard top edge

The same round trip V.26 analyses at a row divider's BOTTOM edge is what
lets scrolled content paint over the plan tab's pinned top strip, and the
answer there is different because the edge is different. `Container.Paint`
is `sealed`: it reads `GraphicsDevice.ScissorRectangle` back, unscales it
with `ScaleBy(1f / uiScale)`, and hands the result to `PaintChildren`,
which re-intersects it with the container's own content region. That
re-intersection re-clamps the top edge only when the container's own top is
BELOW the inherited clip - false for every ancestor of a row scrolled out
of view - so the `floor(floor(y*s)/s) <= y` loss accumulates once per
nested container and grows with recipe-tree depth. Measured at UI Size
Small: 2, 3, 4, 5, 7, 8, 9, 10 logical pixels at depths 1 through 8, and
still climbing at 64.

Three things follow, and the third is the fix.

- **A gap cannot be the fix.** Any inset sized against "the deepest
  realistic tree" is a guess about content, and the module does not bound
  recipe depth. `ClipTopSlipSimulationTests` keeps that measurement, and
  keeps it labelled as the defect.
- **Positioning the viewport lower does not prevent it either.** The
  leaked pixels are drawn relative to the viewport's top edge, so they move
  down with it; a gap only changes what they land on.
- **One line, re-asserted at every container, does.** `Control.Draw` is
  `public virtual`, and it is the one seam the vendor leaves open.
  `Views/Rendering/ClipCutoff.cs` publishes an absolute logical y for the
  duration of the viewport's own paint (`ClipAuthorityFlowPanel`), and
  `ClippedPanel`/`ClippedFlowPanel` clamp the clip they were handed back to
  it before the vendor code uses it. A container that re-asserts the line
  hands its children an edge that has drifted at most ONE round trip, so
  the reach stops accumulating: it is `cutoff - SlipBudget`, at depth 1 and
  at depth 64 alike. `Services/ClipCutoffMath.cs` owns the arithmetic and
  the budget - 2 logical pixels, the worst single round trip across all
  four GW2 UI Sizes - and `ClipCutoffMathTests` proves the bound without
  mentioning depth.

The line is set one budget BELOW the viewport's top edge, so what a
descendant can reach is the edge itself and not a pixel above it. It spends
the viewport's top pixels rather than pixels of the strip above it.

The budget is the LIVE scale's worst round trip, not the four-size worst
case. `ClipCutoffMath.SlipBudgetFor` measures it over a phase sweep and
caches one slot; it is 0 at UI Size Large, where both floors are exact, 1
at Larger and 2 at Small and Normal. The constant was reserving 2px at
every scale, and the strip between the protected edge and the cutoff is
nobody's to paint: at Large that cut the first 2px off every scrolled row
in every viewport, and off every row under a pinned sticky band, for a
round trip that loses nothing. The difference between the reserve and the
loss a particular edge's phase actually suffers is also why a pinned band's
seam FLICKERED rather than sitting still; `StickyHeaderHost` now paints
that strip in the band's own fill while the band is whole, so the band
reads as one piece at every scale.

The sticky clip needs a line of its own. `StickyHeaderHost`'s clip is a
SIBLING of the viewport, not a child of it, so the viewport's line is not
in force while the clip paints and nothing was re-asserting anything for
the band inside it. While a band is whole its top edge and the clip's are
the same, so nothing shows; while the end of a table is pushing the band
out, the band sits above its clip and the two containers between the
clip's edge and a header label's ink let that ink paint 3 logical pixels
above the clip at UI Size Small. That is what an in-game screenshot
showed on 2026-09-06: the Total Cost table's column words drawn over the
plan tab's separator rule and the "Plan updated" line above it. The clip
is now `WheelTransparentClipAuthorityPanel`, which publishes
`CutoffTopFor` its own top edge for its own subtree exactly as the
viewport does for its. `ClipCutoffMathTests` proves the bound for the
two-container chain that ships.

Where nothing paints the strip and nothing can - the Snapshot tab's
viewport top, which has no rule under it - the reserve is still spent, and
that is the trade the cutoff is: an unpainted pixel at the top of a
scrolled row, against a row overdrawing the coin panel above it. It is 0px
at UI Size Large after this change and at most 2px below it.

Coverage is per-container by construction: a plain `Panel` left in the
chain re-opens the accumulation below itself, which is why the swap is a
sweep rather than a single site. The sweep is now complete - the recipe
tree's own per-depth containers in `Views/Rendering/TreeSectionController.cs`
(the section's root divider, each row panel, the dimming icon scrim, the
recursive child flow, and the two panels of a decision pill) were the last
sites, and they are the ones that made the reach depth-dependent: a tree row
at depth d sat under about 2d plain containers, one row panel and one child
flow per level.

`TopStripZIndex` was described here and at its own declaration as defence in
depth against a plain `Panel` added inside the viewport by a later change.
It never was. `Container.PaintChildren` sorts `OrderBy(ZIndex)` - ASCENDING -
while `Container.TriggerMouseInput` sorts descending, and the strip's value
is 1 against the content panel's vendor default 5, so the strip paints
FIRST and covers nothing. The constant now says so. The one control that
does overlap the viewport, the separator rule, carries
`CraftingPlanView.SeparatorZIndex` above the content panel and therefore
paints last, which is what keeps it unnotched at the scales where a
scrolled row can reach into its 2px.

### V.26.2 Wheel-transparent containers

`WheelTransparentClippedPanel` exists because a container drawn ON TOP of a
scrolling panel swallows the wheel from it. Measured against Blish HUD's own
source, not inferred:

- `MouseHandler.HandleMouseEvent` runs a fresh
  `SpriteScreen.TriggerMouseInput(eventType, state)` walk for every hooked
  mouse event, wheel and click alike.
- `Container.TriggerMouseInput` raises the container's OWN mouse event
  first - which is how a `Panel`'s `Scrollbar`, subscribed to
  `_associatedContainer.MouseWheelScrolled`, ever sees a wheel over a deep
  child - then walks children by ZIndex descending, breaking on the first
  non-`Filter` child that answers.
- `Container.CapturesInput()` returns `Mouse | MouseWheel` for EVERY
  container, unconditionally.

So the ZIndex that lets a pinned sticky header be clicked to sort is the
same ZIndex that stops the wheel reaching the scroll panel behind it, and
the two asks were in direct contradiction as long as the answer was a
ZIndex. `Control.TriggerMouseInput` does discriminate by event type,
though: it returns null for `MouseWheelScrolled` unless the MouseWheel flag
is set. A container that drops that flag therefore returns null for the
wheel and non-null for the click, and the parent's loop steps past it to
the scroll panel below - so the band sorts AND the wheel scrolls.

Every container between the clip and the cursor has to answer the same way
or the walk breaks inside it, which is why the sticky clip, the header band
(`HeaderBands.Band`) and `SortableHeaderCells`' hover washes are all this
type. Labels need no change: `Control.CapturesInput()` is `Mouse` alone.
The plan tab's separator rule is this type for the same reason - it now
paints above the content panel, so it also wins the hit test over the first
2px of the first scrolled row.

This is not covered by any test. The repo invariants bar a test from
referencing UI code, and input dispatch is not expressible without it: the
mechanism above is read off the vendor's source, and whether it behaves as
read is an in-game observation.

### V.27 `PlanHeaderRenderer`: the three things that used to compete

The header block was CENTRED while everything under it was left-aligned, so
the plan had no single left edge. It carried a right-aligned "Generated: ..."
panel duplicating - to the minute - the timestamp the fixed status strip 70px
above already shows, so an opened plan said the same thing twice. And its
title shared `DefaultFont18` with every collapsible section header, leaving
the page with no typographic top level at all.

So: the in-scroll timestamp is gone (the strip keeps it, and it never scrolls
away), the title is left-aligned at `DefaultFont32`, and `CreateSectionHeader`
drops to `DefaultFont16`. The "Crafting Plan for " prefix went with the
timestamp - the tab is already titled "Crafting Plan" and the strip already
says "Plan generated", so the prefix cost half the title's width to repeat
what two other elements say.

### V.28 `PressFeedback`: why `Opacity` and not the site's own colour

A helper that wrote to the same properties the sites' own hover vocabularies
use - `BackgroundColor` for a decision pill, `TextColor` for a sortable
header, a different translucent wash for a tree row and for a section header -
would have to capture and restore a resting value that the site's own
`MouseLeft` handler is also writing, making correctness depend on which
handler was subscribed first. `Opacity` is touched by nothing else on any of
these controls.

`AbsoluteOpacity()` walking the parent chain is also what makes the dim
legible on a target whose own background is transparent: dimming the panel
dims its label and icon children with it.

### V.29 `RichTooltipSurface`: the measured canvas

The 0.98 multiplier is Blish's own on the "tooltip" texture (decompiled 1.3.0)
and independently the live client's: fitting
`composite = s*artAlpha*artRGB + (1 - s*artAlpha)*scene` to two clean interior
patches of `live2/k-2` puts `s` at 0.98 and 1.00, residual std about 1
quantisation level (fidelity-audit, 8.4 closure). Those same patches correlate
with the texture at `r = 0.983` at the predicted alignment, which is what
settles that the background is textured rather than flat.

The audit's F5 note suggested 0.82. That number belonged to the flat FILL,
whose constant carries its own coverage; the texture's alpha channel (mean
about 0.80) already supplies the transparency, so scaling it again would land
the box near 0.66 coverage and fail audit H6's no-legible-bleed requirement.
Measurement wins.

### V.30 `ShoppingBadgeColors`: why only two hues

Every badge used to render in `PillKind.Locked`'s recessed grey, so the column
said WHICH source only to a reader who stopped to read four capital letters on
every row. Two hues fix that without spending the accent budget. Reusing an arm
of `PillColors` would have diluted a vocabulary the tree depends on - green
means selected, blue owned, amber ignore-active, and none of those means "go to
a vendor".

### V.31 `SummarySectionRenderer.CreateFormulaBand`

`PlanViewModelBuilder` groups `CostFormulaTile` and `ProfitFormulaTile`
separately and `Render` re-groups by that same `RowType`, so two bands render
as two stacked tile rows rather than one: the cost band at
`SummarySectionLayoutMath.CostBandHeight`, the profit band at
`PlanContentHeightMath.CostTileRowHeight`.

A lone tile centred on a full-width band reads as a stray caption floating in
whitespace, and it is the only tile in the section that aligns with nothing
else in it - the currency table's icon column, the footnote and every section
title all start at the left. So a collapsed one-tile band is left-aligned at
the section's own content gutter, keeps the same band height as the three-tile
case, and simply starts where everything else in the section starts.

Every tile's amount renders at the SAME font; the result tile is picked out by
`highlightResult` instead, with a tinted, semi-transparent box around its
caption, note and amount. A promoted `DefaultFont32` was tried and broke the
band's visual balance. The box is a real `Panel` and the result tile's controls
are its CHILDREN, so the fill is painted behind them by the container's own
paint order - no z-index games - and a resize moves one control instead of
re-centring three runs. Amounts hang one
`PlanContentHeightMath.CostTileLabelToValueGap` under the measured caption
line, in every band, so the distance between a caption and the number it names
is that constant rather than whatever a fixed row height happened to leave
over.

`currencyNoteText`, when non-null, draws a small disclosure line under the
RESULT tile's AMOUNT: the plan has costs the coin figure does not include. It
hangs below the run rather than sitting between caption and run, so it cannot
push the other tiles' amounts down - they share one `amountY`.

The `-` and `=` operators between tiles are small dim `Label`s centered on each
boundary, with no tooltip so they never steal hover; without them, same-shaped
tiles have no visible relationship. They are never drawn for a collapsed
one-tile band. Only the FINAL boundary's symbol is conditional: there is only
ever one non-final boundary (`tileCount == 3`), and the left two tiles' own
subtraction is never in question - only whether the final result tile's
displayed value is the true right-hand side, which it is not in the profit
band's loss case.

### V.32 `TooltipFacility`: one surface, and where content lives

Measured, KNOWN-ISSUES #41: `Control.Dispose` does not dispose the control's
`Tooltip`, and the `Tooltip` is not the control's child, so nothing in Blish
ever tears one down. A per-control instance on controls this module rebuilds on
every render would therefore leak one container plus its child tree per row per
render - hence exactly one rich surface for the whole module, repointed on
hover.

Content is held in a `ConditionalWeakTable<TKey,TValue>` keyed by the control,
so the facility never holds a control alive and a disposed row's content is
collected with it.

`ApplyPlain` routes through `TooltipTextFormat`, the wrap seam this facility
inherits from the tier-1 tooltip work. `ApplyRich` exists for anything a string
tooltip could only spell out as "1g 23s 45c", and for every item hover.

### V.32a The two-box rule

An item tooltip is not a place to put plan details. The first box carries what
closely mirrors the game's own content for that item or currency, and nothing
else. Anything this module adds - a unit price, a wallet holding, an
acquisition hint, a caption, the wiki right-click affordance - goes in a second
box drawn during the same hover, directly under the first.

The game does this itself: hovering an item that fits several slots draws a
"Currently Equipped" comparison box under or beside the item's own. Both boxes
here share one background, border, padding and font, because they are drawn by
one surface.

Box two never measures itself. It is laid out at the width box one arrived at,
so the pair reads as one column whatever the module has to say. `TooltipContent`
carries the second box as `Extra`; `ItemRowTooltipComposer.BuildRowContent` is
the one place that attaches it, so no surface can put its own lines inside the
item's box by accident. `TooltipLayoutMath.Stack` holds the geometry - both
frames' chrome and the measured gap between them - and is tested without a live
control.

Content that has nothing in box one keeps its lines in box one: a second box
under an empty one is a blank frame. A tooltip the module wrote in full - a
decision pill, a value breakdown - is one box, because none of it is the game's.

Every line in box two reads as a sentence. No shouting, and no dashes doing a
comma's job.

### V.33 `TreeSectionController`: heights, in-place refresh, and the pill column

`RefreshTreeContainerHeights` replaced `InvalidateUpToContentPanel`, which only
repositioned siblings and relied on Blish's `AutoSize` convergence - one nested
level per real frame - to eventually grow or shrink ancestor containers to
match. That convergence window was the direct cause of KNOWN-ISSUES #12/#14's
multi-frame windows.

`TryRefreshInPlace` exists because of the mechanism stated in V.21 above:
`MouseHandler` holds exactly one pending mouse event and
`Control.OnLeftMouseButtonReleased` raises `Click` only when that same control
INSTANCE was primed by its own press, so a frame long enough to contain both
halves of the next click loses the press. A decision pill's click used to
re-solve and rebuild every control in the plan, which is what turned into the
reported "rapid IGNORE toggling drops clicks". Ignoring a LEAF material - the
common case, and the one reported in game - passes the gate.

The pill column's budget is exceeded because
`DecisionPillPlanner.AppendOwnershipPills` adds "USING N OWNED" when
applicable on top of a node's 1-3 source pills. The row cannot grow to absorb
them: `TreeRowHeight` is a fixed per-row height shared by every
layout/scroll-height calculation in that file, so there is no wrap and no
second line. Before `ComputePillFit`, trailing pills were simply dropped with
nothing on the row to say they had existed - which is what the "+N" pill now
says.

The ignore button is not one of those pills. It has a column of its own,
which closes every tree row after Cost: the button takes the row's right edge
and the data columns end short of it, the shape `Services/RankerRowLayout`
already uses for the Ranker's row actions.
`PlanRelayoutMath.ComputeTreeColumnEdges` derives `ActionButtonX` from the
panel edge alone, so the button is at the same x on every row and moves only
when the window does. That matters because clicking it re-solves the node: an
ignored node comes back owned, with none of the source markers it was drawn
beside. Placed anywhere among those markers, the button moved out from under
the cursor that had just clicked it, and the next click reached the row and
expanded the node instead. A column derived from the panel edge cannot move
for that reason, or for a re-solve that changes either data column's width.

The column also took 25px off the tree's header band, until 2026-09-06.
`ColumnHeaderRowRenderer` sized the band from the right column's edge plus
`TableRightMargin`, which was the panel edge for as long as Cost was the
last column. The action column moved that edge left and the band stopped
with it, so the tree's band ran short of the panel by exactly the column
and its gap, and the game world showed through above the ignore buttons.
The band is the table's own background and every plan table justifies to
the width it is given, so it is now the panel width for every caller. No
other table was affected: the tree is the only one that passed a derived
right edge.

The new column costs `TreeActionColumnWidth` 21 + `TreeActionColumnGap` 4 of
every row, and it is paid for by `PlanRelayoutMath.TreePillColumnWidth`
dropping 256 to 231, so no window width lost a pixel to it and
`WindowSizing.MinWindowWidth` is unchanged. That 25px was reserve this column
was not using. 256 was derived to hold `CRAFT / TP / VENDOR / IGNORE` at a
10px margin when IGNORE was a 53px word; the button that replaced it draws
21px, so the floor had been 44px wider than its own derivation asked for and
never followed the change. Measured at Menomonia 14, the widest run the
column can be asked to hold from a plan's structure alone is
`CRAFT / TP / VENDOR` at 171px, which 231 seats at full padding with 56px to
spare. An ownership annotation is wider than that, but it depends on the
player's inventory rather than on the plan, and rows carrying one still
degrade through `ComputePillFit` exactly as they did: the run's budget at the
floor went from 225px to 227px, so no row can chip that did not chip before.

The column itself is no longer flat, because a "+N" chip on a window with
hundreds of unused pixels in the name column beside it is a lie the reader
cannot act on. `Services/TreePillColumnMath` derives its width the way
`EffectiveCostColumnWidth` already derives the cost column's: the widest full
run any row in the tree needs, floored at
`PlanRelayoutMath.TreePillColumnWidth` and capped at the space actually
available between the column's two neighbours' minimums - the whole panel
surplus past the module's minimum width leftward, plus the cost column's
reserve above what its rows actually draw rightward
(`TreePillColumnMath.Affordable`). An earlier cap of half the surplus was
what a second look in game caught: the "1x Obsidian Shard" row
still chipped on windows with room to spare on both sides of the column.
Each direction stops at that side's own minimum. Leftward the name column
keeps the budget it holds at the minimum window - the budgets
`docs/research/minimum-window-width.md` derives - so at or below that
minimum the surplus term is zero. Rightward the cost column keeps
`TreeCostColumnMath.TotalWidth`, and the pills' claim on the slack swaps
cost reserve for pill width one-for-one (`TreePillColumnMath.RightClaim`;
`EffectiveCostColumnWidth` nets it out), so PillColX, every cost value and
every name budget hold wherever the unclaimed layout put them - which is
why widening the window can never leave the name column narrower than it
was one pixel earlier, even at the minimum width. Measured on the reported
Obsidian Heavy Breastplate rows at a 1920px window: a
CRAFT/TP/HAVE-annotation row went from two pills and a "+1" chip, tightened,
to all three at full padding, and the column took 82px of the 1314px the
depth-0 name column held.

Like the cost column's, something is held as a one-way floor for the life of
a plan (`TreeCostColumnFloor` says why a column edge that narrows under a
click is a bug), and `TryRefreshInPlace` declines when it moves. What is
ratcheted is the widest run the plan has ever REQUIRED, not the width it was
granted. The two are identical at a constant panel width, because clamping is
monotonic - `max(clamp(a), clamp(b))` is `clamp(max(a, b))` - and they part
only across a resize, which is where ratcheting the granted width was wrong
twice over. It froze the share a wide window had afforded, so narrowing back
to the minimum left the name column without the minimum-window budget the
paragraph above says it keeps; and `RightClaim`, re-derived from the now
smaller surplus, re-attributed those frozen pixels to the cost column's
slack, moving `PillColX` at a constant pill width. Ratcheting the ink instead
holds exactly the quantity an ignore click shrinks, which is the whole reason
the floor exists.

`TreePillColumnMath.Resolve` settles the width and the claim together from one
`Affordable` and one surplus, and `TryRefreshInPlace` gates on BOTH. The claim
is netted out of the cost column by `EffectiveCostColumnWidth`, so an in-place
refresh that kept a stale claim placed its rows where a full render at the
same window size would not have.

That "+N" pill is deliberately not wired to a popup offering the hidden
options. The hidden pills are almost always the trailing annotation, and a
real affordance means a new popup or menu surface, with
its own dismiss, focus and scroll behaviour, hanging off a case that tightened
padding already resolves most of the time. The tooltip states the fact; the
sandbox check decides whether the fact needs an affordance.

### V.34 `TreeToolbarCommands`: why the buttons left the section header

The Recipe Tree's action buttons used to sit in the tree's own section header,
inside the scroll flow - so on a long plan, the moment Collapse All became
useful was the moment it had scrolled off screen. The buttons moved out to the
non-scrolling strip; the override/ignore state they act on could not follow,
which is what this seam carries.

The would-change predicates exist because a dialog that protects nothing
teaches people to click through dialogs, and a click that changes nothing has
to say so rather than silently re-solving.

### V.35 `UiFonts`: the ramp and its Blish-facing edges

The ramp is two reading sizes (`Caption` 14, `Body` 16) and three emphatic
tiers above them (`ColumnHeader`, `SectionTitle`, `Display`), with weight doing
as much of the work as size. Blish surfaces five sizes as `DefaultFontNN`
properties; every other size in the installed Menomonia inventory (8-36, bold
at 8-24 and 36) loads through `ContentService.GetFont`.

Blish's own `Label` default is `DefaultFont14`, so a `Label` this module builds
without an explicit `Font` renders one step below `Body`. Every label site
therefore sets one. The three excluded control types stay at that default for
their own reasons: `Checkbox` exposes no `Font` property at all, and `TextBox`
and `Dropdown` have internal padding Blish authors against `DefaultFont14`
while holding typed values rather than module prose.

`FeedbackButton` used to be a fourth exclusion, because `StandardButton`
exposes no `Font` either. It now declares `Caption` explicitly, so the button's
size is a decision this ramp made rather than a Blish default it happened to
match.

### V.36 `UiMetrics.ButtonHeight`: how 28 was picked

Three heights were in use across the tabs: 30 on the Snapshot tab's Clear Cache
and Refresh Now, 28 on the Log tab's three buttons, Settings' Save and the
plan's Generate Plan, and 24 on the plan's five Recipe Tree actions and its
per-row +/- pair. 28 wins on button count, and it is the height of the one
input row a button already shares - the plan's item row, whose
`AutocompleteTextBox` and quantity `TextBox` are both 28, beside its +/- pair.

It is not the module's input height, and the constant should not be read as
having settled that question. `TextBox`es are 26 at nine of their eleven sites
(Settings' six, the Snapshot and Log search boxes, About's), and the two
`Dropdown`s outside the plan tab are 30, so the Log toolbar's run is three
input heights wide before any button is placed.
### V.37 `DialogWindow`: resizing a window Blish sizes once

`WindowBase2` takes its window and content regions in a PROTECTED
`ConstructWindow` and `Container.ContentRegion` has no public setter, so a
dialog that wrote its own `Height` from outside would keep the content
region it was built with and walk its buttons out of it - which is what the
pre-sizing `ModalDialog` documented as the reason it could not grow.
Subclassing is the seam that reaches `ConstructWindow`, and re-calling it
recomputes padding, content margin, background ratios, title-bar bounds and
`Size` together, exactly as a fresh window would have them.

Two offsets come out of the decompiled 1.3.0 arithmetic and are worth
writing down, because neither is visible from the call site. First,
`ConstructWindow` places the content region at `contentRegion.Y + 40 -
Padding.Top`, and `Padding.Top` is `Math.Max(windowRegion.Top - 40, 11)`,
which for a window region starting at 0 is the 11 floor: a 35px inset lands
the content 64px down. Second, its own `base.Size = windowSize` assignment
fires `OnResized`, which recomputes the content region from `Size` minus the
content margin. So the height actually handed to `ConstructWindow` is not
the height the region ends up with. `DialogWindow` passes the REQUESTED
content height rather than the window region's remainder: when `OnResized`
fires the region lands 11px taller, and when it does not (a resize to the
size the window already has) it lands exactly as passed. Both hold the
content box; passing the remainder would leave the shorter of the two 11px
short, with the button row's bottom edge outside it.

`ChromeHeight` is the 74 that falls out: 24px of window above the content
region, the 40px title bar, and the 10px kept below.

### 12.1 Two verdicts, two severities

`PlanStore` mirrors `SnapshotStore`'s shape - single-file JSON, atomic
`.tmp`+`Replace` write - with one deliberate divergence: an unreadable
file is not silently swallowed to null the way `SnapshotStore`'s
`Deserialize` is. It is logged first. A missing file stays silent, because
"fresh start with no plan" is the ordinary first-run case rather than a
failure.

The two unreadable-file verdicts carry two different severities because
merging them once cost a full forensic investigation (2026-08-23). A
corrupt or otherwise unparseable file goes to `onError` at Warn, the same
as every I/O failure; a file written at a *shipped* schema version below
`PersistedPlan.MinimumReadableSchemaVersion` - expected, benign, and
repaired by the next Generate - goes to `onInfo` at Info. Any caller wiring one and not the other silently drops half the
story.

`PlanStoreHelpers.DeserializePersistedPlan` is what makes that split
possible: unlike `SnapshotHelpers`' silent-null precedent it does not
swallow a parse or schema failure itself, but lets the exception
propagate to `PlanStore.LoadLatest`'s single `try`/`catch`, which owns
both callbacks. Its formatting is compact rather than indented, for the
reason `SerializePersistedPlan`'s own doc comment gives.

### 12.2 The gzip container

The on-disk container is gzip: a large plan's compact JSON runs ~700 KB,
and this file is rewritten on every override-resolve pill click, not just
once per Generate. The `plan.json` name is kept as-is with no `.gz`
rename - `LoadLatest` sniffs the first two bytes for the gzip magic number
(`0x1F 0x8B`), so an existing plain-JSON `plan.json` from before this
change (PR #107) still loads, and `Save` always writes gzip going forward.
The payload schema is completely unchanged; only the container encoding
differs, so every existing tolerance guarantee (truncated or corrupt data,
one Warn, return null, never partial) is preserved by construction -
decompression and JSON parsing both happen inside that same single
`try`/`catch`.

`PlanStore.Save` takes an internal lock, unlike `SnapshotStore`/
`StatusStore` whose callers are already serialized by a higher-level
in-flight guard (`Module`'s `_refreshInProgress`). It has two genuinely
independent call sites - a Generate's own post-await persist, and a
pill-click override re-solve's fire-and-forget background persist (see
`Module.cs`'s `PersistAfterGenerateAsync`/`PersistResolvedPlanInBackground`)
- which can race, because a decision pill on an old plan stays clickable
while a new Generate is in flight. The lock is what stops two overlapping
writers being mid-write to the same `.tmp` path at once.

`PlanHistoryStore` serializes indented, following `RankerStore`/
`SnapshotHelpers` rather than `PlanStoreHelpers`' compact choice: the
index is single-digit KB and rewritten once per Generate, not per pill
click, so the compact decision's rationale does not apply to it.

### 12.3 What the structural validator guarantees

`Services/PlanStructuralValidator.cs` walks the entire object graph a
deserialized `PersistedPlan` carries, at the deserialization boundary,
before the file is accepted at all. Every check it makes exists because
some production path dereferences that exact field with no null guard, on
an assumption that holds for every solver-*built* `CraftingPlanResult`/
`PlanSolveContext` - those are only ever constructed by `PlanSolver`/
`PlanResultBuilder`/`CraftingTreeBuilder`. A restored plan is the one path
that bypasses the solver and hands the same types straight from disk into
that trusted code, so the invariants are re-established once, centrally,
instead of at each call site.

Centrally, because several of those sites sit outside any `try`/`catch`:
Expand All and the per-node toggle call `RenderTreeNode` straight from a
click handler, on nodes a guarded initial render never visited because
they were collapsed, and Craft All/Buy All walk the whole tree through
`BuildPresetOverrides` before `ApplyOverridesAndResolve`'s catch is
reached.

### 12.4 What the shape hash last moved for

`PersistedPlan.SchemaShapeHash` last moved for the missing recipe sheet
sources and for what a vendor-requirement notice now says, both purely
additive: `CraftingPlanResult.MissingRecipeSheetSources` and the
`MissingRecipeSheetSource` type it reaches, plus
`VendorRequirementNotice.Kind`, `VendorRequirementNotice.UnknownReason`,
`CraftingPlanResult.AccountProgressionAccess` and
`PlanSolveContext.AccountProgressionAccess`. An older file omits them,
Newtonsoft leaves each at null or its zero value, and a restored plan then
prices no missing sheet and tells the reader to generate the plan again
before it can say whether a gated vendor will trade. A plan written before
it still deserializes and `CurrentSchemaVersion` stays at 4.

Before that it moved for vendor requirements, also additive:
`VendorOffer.Requirement`, `PlanStep.VendorRequirement`,
`PlanSolveContext.AccountProgression`,
`CraftingPlanResult.VendorRequirementNotices`, and the `VendorRequirement`,
`VendorRequirementNotice` and `AccountProgression` types they reach.

Before that it moved for a REMOVAL, and removals are
what `CurrentSchemaVersion` exists to gate: `VendorOffer.Locations` is gone,
so the version went 3 -> 4. It cost no saved result, because a removal is
the one shape change the current types absorb whole - see 12.5 - so the
floor stayed at 3 and a version 3 file still restores its plan and every
Plan History blob. Nothing reads a location: the module dropped
the field to stop holding 2.19 MB of place names for the whole session, and
`Services/VendorOfferLocations.cs` reads them back off
`ref/vendor_offers.json` on demand. Measured saving on the loaded corpus:
71,226,080 bytes of managed heap before, 60,178,792 after.

Before that it moved for the plan-level barter item
total, which is purely additive: `CraftingPlan.BarterItemCosts`,
`PlanStep.VendorBarterItemCosts` and the `BarterItemCost` type they reach.
An older file omits all three, Newtonsoft leaves the lists null, and a
restored plan then shows no barter rows in its Total Cost table until it is
re-solved - the same degradation shape the previous addition had. That one
was additive, so a plan written before it still deserialized and
`CurrentSchemaVersion` stayed where it was.

Before that it moved for the currency tooltip work, also purely additive:
one string, `CurrencyMetadata.Description`, absent from an older file and
left null by Newtonsoft, which drops the tooltip's paragraph and nothing
else. A bump now costs a re-solve rather than the plan, but it still costs
one.

It does cost bytes. The persisted `CurrencyMetadata` is the whole
`/v2/currencies` reply, so every saved plan grows by the descriptions of
all 79 currencies - measured 2026-08-28 at 8.5 KB raw, ~2.5 KB gzipped,
per plan blob.

### 12.5 Which past plan versions are readable, and why

`PersistedPlan.MinimumReadableSchemaVersion` is 3. A version belongs in
the range when nothing a file stamped that way carries can be *misread* by
the current types - not when it merely parses.

Two of the four shape changes are absorbable and two are not:

- An **addition** is free. Newtonsoft leaves an absent member at its
  default, and there was no value on disk to lose.
- A **removal** is free. Newtonsoft skips a JSON property no type claims,
  and nothing that survived the removal moved.
- A **rename** is not. The old name is skipped and the new one defaults,
  so a value the file *did* carry is silently lost.
- A **retype** is not. Newtonsoft either throws or coerces, and a coerced
  value is a wrong one.

Read against that, each bump:

| Bump | What it did | Below it readable? |
| --- | --- | --- |
| 3 -> 4 (`b03eb0ef`) | Removed `VendorOffer.Locations`, nothing else. | **Yes.** A removal, so a version 3 file is complete and its extra property is skipped. |
| 2 -> 3 (`35e97ed3`) | No shape change of its own. It retired a stamp left at 2 while the graph grew ~275 unversioned lines. | **No.** "2" names no single shape, so there is nothing to check a version 2 file against. |
| 1 -> 2 (`c55596a9`) | Added `PersistedPlan.ValueOwnMaterials`. | **No.** Additive in itself, but 1 sits under the same unversioned drift 2 does. |

The evidence for the 3 -> 4 row is `tests/shared/persisted_plan_schema.txt`:
across its whole history the only deleted line is the one for
`VendorOffer.Locations`, and the part of version 3's life
that predates the snapshot (`35e97ed3`..`0492fc88`) contains no property
removal, rename or retype in any type the persisted graph reaches.

A future bump for a rename or a retype must raise the floor to that bump's
version, in the same commit, with the member named in the message. A bump
for an addition or a removal must leave the floor alone.

**Every other persisted store.** `PlanHistoryStore` already read a range.
`PlanHistoryBlobStore` reads through `DeserializePersistedPlan` and
inherits the plan's. `RankerStore` had the same exact-version rule the plan
store did and now reads
`[RankerWatchlist.MinimumReadableSchemaVersion, CurrentSchemaVersion]`;
only version 1 has ever shipped, so its accepted set is unchanged and the
constant is there for the next bump to decide against.
`OverlayRecipeCacheStore` already migrates its own version 1 file rather
than rejecting it. `SnapshotStore`, `StatusStore` and `ModuleLogStore`
carry no version stamp at all, so there is no rule to relax;
`VendorOfferStore` reads shipped reference data, not user data, and never
compares its stamp.

Between `b15b3fd2` and `dfdc5eac` master briefly stamped 4 for a different
reason - a public `CraftingTreeNode.IsPlanRoot` - and that bump was
reverted rather than released. No tag ever carried it; a file from such a
build reads today because `IsPlanRoot` is now `internal` and its JSON
property is skipped like any other unclaimed one.

---

## S1. Services A-P: relocated design narrative

Derivations, histories and investigations moved out of over-length XML doc
comments under `Services/` (A-P) and `Models/`. Each subsection is cited by
the comment it came from; the comment keeps the invariant a caller can
violate, and this is where the "how we got here" lives.

### S1.1 The logging system: one ring, one file sink, two locks

`Services/ModuleLog.cs` is an ordinary instantiable class rather than a
static one so tests can construct isolated instances (`new ModuleLog()`)
with deterministic, non-shared state regardless of xUnit's default
cross-class parallelism. Production call sites use the single app-wide
`Shared` instance instead of threading a `ModuleLog` dependency through
every constructor - deliberately the "static-or-singleton" shape
`dev/proposals/d2-log-system.md` section 4.1 describes, resolved as a
singleton-by-default instantiable type specifically so it stays testable.
It is Blish-free for the same reason `ModuleLogEntry` is, and the Log tab
reads the ring on its own `Version` poll rather than through a push
callback - the same producer/consumer separation `Module.cs` already uses
for its own dirty-flag fields.

**Why two locks, and why `Write` never touches disk.** `_gate` guards the
in-memory ring and `Version` - fast, pure in-memory work, taken by every
`Write` and by `Snapshot()`. `_fileGate` guards the attached file sink -
slow, real disk IO. `Write` hands its entry to a single-consumer background
flush queue rather than performing IO itself, so neither lock, and
therefore neither the Log tab's per-frame `Version` poll nor any other
caller's ring access, can ever block behind file IO regardless of which
thread called `Write` or how large the file has grown. That was a real
live hazard before the split: the `[scrolldiag]` Debug channel calls
`Write` on the main thread from inside `CraftingPlanView`'s
frame-timing-sensitive scroll-verify loop, so any synchronous disk IO
inside `Write` - never mind an occasional full-file read+rewrite trim -
would stall that exact frame. `Write` checks `_store` as a volatile field
read for the same reason: taking `_fileGate` to find out whether a sink
exists would reintroduce the stall against a different lock.

`SeedFromStore` is the one path that holds both locks, always `_fileGate`
then `_gate`. It holds `_fileGate` for the whole read+seed, serializing
against the background flush loop and against `PruneOlderThan` so the read
can never race a concurrent file write or rewrite, and nests `_gate` only
around the ring-append portion, serializing against a concurrent `Write`'s
own ring append from a background continuation during startup (the build-ID
fetch's `Task.Run`) - without which a brand-new entry could land in the ring
chronologically *before* the seeded history. Every other path holds one lock
at a time, so nothing can complete the opposite ordering and deadlock. It
resolves d2 section 7's Open Question 2 as yes: pre-session history is
visible on first tab-open, not just "since this launch".

`DeleteFileAndReset` (d2 section 7, Open Question 4) starts with a brief,
bounded spin-wait drain of the pending flush queue so entries queued before
the call land in the file before it is deleted rather than resurrecting it
afterwards. Best-effort: an entry still in flight past the budget - a hung
disk - can land in the recreated file, which is a stale line in the new log
rather than a correctness hazard, and in practice the queue is empty at the
moment a user clicks the button, so the common cost is zero. Clearing only
the view floor would let `SeedFromStore` resurrect every entry next session;
deleting only the file would leave this session's ring intact - hence both
halves.

`Services/ModuleLogStore.cs` writes JSONL rather than one big JSON array
(the `SnapshotStore`/`StatusStore`/`VendorOfferStore` shape) because a log
is fundamentally append-heavy: a crash mid-append to JSONL loses at most the
last, tolerated-as-partial line, whereas a crash mid-write to one big array
can corrupt the entire file. Rotation is split into two independently
callable, independently testable operations rather than d2's single combined
`RotateIfNeeded(maxBytes, maxAgeDays)`, matching how they actually fire at
different cadences. Every public method has its own internal try/catch and
calls its own `onError` rather than propagating, which is why
`ModuleLog.Configure`'s `onStoreError` parameter is defence-in-depth rather
than the error path a caller should expect to see fire.

### S1.2 Shared layout laws

`Services/GridLayout.cs` states the column-grid law once. It was stated
three times before the class existed - `SnapshotItemGridLayout`,
`SettingsCurrencyGridLayout` and `ColumnBoardLayout` each carried their own
`ComputeColumnCount` and `ComputeColumnWidth` with character-identical
bodies - and the copies had already drifted apart once and been re-synced by
copying again, leaving a note that pointed a reader at a sibling's prose
rather than at shared code. Grid geometry meant to agree now agrees by
construction.

`Services/JustifiedColumnTracks.cs` was written for the currency table,
whose packed stack left ~1000px of nothing between a currency's name and its
first number with no anchor for the eye between them. The Plan History table
had the same shape and the same complaint, so the arithmetic lives in one
place rather than two: a second copy is how two tables that are supposed to
read alike drift apart. Right-aligning a header and its cells to a shared
edge also lines them up, but only at that edge - a short header over wide
cells then reads as belonging to the column on its right, which is why the
tracks centre instead. `Services/PlanHistoryRowLayout.cs` is that law applied
to one row.

`JustifiedColumnTracks.CenteredOverContent` is the second half of that same
law, and the correction to the first attempt at it. Centring each header on
its column's reserved *band* was shipped first and read wrong on the plan
panel: a band is invisible, so a header centred in one drifts off the ink it
names by half of however much the band exceeds it, and a band routinely
exceeds it - a fixed floor (`ShoppingColumnMath.TotalMinWidth`), a
header-width floor (every band in the module is floored at its own label so
the label fits), or a reserve shared by several columns and sized by the
widest of them (`SummarySectionLayoutMath`'s one `NumberColumnWidth` across
Required/Have/Needed). Measured on a 2026-08-28 capture: the
Recipe Tree's "Source" header centred at x~797 over a badge run occupying
700..765, and the currency table's "Have" header sat over neither its own
numbers' right edge nor their centre.

The cells were never the problem and do not move: badges stay left so their
left edges rule down the column, numbers stay right so their digits line up.
Only the header moves, onto the centre of the extent the cells actually
cover. The caller derives that extent from the cells' own justification
(the column's left rule for a left-ruled column, `rightEdge - contentWidth`
for a right-aligned one).

Clamping that result back into the *band* was the first attempt's own second
defect, and shipped the disease as the cure. `bandX + bandWidth - headerWidth`
is the column's right edge whenever the band ends there, so the clamp fires
for every header wider than its column's ink - which is most of them, since
every band is floored at its own header label - and pins the header's right
edge to the values' right edge. That is right-alignment: precisely what the
centring was added to remove, and what the 2026-08-29 capture measured,
where Required/Have/Needed sat 17, 12 and 15px left of their ink -
exactly half of each header's excess over the numbers under it.

A band is not a boundary. What a header must not reach is the *neighbouring
column*, and on a justified table those sit a whole track apart. So the
clamp is `JustifiedColumnTracks.HeaderRoom`: from the boundary with the
column on the left to the boundary with the column on the right, each
boundary the middle of the gap between the two columns' ink and each header
backing off half a `HeaderGutter` from it, so two headers that both run to
their bound stay a whole gutter apart and can never touch. Where there is no
neighbouring column the bound is the table's own edge, which nothing crosses.
A column's own ink is always inside its room, however little gap precedes it,
and a header wider than the whole room pins to the room's left bound and
spills rightward only - the one direction `CenteredX` already spills in.
Used Materials' Amount column is the case that reaches that last rule: it
reserves nothing beyond its widest quantity and pins to the table edge, so
its header has ~27px of room and cannot be centred at all.

`Services/LogGutterLayout.cs` replaced one worst-case prefix *template* -
the widest level word, a widest-digit stamp, and a fourteen-`w` tag
allowance, measured once and applied to every row at every width whatever
the rows actually contained. The template existed for a real reason,
recorded on `LogTabContent.FullPrefixWidth`: the incremental append path
sees only the new entries, so a width derived from what it can see would
drift from what a full rebuild produced. That is answered rather than
reverted - the view holds the widest rendered tag as a monotonic high-water
mark per render generation, so both paths agree by construction while the
band still tracks the content.

### S1.3 Icon tiers: measured from the game

`Services/ItemIconTiers.cs` and `Services/CurrencyIconTiers.cs` hold two
sizes each, matched to the game's own two inventory and two wallet tiers
(decided 2026-08-26 and 2026-08-27). Both are Blish-free so the
layout math that reserves room for an icon and the view that draws it read
the same number.

**Item tiers**, measured from the staged references against in-game tooltip
text (~14px, the same class as `UiFonts.Body`, so game pixels at default UI
scale read 1:1 as module logical pixels): `bag-icon-size-reference.png`
shows a main bag grid at ~59-60px slot pitch with ~54-56px of slot art;
`bag-sidebar-icon-size-reference.png` shows the bag side bar at ~44px pitch
with ~39-40px of art - a sidebar-to-slot art ratio of ~0.72.

**Currency tiers**, measured from `gate-ranker/currency-wallet-list-reference.png`
and `gate-ranker/currency-summary-bar-reference.png`. Calibration follows
the tooltip fidelity audit's method - a capture counts as native only once
one of its metrics is shown to match a known native one - but lands a far
tighter anchor than that audit's text pitch can. The live gold-coin currency
texture (asset 156904) is 32x32, and template-matching it against the wallet
list row is pixel-exact at scale 1.000: mean squared error 2.0 over the
texture's opaque pixels, against 887 and 1176 one pixel either side. A
capture cannot match an unresampled source that closely unless it is native
1:1, so game pixels read 1:1 as module logical pixels - the same conclusion
the item tiers reached, independently corroborated by the bag sidebar
measuring the same 44px pitch in this capture as in its own reference.

**Where the icon sits**, measured at both currency tiers: the box is centred
on the number's *ink* rather than sitting on the baseline - list tier, icon
box y178..209 (centre 193.5) against the "841" glyph ink y188..198 (centre
193.0); bar tier, box y114..129 against ink y115..126, within a pixel at
half the size. That is the rule the renderers spell as
`iconYOffset = (textHeight - iconSize) / 2`, recorded here so it need not be
re-derived from a screenshot.

**The coins are the exception**, decided 2026-09-04: gold,
silver and copper seat their *art* on the number's ink bottom
(`CoinIconY`, in `Services/CoinSegmentMath.cs`), because a centred *box* leaves the
padded coin art reading high against the digits. Two paddings sit between
the two numbers a caller holds. A Menomonia glyph box is one pixel taller
than its ink at each edge, the faces being built with `outline="1"
spacing="1,1"` - measured on the shipped 14-regular, 16-regular, 20-bold and
32-regular pages, every `0` inks exactly rows 1..height-2 of its own box. And
the three 32x32 coin textures draw rows 5..23 (gold 156904, silver 156907)
and 7..23 (copper 156902), so their shared bottom edge is row 23 and the
denomination that starts lower never enters a bottom seat. Rows 24, 25 and 26
carry ink but no coin: composited over a dark row ground they come out darker
than the ground on all three textures, being the art's black bottom rim. A
seat that counts them hangs three rows of shadow under the digits and leaves
the visible coin two rows high. `CoinInkBelowBaseline` is read ink against
ink for the same reason - the icon box's matched position in a bar tier
capture is a template fit against art the game rescales itself, and it does
not resolve to the row. Reading it *within* one capture also keeps it free
of that capture's UI size: both edges scale together, so the row count
carries into a logical constant without a conversion. Every other inline
currency icon keeps the centred seat: they measured centred to within half a
pixel in the same capture that reported the coin defect, and were left alone
deliberately.

**The tooltip header tier is the one that had to be converted**, decided
2026-09-05. A capture is native 1:1 only relative to the GW2 UI size it was
taken at, and Blish paints module coordinates through that size's own scale
(0.897 at "Normal", 1.0 at "Large"), so a number lifted off a capture is in
game UI units only when the capture was taken at "Large". The item and
currency tiers above were; the tooltip header icon's 34x34 was not. The same
frame proves it: a currency tooltip capture shows the game's header icon at
34 physical pixels beside the module's own 34-unit frame painted at 31, which
puts that capture at 0.897. So the tier holds 34 / 0.897 = 38, which paints
the game's 34 at "Normal" and its 38 at "Large".

**The frame is reserved at one pixel and painted at two**, decided
2026-09-12. `ItemIconTiers.FrameBorder` is the term every `FrameSize` is
built from, and the measured art windows above are what fix it at 1. But a
1px band is 0.897 physical pixels at GW2 UI Size "Normal", and the GPU
covers a scanline only when the scanline's centre falls inside the band, so
the band misses entirely on 10.3% of the vertical positions a window can
take and on 42.0% at "Small". A reader sees an icon with one edge of its
frame gone and reads the icon as cut off, which is how this was reported.
`ItemIconTiers.PaintedFrameThickness` is 2 for the reason
`PlanContentHeightMath.RowDividerHeight` is: `floor(2 * 0.81) = 1` covers a
scanline at every shipped scale. The extra pixel comes out of the ART, so
every `FrameSize` and every layout built on one is unchanged, and the art
inside the tooltip header's 38 is 34 rather than 36. The sweep is
`tests/TaimisToolbench.Tests/Services/IconFrameScissorSimulationTests.cs`,
over the same paint model as section V.26's divider proof.

Thickness alone is not sufficient where a row's own container ends on the
row's bottom edge. That container hands its children a clip that has been
through one floor/ceil round trip and can sit up to two logical pixels
high, which reaches into the last row's frame. The Snapshot tab's result
panel is sized to its content and so ended exactly there;
`SnapshotResultLayout.TrailingClearance` moves its bottom edge one pixel
past the last row. The sweep covers that case at zero clearance and fails
on it, so the pixel cannot be read as slack and dropped.

### S1.4 Item tooltips: what the API says and what the game shows

`Services/ItemStatBlockFactory.cs` is where every "what does an absent field
mean" decision is made, so the composer downstream only renders facts.
Stat-*selectable* items (non-empty `stat_choices`) record only how many
combinations exist: computing numbers for one nominated combination is
possible - see `Services/ItemStatMath.cs` - but *which* one is an open
judgment call (KNOWN-ISSUES #40, Q4), and it is the only thing in this
feature that would need a `/v2/itemstats` request.

**Bindings.** `ResolveBindings`' independence rule and its bare-versus-"on
Use" wording come from captures: relic-livingcity 104938 (2026-08-26) shows
"Account Bound" and "Soulbound on Use" stacked on one item (AccountBound +
SoulBindOnUse), which is what rules out a most-specific ladder. Within a
dimension the stronger flag wins - live3 almonds 12337 and fury-scorched
86967 both carry AccountBound *and* AccountBindOnUse and render one account
line. Five captures carry a bind-on-acquire flag and read bare: Gift of
Twilight 19648 (the 2026-08-27 A/B, the same item hovered in the
module and in the game), heart-of-destroyer 67017 and holographic-wings
79157 - all AccountBound + AccountBindOnUse, all bare "Account Bound" -
relic-livingcity 104938, and red-festival-lantern 68638 (SoulbindOnAcquire +
SoulBindOnUse, bare "Soulbound"). The "on Acquire" wording appears on
exactly two captures, almonds 12337 and fury-scorched 86967, and both are
*material storage* hovers, where the copy shown is not bound to anyone yet.
Which copy the player is looking at is instance state `/v2/items` cannot
carry, so the majority-and-A/B wording wins.

**Attribute arithmetic.** `ItemStatMath`'s formula is measured on
Berserker's (itemstats 161, .35/.25/.25) against Zojja's
Warfists/Pauldrons (adjustment 134.442 -> 47/34/34), Visor (179.256 ->
63/45/45), Tassets (268.884 -> 94/67/67) and Breastplate (403.326 ->
141/101/101). `ItemStatMathTests` asserts against those published answers
rather than against the method's own arithmetic.

**The consumable block.** `ItemStatTooltipComposer.AppendConsumableEffect`'s
colours are measured on live3 soul-pastries / candy-corn / omnomberry
(2026-08-26): all three saturate at (170,170,170) for every line of the
block, superseding F7's white-first-line split, whose allspice/steak
evidence was JPEG-era. The effect lines' own `+`/`%` prefixes come from the
API text and are not normalised - omnomberry carries "30% Magic Find" beside
"+10% Experience from Kills".

**Descriptions.** `Services/ItemDescriptionSanitizer.cs` keeps the API's
colour spans as roles rather than discarding them because the game colours
only the marked runs and leaves unmarked text white, which is the only way
"A gift bag!" can be told apart from the quoted flavour that follows it
inside one description string (KNOWN-ISSUES #42, gap G7). Unknown angle-
bracket content is passed through rather than stripped: a blanket
tag-stripper would silently delete real item text the day the API uses a
bracket for something that is not markup.

**Equipment slots.** `Services/ItemSlotFacts.cs` answers two questions the
API answers only half of. Infusion and enrichment slots are read straight
out of `details.infusion_slots`; the flag vocabulary there is exactly
`Infusion` and `Enrichment`, one flag per slot at most, which is both what
the schema says (`https://wiki.guildwars2.com/wiki/API:2/items`, "The array
contains a maximum of one value") and what a census of all 74,072 items
found on 2026-09-05: 6,507 `Infusion`, 94 `Enrichment`, nothing else, no
empty flag array and no slot with two. The four infusion glyphs `/v2/files`
publishes are pre-2016 artwork: the 2016-07-26 release merged the offensive,
defensive and agony subtypes into one infusion and renamed utility infusions
to enrichments, so no live flag distinguishes them. Two of the four are
byte-identical.

Upgrade slots have no API field. The count comes from the item's type - two
sigils on a two-handed weapon, one on a one-handed one, one rune per armour
piece, one jewel per trinket or back item - and from the `NotUpgradeable`
flag, which is what removes the slot from an ascended trinket or back item
(their jewel's stats are baked in) and from the Bloodbound and Dreambound
weapon families at ordinary rarities. Every one of the 705 ascended and
legendary trinkets and back items in that corpus carries the flag, so keying
on rarity instead would be strictly worse. `details.secondary_suffix_item_id`
is non-empty only on the nine two-handed types and would corroborate the
rule, but it is empty on almost every two-handed weapon in the game, so it
cannot derive it.

Both counts net off what the definition already ships socketed, because the
game prints a filled slot's contents rather than an unused-slot line. WHICH
sigil or infusion that is needs a second `/v2/items` request and is not
fetched; a filled slot simply produces no line. "Unused Upgrade Slot" and
"Unused Infusion Slot" are verbatim from live captures; the enrichment
line's wording follows the same pattern.

`Models/ItemStatBlock` stays off `ItemMetadata` because `PersistedPlan.Result`
is a `CraftingPlanResult` holding the `ItemMetadata` dictionary, and
`PersistedPlanSchemaMemberSetTests` guards that whole reachable graph against
`PersistedPlan.CurrentSchemaVersion` - so hanging stats there would force a
schema bump (section 12).

### S1.5 The two decision vocabularies

`Models/AcquisitionSource` is the solver's vocabulary and
`Models/CraftingDecision` the display layer's. They diverge because the
solver never needs an owned/ignored state - that is display-only,
`CraftingDecision.Have` - while the tree builder never needs a distinct
currency-leaf source. The single bridge is
`Services/CraftingTreeBuilder.MapSource`.

Per-member mapping:

| `AcquisitionSource` | `CraftingDecision` |
| --- | --- |
| `Craft` | `Craft` |
| `BuyFromTp` | `BuyFromTp` |
| `BuyFromVendor` | `BuyFromVendor` |
| `Currency` | `Currency` in principle only - see below |
| `UnknownSource` | `Unknown` |
| (none) | `Have`, `GuildUpgrade`, `UnrecognizedIngredient` - set directly |

`AcquisitionSource.Currency` exists because `PlanStep.Source` shares the
enum for aggregation bookkeeping, but `CraftingTreeBuilder` never routes a
currency leaf through `MapSource` at all: it sets `CraftingDecision.Currency`
directly as soon as it sees a non-`"Item"` ingredient type, before any
decision lookup. `UnknownSource` -> `Unknown` is a genuinely reachable
production path - gw2efficiency's "Not sold or crafted": no recipe, no TP
price, no vendor offer - and it is the only case that legitimately offers
the pill layer's interactive IGNORE toggle, because the user may already
have the item in hand with no way for the module to know.

`GuildUpgrade` is set directly for a Guild Decoration recipe's
claimed-upgrade requirement and is deliberately separate from `Currency`: a
guild upgrade id and a wallet currency id are distinct id spaces with no
defined relationship, so resolving one as if it were the other on the
strength of a numeric match would risk silently showing the wrong name or
price on any collision. Full guild-decoration crafting support - resolving
the upgrade's real name, verifying ownership - is out of scope
(KNOWN-ISSUES #54). `UnrecognizedIngredient` covers an ingredient type that
is none of `"Item"`, `"Currency"` or `"GuildUpgrade"`, and is distinct from
`Unknown` because a shared value once gave that leaf the interactive IGNORE
toggle, keyed on a raw non-item id that could silently zero an unrelated
`"Item"` node sharing the same numeric id. Its own value routes it to the
single-locked-pill short-circuit instead.

`GuildUpgrade` and `UnrecognizedIngredient` are appended last, after every
pre-existing member, in the order they were introduced - the reason the
enum's own doc comment states as a rule.

### S1.6 Tree rows: vendor cost leaves and the pill column

`CraftingTreeBuilder.BuildVendorCostComponentLeaves` synthesizes leaves only
for a mixed-kind winning offer, plus the one pure-barter case. A pure-coin
or pure-TP-item offer shows its whole cost in the parent's own coin cell,
and a pure-currency one in that cell's currency segments
(`TreeCostColumnMath.ShowsCurrencySegments`), but a barter quantity has
neither. Currency leaves, and item leaves for an untradeable barter item,
carry blank cost cells by design; only a TP-valued item leaf must visibly
sum, which is what keeps "parent total = sum of the parts a leaf can show"
true while a raw coin component stays folded into `SubtreeCost`.

`Services/DecisionPillPlanner.cs`'s pill count *is* the affordance: 2-3
pills means a real choice, exactly one means the source is locked. The
`default` arm of its source switch is a non-crashing safety net for a future
regression, not a real code path.

A row the plan buys for one wallet currency and nothing else is a
*currency trade-up* row (`Services/CurrencyTradeUpRow.cs`). Three Secrets
of the Obscure materials are the live case: 250 map currency each, from
several merchants, at one fixed price. The module deliberately plans no
route to the currency itself - it is earned several ways, capped
differently per way, and there is no cheapest answer to optimize - so the
row states what the held currency buys now and leaves the earning to the
player. Two things follow from that. The row gains a
`BUYS {n}/{needed} NEEDED` annotation pill whose tooltip names the
remainder, and its right-click opens the item's wiki Acquisition section
(`WikiLinkBuilder.BuildItemAcquisitionUrl`) rather than the page top,
because that section is where the player's options are listed. Both
decisions are made in Blish-free classes -
`DecisionPillPlanner.AppendCurrencyTradeUpPill` and
`TreeRowTooltipComposer.BuildWikiUrl` - so the view only wires them up.

The Total Cost table states such an item ONCE, whatever mix of routes the
plan took to it. `Services/CurrencyTradeUpCoalescing.cs` is the rule:
a finished vendor step that pays one wallet currency and nothing else, for a
currency the module puts no coin value on, contributes its own item count to
`CraftingPlan.BarterItemCosts` instead of its currency to
`CraftingPlan.CurrencyCosts`. The reported case was the Obsidian Heavy
Breastplate, whose plan needs each of the three materials twice over: another
vendor offer takes 13 in barter, and the plan buys 5 more with the map
currency. The table used to read "13 Case of Captured Lightning" beside "1,250
Static Charge" - one item, two lines, and neither number was 18.

Two exclusions keep the rule where it belongs. A currency the curated table
prices has a coin equivalent, so the plan can compare a route to it and the
wallet line is the honest statement; only an unvalued one coalesces. And a
requested item is what the plan produces, so its own acquisition is never
folded into a requirement for itself.

A coalesced row carries a Note column saying what a held sub-currency converts
into - the holding, that currency's icon, then how many of the row's item it
buys, capped at what the row still needs. That count comes off Needed, because
a player holding the currency converts it up before acquiring the rest any
other way. Have stays the literal count of the item itself. The note and the
subtraction are one decision in
`PlanViewModelBuilder.ApplyTradeUpNote`: both are skipped when the currency
resolves no icon, so a Needed no visible note accounts for cannot ship. The
column reserves nothing at all when no row carries a note, which is every plan
without one of these items.

`Services/PlanRelayoutMath.cs` fits those pills into the row.
`ComputeVisiblePillCount` is the primitive: at least one pill is always
drawn even when it alone overruns, because a completely empty pill column
reads worse than one slightly-overflowing pill, and every pill after the
first is dropped strictly once it would not entirely fit - a node's pills
only ever grow wider left-to-right, so once one is cut every later one would
be too. `ComputePillFit` is the policy, escalating normal padding ->
tightened padding -> tightened padding with a trailing "+N": squeezing is
cheaper than hiding a real option, and announcing the remainder is cheaper
than dropping it silently. The "+N" pill's own width depends on N, which
depends on how many pills its width displaced, so the last step iterates to
a fixed point; N is non-decreasing across iterations and bounded by the pill
count, so it settles immediately in practice (only a digit-count change
moves it at all). The loop is capped anyway, and `HiddenCount` is derived
from the final `VisibleCount` either way, so an unconverged width is a few
pixels wrong and never a wrong count.

### S1.7 Per-unit currency amounts

`CurrencyDisplayResolver.ResolveUnitAmounts` resolves the winning offer's
own per-batch rate rather than a truncated `total / quantity` average. The
average could show a misleading "1" for a merged row whose real purchases
were, say, a 3-for-3 batch plus a 1-for-1 batch. When the offer data is not
available - mixed offers merged into one step, or a non-vendor row - it
returns null rather than reviving that average: gw2efficiency itself never
shows a per-unit currency price at all (`docs/gw2e-parity-spec.md` section
4.3, directive 5), so omitting the Each cell is the closer parity choice
than guessing. When a line's per-batch count does not divide evenly, the
true rate is not a whole number, and rather than round - inventing data the
spec does not ask for - the amount carries a literal "N for M" bundle label.

`ResolveTreeNodeUnitAmounts` answers the same question for a single tree row
(`TreeSectionController`'s "Unit price:" tooltip line, an in-game finding),
where the true batch rate is simply not present: `OutputCount` and
`CurrencyCostLinesPerBatch` exist only on `PlanStep`, threaded there by
`VendorBatchSolver.FinalizeVendorBatches` for the merged shopping list - a
later, separate pass a single tree node's `SolverDecision` never goes
through. Dividing the node's own total by its own `Quantity` agrees with the
true rate whenever the offer's batches divided evenly into that quantity,
and falls back to the same bundle text when they did not (the total already
includes rounding up to a whole purchase - `EvaluateVendorOffers`'
`unitsNeeded`). It is a display-layer fix: no solver change was needed.

### S1.8 Shopping List sort order

`PlanTableSorter.CompareValue` sorts in three blocks because the column
holds three kinds of cell, not one scale. Reversing the *blocks* would
express nothing - "5 spirit shards" is neither more nor less than "3 gold"
in either direction - and it would float the dash rows to the top, where
they are pure noise, so the block order is direction-invariant while the
order within a block flips.

Currency-only rows sort by currency name first (ordinal, case-insensitive)
so every karma row lands beside every other karma row, then by amount within
that currency - the only numeric comparison in that block that is actually
meaningful. A row carrying more than one currency is keyed on its
ordinally-first currency name and that entry's amount, which is stable
regardless of the order the resolver emitted them in; no attempt is made to
add amounts across currencies. The numeric key is `UnitRate` rather than
`Amount` because a per-unit amount whose rate does not divide evenly carries
`Amount` 0 and shows its rate as bundle text ("912 for 92"), so keying on
`Amount` would sort every such row as free and tie them all with each other.

### S1.9 Learned recipe freshness and timing diagnostics

Every plan generation fetches the account's learned recipe ids from
`/v2/account/recipes`. There is no cache in front of that call. A five
minute cache used to sit there and was removed in September 2026, because
Blish HUD is an overlay on the running game: a player can buy a recipe
sheet, learn the recipe and generate a plan again well inside five minutes.
The learned ids drive two annotations, both of which then told the player
to buy a sheet they already owned: the "already known" flag on required
recipes (`PlanResultBuilder`'s `RecipeRequirement.IsMissing`), and the gate
on `RecipeSheetSavingsCalculator`, which emits a note advising the purchase
of a recipe sheet the account does not own, carrying a `SavingsPerUnit`
coin figure. Neither is a solver input, so the stale answer never produced
a wrong plan, only a wrong recommendation. The cost of the removal is one
extra fetch per generation, measured at 1129ms and 1453ms in a tester's log
on the day it was removed.

A caller generating several plans in a row fetches once instead. The
Crafting Ranker does: `Views/RankerTabContent.cs` calls
`CraftingPlanPipeline.GetLearnedRecipeIdsAsync` once per refresh and passes
the ids to the `learnedRecipeIds` parameter of every generation, which
solves two plans for each watchlist row. Those generations make no call, so
their `Fetch learned recipes` timing line reads about 0ms. A refresh is one
user action, and the account cannot learn a recipe part way through it
without the player leaving the tab. This is a per-run argument, not a clock:
the next refresh fetches again.

`Services/Diagnostics/PlanPhaseTimingSummary.cs` buckets the raw per-step
`timingLog` lines `PlanTimingAnalyzer` already parses (Build recipe
tree/trees, Collect item IDs, Fetch TP prices, Query vendor offers,
Inventory reduction, Solve, Fetch item metadata, Fetch currency metadata,
Fetch learned recipes, Build result) into the coarser phases
`PlanPhaseEvent` exposes to the live UI, rather than
`PlanTimingAnalyzer.Summarize`'s per-raw-step percentage breakdown. It reads
straight from a full `CraftingPlanResult.DebugLog` - raw timing lines, then
`PlanTimingAnalyzer.SummaryHeaderLine`, then `PlanResultBuilder`'s own
reduction/decision lines - rather than needing the pipeline to plumb its
local `timingLog` list out to the wrapper that calls this.

The `wallClockMs` parameter exists because the sum of the raw per-step lines
necessarily omits every un-instrumented gap between them - task scheduling,
awaits resuming, GC - so it is always less than or equal to the wrapper's
own wall-clock `Stopwatch`, and for a real ~19s generation the two can
differ by seconds rather than milliseconds. The single number a a player
actually experiences ("it took 19 seconds") is the wall-clock figure, so
when it is supplied it becomes the "total" with the phase sum appended
alongside as "(phases Nms)" for comparison; when absent - every existing
test, any future non-UI caller - the "total" stays the phase sum exactly as
before.

### S1.10 Dialog sizing: the width bracket and the balanced wrap

`Services/DialogLayoutMath.cs` replaced two fixed rectangles. `ModalDialog`
was a 560x190 window whose every inner offset was derived from those two
numbers, so a one-line acknowledgement and a four-line warning drew the same
box; `ApiAccessDialog` was a second fixed rectangle, 560x300, with its own
copy of the same constants. Both were sized for their worst case.

**Why the width has a floor at all.** Nothing about readability stops a
dialog at 500px. The title bar does. Decompiled BlishHUD 1.3.0,
`WindowBase2.RecalculateLayout`, draws the left title-bar texture into
`Min(textureWidth, windowWidth - 216)` and `DrawOnCtrl` stretches rather
than crops, so a narrow window squeezes the art. The recorded evidence is
two points: a 400px window (about 184px of draw) rasterized as coloured
streaks behind the title, and 560 (about 344px) renders clean. 500 - the
480px `MinContentWidth` plus the shell's 2x10 side insets - sits at about
284px of draw. That is inferred from the bracket, not measured; if the art
degrades in game the floor moves up, and nothing else has to change.

**Why the title can push past the ceiling.** `PaintTitleText` draws the
title in `DefaultFont32` at a fixed 80px indent with no alignment control,
and the exit button sits at a fixed inset from the right, so window width is
the only lever a dialog has over its own title. `ApiAccessDialog` learned
this the expensive way: at 480 its title clipped three characters mid-word.
That is now a rule rather than a magic number - the caller measures its
title in the face Blish paints it in and the width floor becomes
`TitleTextIndent + titleWidth + TitleRightReserve`, which reproduces the 560
that dialog needs without anyone writing 560 down.

**The balanced wrap.** Wrapping at the ceiling and stopping there gives a
full first line and a stub second one. The width is instead the narrowest
that still reaches the same line count, found by binary search over
`TextWrapMath.Wrap`: greedy wrapping never yields fewer lines as the width
shrinks, so the predicate is monotone and the search is exact. It runs once
per `Show`, about ten wraps of one message, and never on a render path.

**One seat for a pair.** A confirm and a cancel in the same row share one
width, the larger of what the two labels need. The first cut kept the two
floor widths the pre-sizing dialog happened to ship, 100 for the confirm
seat and 70 for the cancel, and that inverted the relationship between the
labels: "Clear" floored up to 100 sat beside a longer "Cancel" that reached
only 70, so the shorter word rendered as the wider button. Invisible while
every dialog was 560px of mostly empty space; reported on sight once the
boxes went tight. The single floor that replaced them is 80, the width of
the Settings save bar's "Save", so a verb in a dialog is never narrower than
the same verb on a tab. A lone acknowledgement button is not in a pair and
is not stretched to one: it takes its own label or the floor.

**Order of operations.** Balance within the preferred wrap ceiling; raise to
the largest of the button row, the title and the width floor; clamp to what
the screen can actually hold. The screen wins last, which is why
`MaxContentWidth` returns the screen's hard ceiling and applies no preferred
clamp of its own - a button row wider than the preferred width must be able
to grow the box, and only the screen may refuse it.


## S2. Services Q-Z: relocated design narrative

Design narrative moved out of doc comments in `Services/` (files whose
names begin Q-Z) under CLAUDE.md's comment rule: the invariant a caller can
violate stays at the member, the derivation that explains it lives here.
Each subsection names the class and member it came from, so a reader who
arrives from the citation lands on the same argument the comment used to
carry.

### S2.1 Sell-side economics for a batch

**`SellSideEconomics.ApplyBatchSellSideEconomics`.** The batch rollup is
gw2efficiency parity work (KNOWN-ISSUES #25), and it diverges from gw2e's
own multi-item rollup - the `o()` function in the live app bundle, see
[`docs/research/m37-r2-batch-economics.md`](research/m37-r2-batch-economics.md)
sections 1.2 and 4.1 - in three deliberate ways:

1. **No craft-vs-buy filter.** Any requested root with a live TP sell
   price contributes its own `SellableQuantity`/`NetSaleValue`/
   `CraftingProfit` regardless of whether the solver bought or crafted it.
   This matches the module's already-shipped single-item
   `ApplySellSideEconomics` semantics, which has never filtered by
   craft-vs-buy - a flip/arbitrage number is still meaningful - and the
   research report's explicit recommendation (4.1.1) *not* to adopt gw2e's
   `craft === true` filter. Pinned by
   `MultiItemPlanTests.GenerateStructuredAsync_MultiItem_OneRootBoughtButTradable_IncludedInSum`.
2. **A crafted root with no live TP sell price contributes nothing** -
   excluded entirely, its revenue and its own craft cost dropping out
   together - rather than gw2e's silent "-cost" drag for an untradable
   crafted root.
3. **A single profit basis** (instant-sell/buy-order, via `SellInstant`),
   matching the single-item row. gw2e always shows a second
   sell-listing-basis figure this module has never surfaced.

**Why `MaterialOpportunityCost` is a single batch-wide sum.** `Reduce`
still runs on the entire wrapper tree before `Solve` ever picks buy vs
craft per root (see `GenerateStructuredMultiAsync`'s step ordering), so the
figure is one sum over the merged `UsedMaterials` list and is not scoped
down to the roots that end up contributing to the sellable totals. What
makes that safe is that `UsedMaterials` is itself decision-aware:
`InventoryReducer.Reduce`'s `zeroOwnedDecisions` guide, fed by a throwaway
zero-owned `Solve()` on the same unreduced tree, means a root the solver
decides to buy no longer has its never-crafted subtree's owned ingredient
stock recorded as "used" at all, so there is nothing left to deduct from
`CraftingProfit` for that root. The single-item path was updated to the
same guided reduction, so both behave identically. Pinned by
`MultiItemPlanTests.GenerateStructuredAsync_MultiItem_ValuedMode_MixedBuyCraftBatch_MaterialOpportunityCostNullForBoughtRootOwnedIngredient`.

**`SellSideEconomics.PerItemEconomics`.** `itemId` is passed explicitly
rather than read from `itemRoot.Id`. Both call sites already guarantee
`itemRoot.Id == itemId` by construction (`RecipeService.BuildTreeAsync` and
`BuildMultiItemTreeAsync`), but keeping it explicit means the struct never
silently depends on that invariant holding.

**Full history:** KNOWN-ISSUES item 25;
[`docs/research/m37-r2-batch-economics.md`](research/m37-r2-batch-economics.md).

### S2.2 Crafting Ranker: cascade, ramp, weights, cache

**`RankerPriorityCascade` - what the ledger tracks.** "What a plan takes
from you" is several different things, and the ledger keeps them apart:

- materials, from `CraftingPlanResult.UsedMaterials` - the solver's own
  post-solve consumption record, which already reflects every buy-vs-craft
  decision, including decisions caused by what was owned;
- currencies and coin, netted by the cascade itself, because the solver
  never consults the wallet (see `AccountCurrencyIndex`);
- daily-cooldown crafting actions, which are capped per **account**, so two
  items needing the same gated ingredient queue rather than run in
  parallel.

**`RankerReadinessRamp` - why the ramp is deep rather than pastel.** A
naive red-to-green ramp peaks at a bright yellow around the midpoint, and
white on bright yellow is illegible. WCAG's 4.5:1 floor for white
(`#FFFFFF`) means every colour on the ramp has to keep its relative
luminance at or under `1.05 / 4.5 - 0.05 = 0.1833`, which is a deep,
saturated ramp and is why the three anchors are dark for their hues.

Interpolation is in OKLCh, not in sRGB. An sRGB lerp between two saturated
hues cuts through the interior of the colour solid and desaturates on the
way - red to green goes through brown - because sRGB's axes are not
perceptual. OKLab (Bjorn Ottosson, 2020) is a perceptual space whose polar
form OKLCh separates lightness, chroma and hue, so walking the hue angle
keeps chroma up all the way across and the intermediate colours stay orange
and olive rather than mud. The 25% sample is `(159, 76, 0)`, a real orange,
which is the measurement that says the space is doing its job.

**`RankerReadinessRamp.Track` - the second contrast obligation.** The track
constant was first chosen against white alone. At `Rgb(38, 36, 34)` it
scored 1.05:1 against the panel behind it, while the panel's own texture
varies by 1.076:1 - so the track was literally less distinguishable from
the surface than the surface is from itself. Measured in game at 3440x1440.
Darker is the only direction that serves both obligations, and the current
value sits at 1.32:1 against the panel with pure black as the 1.42:1
ceiling.

**`RankerReadinessWeights` - the per-gate argument.** The weights are not
derived from each gate's magnitude; deriving them that way sounds
principled and is the exchange-rate trap in disguise. They are judgement
calls about substitutability, which is a property the game itself decides:

- A daily reset cannot be bought at any price. It is the only barrier with
  no substitute, so it takes the largest share.
- The bill is the bulk of the work and the gate measured most precisely, by
  the real solver at real prices. Equal claim on precision grounds; no
  better claim than time on difficulty grounds. It is a copper figure, so a
  currency the plan pays enters it at the decision valuation (section 8.3)
  and an unvalued one enters neither half.
- The currencies gate measures something else: what the WALLET covers of
  what the plan still needs, as within-currency ratios. The materials gate
  measures the bill, this one measures the ability to pay it, and a row can
  owe a large Karma cost it can already afford. Each point carries less
  information than a coin point, which is why it is weighted below
  materials - not because currencies matter less.
- A discipline is a hard wall - you cannot craft at all without it - but a
  short one next to a legendary's materials bill, and usually either
  satisfied already or cheap to satisfy. Non-zero because it is real; small
  because it is short.
- A recipe unlock sits on the same substitutability rung as a discipline: a
  hard wall, but most recipes are purchasable sheets or cheap unlocks, so it
  takes the disciplines weight rather than inventing a new tier. First call,
  reviewable like the others.

**Barter items are not a sixth gate - the measured size of that gap.** A
barter item is the account-bound token a vendor takes in place of coin.
`CraftingPlan.BarterItemCosts` holds them, `TotalCoinCost` excludes them
because they carry no Trading Post price to fold in, and no gate scores
them. A row that pays one is ranked as though that cost is not there.

MEASURED over the shipped corpus, planning 30 legendaries and gifts through
the real pipeline against `ref/vendor_offers.json`: 15 of the 30 pay at
least one barter item cost, over 50 barter lines and 26 distinct items.
Aurora pays 11 distinct items. Gift of Dedication and Infinite Trebuchet
Blueprint pay 4 each with no coin bill at all. Only 5 of the 26 items carry
a curated decision value in `Models/BarterItemDecisionDefaults.cs`, so
valuing the class into Materials would leave most of the bill uncounted.

The gap is real and undisclosed. A sixth gate was written for it and taken
back out: the headline is five gates by decision. Anyone reopening this
starts from these numbers rather than measuring them again.

**`RankerResultCache` - why two sets.** The two comparison modes answer
different questions about the same rows, and a row's answer under one says
nothing about its answer under the other. Keeping only the last mode's set
made every toggle a full recompute, including a toggle straight back to
numbers the session had already paid for (decided 2026-08-27).

### S2.3 Column and section geometry (Q-Z)

**`RecipesColumnMath` - why discipline is a column.** The discipline used to
be a second `Caption` line *under* the recipe name, which forced the
section to carry two row heights and put a name and its discipline on
different reading lines. As a column, every recipe row is one line at
`PlanContentHeightMath.RecipeRowHeight`.

**`ShoppingColumnMath` - why the bands are distributed.** Distributing the
bands over equal tracks rather than packing them against the panel's right
edge is what stops a short item name being stranded far left with the
middle of the row empty. Each header centres over its band rather than
sharing an edge with it; `JustifiedColumnTracks` carries the argument for
why a shared edge is not enough.

**`ShoppingColumnMath` - why Amount left the track grid.** The table read
Item, Source, Amount, Each, Total, so a player scanning a shopping list met
the count in the middle of the row while Used Materials and the Account
Snapshot both put it first. Amount now heads the row in the fixed band on
its left inset that `AmountLedRowMath` owns, and three tracks carry Source,
Each and Total over what the Item column does not need. The plan tab's own
Shopping List and the popout are the same renderer, so the two could not
have been changed apart.

**`SnapshotHeaderLayout` - what the shared row buys and costs.** The header
used five sparse rows to say what four can, and the widest of them - the
search row - was empty for everything right of the content-type dropdown.
Sharing that row halves the width the source-filter run has to flow into,
so a roster that used to fit inside the 4-row cap can wrap past it and hide
filters behind a scrollbar: a third of the filter set for 38px of header.
That is why the sharing is conditional on the whole run fitting in one row,
and why the fallback is exactly the full-width row the flow had before.

**`SummarySectionLayoutMath` - why it is its own class.** Its role is the
same kind of thing `Services/PlanContentHeightMath.cs` and
`Services/PlanRelayoutMath.cs` already do for every other section, and it
is deliberately kept out of both: they are shared infrastructure several
other sections' row builders depend on, and both are pinned by expensive
evidence (see
[`docs/KNOWN-ISSUES.md`](KNOWN-ISSUES.md#policy-code-pinned-by-expensive-evidence)) -
off-limits for the broader fold-back this class's existence sidesteps.
KNOWN-ISSUES #46 carries the original rationale.

**`TreeChipStripLayout` - what the slot held before, and why zero hides.**
The slot the state chips occupy used to hold a grey "Recipe Tree:" caption:
small *and* grey, labelling five buttons whose own verbs and tooltips
already said what they act on. Real information replaces a caption that
named nothing. A chip is hidden entirely at zero rather than shown reading
zero because a standing "Overrides: 0" spends attention on the absence of a
thing, and a permanently-disabled clear button beside it invites "why is
this disabled?".

**`TreeToolbarRowLayout` - why the button widths live in the class.** A
width that can only be read off a `PlaceRight` argument is a width no test
can assert the boundary cases against without re-typing it, and a re-typed
width is one a later rename silently invalidates.

**`TreeCostColumnMath` - what the column looked like before.** It used to
right-align one ragged run per row: a gold/silver/copper row and a currency
row both ended at the same x but shared no interior alignment, so no two
coin icons in the whole tree lined up vertically. Scanning the whole tree
once per render pass costs one walk of an already-materialised tree and
buys a column that never shifts under the user; the node count the section
header shows rides on the same walk for the same stability reason.

**`UiSpacing` - the coincidences on record.** 8px also ships as
`LogToolbarLayout.Gap` and `LogRowLayout.RightPad`, and 20px as both
`SettingsFormLayout.SectionGap` (vertical, between section blocks) and
`TreeToolbarRowLayout.GroupGap` (horizontal, between button groups). Those
are coincidences, they stay where they are, and coupling them would make a
deliberate change to one silently move the others.

### S2.4 Tooltip text: wrap seams and scope vocabulary

**`ShoppingRowTooltipFormatter.BuildCurrencyLines` - why every line names
its scope.** The cost on a shopping-row currency line is that *row's* own
total (`cc.Amount`, one `PlanStep`'s `VendorCurrencyCosts`), never the
whole plan's requirement for that currency id, and the holding beside it
is the whole account's wallet balance. Without both scopes stated, two
shopping rows drawing on the same wallet currency - Karma split across two
vendor rows, say - can each independently read as "fully covered" and
double-count the one balance. That is the same misreading class
`DecisionPillPlanner`'s plan-scope `HAVE {have}/{planTotal} TOTAL` pill
(`AppendCurrencyOwnershipPill`) exists to avoid.

The lines used to carry that scope as the shouted markers "THIS ROW" and
"(wallet N)". They now say it in a sentence - "this row costs 3660. You
have 2812 in your wallet and need 848 more." - because these lines live in
the module's own second tooltip box, where the standing rule is that text
reads as prose. The scope claim is what must survive a rewording; the
markers themselves are not sacred.

**`TooltipLayoutMath.ItemTooltipMaxContentWidth` - the corpus.** The cap is
derived from the game's own break decisions rather than from a game-pixel
cap converted by a scale factor, because a mean font ratio hides a real
per-string spread (0.99x to 1.03x at Menomonia 14): `LetterSpacing = -1`
tightens tracking on a face whose glyph boxes are already wider than the
game's, so how a given string lands depends on its letter count as much as
its length. Each live capture that wraps a paragraph pins the cap twice -
it must be at least the width of the line the game *kept whole*, and below
that line plus the word the game *pushed down*. Measured through this face,
in this constant's units:

- Gift of Twilight 19648: 320 kept / 381 with "Twilight." pushed down; its
  "Made by combining these items in the Mystic Forge:" line, 359, stays
  whole.
- eyes-of-kormir 83103: 354 kept / 415 with "because"; 357 kept / 400 with
  "under".
- heart-of-destroyer 67017: 330 kept / 408 with "Bloodstone"; 372 kept /
  442 with "Destroyer".
- fury-scorched 86967: 406 kept / 430 with "for" - the one outlier.

Every constraint but fury's intersects at [372, 381); 376 is its midpoint,
so no decision sits within 4px of flipping. Fury's kept line would need a
cap of 406+, which would un-wrap Gift of Twilight *and* eyes' second line,
so it loses 1 constraint to 5.

**`TooltipTextFormat.LineBudgetChars` - why 71, not the shipped 75.** Every
prose string of 55 characters or more that this module builds (73 of them,
swept out of `Services/` and `Views/`) was measured against the installed
Menomonia 14 XNB with MonoGame.Extended's own advance / `XOffset+Width`
rule - the same parse behind
[`docs/research/minimum-window-width.md`](research/minimum-window-width.md).
They average 7.03px per character, so 500px is 71 characters, not the 76 the
original 6.5px/char estimate assumed. Per-string the spread is
6.7 to 7.5px/char, so prose at the wide end still crosses 500px inside a
71-character line; Blish's own space wrap takes those, which costs a break
it would have made anyway and never loses text. The one case only this seam
handles - a single token wider than the cap, which Blish's wrapper will not
split - is hard-cut by `TextWrapMath` before the budget matters.

The budget is a **character** count, not pixels, because a tooltip string
is composed in `Services/`, far from any font; the alternative - threading a
measured `Func<string, int>` down from `Views/Rendering/` - would put a
Blish dependency on the very seam the class exists to keep Blish-free.

**`TreeRowTooltipComposer.BuildExtraTooltipContent`.** The returned content
is computed once and reused verbatim by `RenderTreeNode`'s
`extraTooltipLines`. There was once a second entry point returning
pre-wrapped strings; nothing called it and it is gone.

**`ValueDetailTooltipBuilder`.** The hover template is duplicated verbatim
from gw2efficiency's own crafting-pill hover, and the class is kept
Blish-free - unlike `TreeSectionController`, which only calls it and assigns
the result to `BasicTooltipText` - so the text-building logic is directly
unit-testable, matching this repo's established pattern for tree-rendering
logic (`DecisionPillPlanner`, `CoinSegmentMath`, and the rest). The
divergence it surfaces can be an unpriceable descendant's own divergence
rolled up recursively; see `DecisionValue`'s own doc comment.

### S2.5 Account-snapshot concurrency and search

**`SnapshotCommitGate` - what `SnapshotEpochGuard` alone left open.** The
original KNOWN-ISSUES #31/31a-F1 fix captured `myEpoch` before a snapshot
fetch's await and re-checked it afterwards against a bare
`volatile int _snapshotEpoch`, with the field commit that follows (write
`_currentSnapshot`/`_pendingSnapshot`/`_snapshotDirty`, save to disk) as
several more unguarded instructions after that check. `Module.ClearCache`
bumps the same epoch and nulls those same fields with no synchronization of
its own. The check and the commit were never atomic with respect to
`ClearCache` - just narrowed from "the whole fetch" down to "the few
instructions between the check and the last field write" - so a Clear Cache
landing in that gap could still resurrect a just-cleared snapshot, or leave
the three fields in a combination that never legitimately occurs. The gate
closes the gap for real by putting the bump and the re-check under one lock.

**`SnapshotRefreshSlot` - what the check-then-set gate cost.** Three threads
reach `Module`'s two refresh entry points - `LoadAsync` on a ThreadPool
task, `Update()` on the main thread, and `OnSubtokenUpdated` on a thread the
module does not control - and both entry points used to gate on a
check-then-set over a `volatile bool`. Volatile makes a write *visible*; it
does not make check-then-set *atomic*, so two entrants could both get past
it. Each would then run the same three-statement sequence (cancel the live
source, dispose it, assign a fresh one) and each could dispose the source
the other had just published, after which the loser's own
`_refreshCts.Token` read threw `ObjectDisposedException` - or
`NullReferenceException`, if a Clear Cache click nulled the field in the
same window. `Module`'s generic catch reported that as "refresh failed" and
armed a 60-second retry backoff for a call that never reached the network.

**`SnapshotSearchResultBuilder.ShortQueryCharacterHint` - why the hint
exists.** The `MinCharacterSearchLength` hold-back is deliberate but
invisible: a one-letter query that a character's name does contain looks
like a plain no-results, so the user reads the tab as broken rather than as
waiting for a second letter.

**`SnapshotSearchResultBuilder.BuildItemRows` - inputs and cost.**
`itemsById` is the already-deduped itemId -> representative-entry map (see
`BuildRepresentativeIndex`); the method never re-scans the raw per-source
entry list itself, so it stays cheap to call on every keystroke as long as
the caller builds the map once per snapshot rather than once per call.
Character matching costs a full source walk for every item whose name does
not match, where a name-only search could skip straight past it; that is
bounded above by the empty-search rebuild, which already walks every source
of every item. The match is against character names only - storage-location
labels stay unmatched (Feature 1 Open Question 2, resolved in favour of
source-label matching). A row surfaced by a character match reports the
account-wide total rather than the matched character's share, so the total
keeps meaning the same thing on every row in the list.

### S2.6 Receipt captions and the multi-item tree wrapper

**`ReceiptCaptionHelper` - where the stacked shape comes from.** The stack
this helper detects is produced by one branch of
`CraftingTreeBuilder.BuildNode`: `componentLeaves != null &&
wantsReferenceBranch`. That branch synthesizes the cost-component leaves,
appends the reference branch's own recipe ingredients after them, and sets
`node.IsReferenceBranch` - which is why "IsReferenceBranch and the first
child is a cost component" identifies the case from the node alone, with no
new model field. The helper is Blish-free by design so it can be exercised
by a real test over plain `CraftingTreeNode` objects, independently of the
`Views/Rendering` pass that consumes it. The caution about never touching
`Children` is not stylistic: row heights flow through
`PlanContentHeightMath`'s tree arm, which counts exactly
`node.Children.Count` rows per level, so a caption rendered as an extra
row - rather than as an extra tooltip line on an existing child's row -
would desync a height the view assigns synchronously.

**`RecipeService.BuildMultiItemTreeAsync` - why a synthetic wrapper.** For
2+ items the per-item trees are wrapped under a synthetic root `RecipeNode`
the same way gw2efficiency's frontend does for its own Calculator (see
[`docs/gw2e-parity-spec.md`](gw2e-parity-spec.md)): a reserved-id,
never-rendered "recipe" whose `Ingredients` are the N real item trees, each
already carrying its own requested amount as its own `Quantity` (set by
`BuildTreeAsync` itself, exactly like an ordinary recipe ingredient's
quantity). Feeding that wrapper through the unmodified
`PlanSolver`/`InventoryReducer`/`CraftingTreeBuilder` pipeline is what gives
merged shopping-list, steps and currency totals for free, via the existing
per-item-id aggregation in `PlanSolver.Collect`'s `AggregateStep`: no
multi-item-specific solver logic exists, or is needed. The single-entry
short-circuit echoes gw2e's own `if (r.length === 1) return r[0]`.

### S2.7 Recipe-tree row identity

**`TreeRowIdentity` - why a shared `NodeId` is not enough.**
`RecipeNodeIds` gives a real recipe node a stable pre-order id, so there the
id does fix the item for the row's life. A vendor cost-component leaf's id
is `CraftingTreeBuilder.SyntheticComponentNodeId(parentNodeId,
componentIndex)` - the leaf's *position* in the chosen offer's cost lines -
while its name, icon and rarity come from that line's own `ItemId`. A
re-solve that picks a different offer of the same shape (`{item, currency}`
becoming `{other item, currency}`) keeps every id and every structural fact
and changes only which items the lines name, so an identity-blind refresh
would repaint one item's quantity, cost cell and tooltip under another
item's name and icon.

### S2.8 The re-solve status line

**`StatusText.ForOverrideResolve` - why the count left the line.** The line
used to carry the standing override count - "Decisions updated (3
override(s))" - which is a different kind of fact. How many decisions you
have overridden is the plan's *state*, true until you change it; this line
says what just happened and is replaced by the next thing that does. The two
are not connected, and a line that mixed them made the count vanish the
moment anything else happened. The count lives in the top strip's Overrides
chip now, where it persists and can be acted on.

### S2.9 Window placement and the measured width floor

**`WindowPlacement` - why the arithmetic is split out.** It is split out of
`Views/ResizableTabbedWindow` on the same terms as `WindowSizing` and
`PanelChromeMath`: the arithmetic is the part that has to be pinned, and a
Blish control cannot be constructed in a Blish-free test.

**What Blish's own clamp does and does not do.** `WindowBase2.Show` reads
the persisted position and applies `Clamp(x, 0, SpriteScreen.Width - 64)`
per axis (BlishHUD 1.3.0, decompiled). Nothing on that path consults the
window's size, so a position saved against a wide client leaves an arbitrary
amount of the window's right-hand side - cost column, Generate button,
resize grip - past the edge of a narrower one, with no way to drag it back.
A restored *size* gets no clamp at all on that same path, which is what
`ClampExtent` exists for.

**`WindowSizing.MinWindowWidth` - the term-by-term chain.** Measured at
Menomonia 16 against the installed XNBs
([`docs/research/minimum-window-width.md`](research/minimum-window-width.md)
section 9 reproduces the method and every anchor figure of that report's own
1478-era derivation):

```
 629  widestNameEnd  = nameX(14) 394 + "429750x " 69 + name 166
 +24  the designed clearance before the decision column at the deepest row
+231  TreePillColumnWidth
+335  cost column: 181 worst-digit six-digit-gold coin run
                   + 154 widest two-currency vendor run
 +25  action column: TreeActionColumnGap 4 + TreeActionColumnWidth 21
  +8  TableRightMargin
---- 1252  tab panel
+126  WindowToTabPanelChrome
==== 1378
```

The decision column and the action column together are the 256 the decision
column alone used to hold, so this total is unchanged by the action column's
arrival. Why 231 is the right figure for the decision column now: V.33.

1378, not the 1232 the like-for-like depth-14 arithmetic gives on its own:
1232 accepts that a row combining a forced-craft dust chain with a vendor
currency run ellipsizes, and that trade was declined: the module is
designed against a minimum resolution of 1920x1080, and a smaller minimum
window size produces cramped renders. The +154
rider is what buys "a two-currency vendor run always fits at the floor".

Down from 1478, which fitted the depth-23 "+24 Agony Infusion" chain
untruncated. That chain now ellipsizes from depth 20 - six levels past the
deepest realistic plan, and exactly the idiom of record everywhere else in
the view (ellipsis, full name on the tooltip).

The other contributor to this floor is the controls row, which is subsumed:
its widest arrangement is the "Value Own Materials" checkbox at x=350 (its
label measures 145px at Blish's own Font14, plus the box) clearing the
right-anchored 120px Generate Plan button and `WindowToTabPanelChrome`'s
trailing padding - under 700px all told, half of what the tree needs.

### S2.10 Wiki link launch

**`WikiLinkLauncher` - the first external-URL launch.** This is the module's
first launch of an external URL, a deliberate decision. The
try/catch exists because ShellExecute can throw for reasons outside the
module's control - `Win32Exception` for no registered URL handler, a
locked-down environment, and so on. The `Task.Run` offload was a later
fix-pass: `ShellExecuteEx` blocks the calling thread until the shell hands
the URL off, and a cold browser start, DDE negotiation, or a "choose an app"
prompt can stall that call for hundreds of milliseconds to seconds, freezing
the whole overlay - scroll and relayout included - for as long as it runs.

**Why the browser opens behind the game.** Windows lets a process raise a
window to the foreground only under the conditions listed in the Remarks of
[`SetForegroundWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow):
the calling process is the foreground process, was started by the foreground
process, received the last input event, is being debugged, there is no
foreground process at all, or the foreground lock time-out has expired.
[`AllowSetForegroundWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow)
passes that right to another process and is documented to fail when the
caller does not hold it: "The function will fail if the calling process
cannot set the foreground window."

Blish HUD cannot hold it while the player is playing, for three separate
reasons.

- The overlay window is click-through and stays that way.
  `WindowUtil.SetWindowParam` is the only `GWL_EXSTYLE` write in Blish HUD
  and it ORs `WS_EX_TRANSPARENT` in on every call, so nothing ever clears
  the bit. Read off the running overlay, its ex-style is `0x000900A0`
  (`WS_EX_LAYERED | WS_EX_COMPOSITED | WS_EX_TOOLWINDOW |
  WS_EX_TRANSPARENT`). A click-through window is never hit-tested, so a
  click can never activate it.
- Blish HUD never activates itself. Its only `SetForegroundWindow` call site
  is `Gw2InstanceIntegration.FocusGw2`, which targets the Guild Wars 2
  window, and nothing in Blish core calls it. `WindowUtil.UpdateOverlay`
  moves the overlay in the Z order with `SetWindowPos`, which does not
  activate, and demotes it below Guild Wars 2 whenever Guild Wars 2 is
  active.
- The right-click is never delivered to Blish HUD. Blish reads mouse and
  keyboard input from `WH_MOUSE_LL` and `WH_KEYBOARD_LL` global hooks while
  Windows routes the input itself to Guild Wars 2. Guild Wars 2 is therefore
  both the foreground process and the process that received the last input
  event. Focusing a Blish text box does not change this: it registers a
  delegate and swallows keystrokes at the hook, and never activates a window.

The overlay window is not barred from the foreground outright. It carries no
`WS_EX_NOACTIVATE`, and polling `GetForegroundWindow` caught it holding the
foreground twice while the player was switching between other applications.
That state cannot be the one a wiki click arrives in, though.
`InputService` enables the hooks on `Gw2AcquiredFocus` and disables them on
`Gw2LostFocus`, so the module can only receive a right-click while Guild
Wars 2 has focus, and while Guild Wars 2 has focus it is the foreground
process. The two states exclude each other.

That leaves the foreground lock time-out as the only condition the module
can ever satisfy, and only while nobody is playing. Measured on the
maintainer's machine, where `HKCU\Control Panel\Desktop\ForegroundLockTimeout`
holds its 200000 ms default: `AllowSetForegroundWindow(ASFW_ANY)` called
from a background process returned FALSE with `GetLastError` 5
(`ERROR_ACCESS_DENIED`) at 0 ms, 15 ms, 281 ms and 469 ms since the last
user input, and TRUE at 265953 ms. The grant is not dead code. It succeeds,
and the browser does come forward, once the player has been idle longer than
the time-out.

One other Blish module does force the browser forward, using
`AttachThreadInput` against the foreground thread followed by
`BringWindowToTop`. This module will not do that: it hijacks the input queue
of a process it does not own.

So `WikiLinkLauncher.Launch` records which of the two cases happened and
reports a `WikiLaunchOutcome`. `Module.DrainWikiLaunchNotice` raises a
screen notification for `ForegroundRefused` and `Failed` only. When the grant
succeeded the browser is already in front of the player, and a caption
telling them so would be noise.

### S2.11 Recipe corpus refresh

**`RecipeCorpusRefresher` - the case that motivates it.** Recipe 14025's
rift-essence ingredients turned from items into wallet currencies without
the recipe id changing (KNOWN-ISSUES #48), and `RecipeCorpusVerifier` cannot
see such a change because it only ever fetches ids the corpus lacks. The one
comparison the refresher does make - is the fetched row identical to the
seed's? - exists to keep the overlay from becoming a 10 MB duplicate of the
shipped seed for no gain.

---

## T. Tests and tools: relocated design narrative

Derivations, histories and investigations moved out of over-length XML doc
comments under `tools/` and `tests/`. Section 9 above describes the data
pipeline these tools feed; this section is the level below it - the wiki
shapes, the incidents, and the coverage gaps that explain why the offline
tools and a handful of test fixtures are built the way they are. Each
comment they came from keeps the part a caller can violate and points
here for the rest.

### T.1 `HomesteadTierResolver`: the parity shape and the live probe

The merchant-name test matches gw2efficiency's own `cheapestTree.ts`
matching shape (`docs/research/m37-r1-homestead.md` section 1.2): a row
participates in tier gating only when its merchant name contains the
literal substring "Homestead Refinement" (gw2e:
`tree.merchant.name.includes('Homestead Refinement')`), which catches all
three station pages ("...-Farm", "...-Lumber Mill", "...-Metal Forge") the
same way a plain `.includes()` would.

The tier encoding was confirmed live, by a direct SMW ask probe against
Homestead Refinement-Metal Forge: a tier-0 row's "Has requirement"
printout returns an empty array, and a tier-1 or tier-2 row returns
exactly one `_txt` value, "one [[Homestead Upgrade: ...]]" or "two
[[Homestead Upgrade: ...]]" respectively. That is not an inference from
the rendered page - the wiki's `{{vendor table row}}` template parameter
is literally `requirement=one [[...]]` / `requirement=two [[...]]`.

The class is separate from `ConvertToOffer` so this pure resolution logic
is covered by direct unit tests without a `Gw2ApiHelper`/`HttpClient`
fixture.

### T.2 `TemporaryTemplateParser`: the wikitext shapes that were observed

Every shape below was read off a live page through the wiki mirror
(`api.php?action=parse&prop=wikitext`), not inferred from the template's
documentation.

- **Template name casing varies in the wild.** Both `{{Temporary|...}}`
  and `{{temporary|...}}` appear verbatim on real pages ("Mad King's
  Realm" uses the lowercase form), which is why the match is
  case-insensitive.
- **Parameter name varies too.** The six recurring festival vendor NPC
  pages this module cares about all use `seasonal=` - for example "Candy
  Corn Vendor (Weekly)":
  `{{Temporary|release=Shadow of the Mad King 2019|seasonal=Halloween}}`.
  A minority of vendor NPC pages use `event=` for the identical purpose:
  confirmed on "Trader" (Bazaar of the Four Winds),
  `{{Temporary|release=Bazaar of the Four Winds|event=Festival of the
  Four Winds}}`, and on the non-festival one-off vendors "Consortium
  Trader (Fractal Rush)" and "Starter Equipment Vendor",
  `{{temporary|event=Fractal Rush}}` / `{{temporary|event=Fractal
  Incursion}}`. The parser treats both parameters identically; it is
  `Gw2Constants.ResolveSeasonalFestivalKey`, not the parser, that decides
  whether an extracted value is one of the six known festivals or an
  unrecognized one-off event or release.
- **A page can carry `{{Temporary|release=...}}` with neither parameter**
  - a one-off, non-festival, non-`event` release vendor. That returns
  null, the same as a page with no `{{Temporary}}` template at all.
- **One shape has never been observed and is therefore untested:** the
  extracted value is not normalized against wiki markup, so
  `seasonal=[[Halloween]]` would extract the literal "[[Halloween]]".
  `Gw2Constants.ResolveSeasonalFestivalKey` correctly leaves that
  untagged with a warning rather than fuzzy-matching or guessing (the
  never-guess repo invariant), but it is worth knowing about if a future
  wiki edit introduces wikilink-wrapped parameter values.

The template regex matches up to the first literal `}` via a negated
character class rather than up to the first `}}`, so a single stray `}`
inside a real template's parameter list would make that template
unmatchable rather than truncate its captured body early. Not observed on
any real page, and left unhardened for that reason.

### T.3 `VendorOfferDiff`: why a raw id diff is useless

`git diff` on `ref/vendor_offers.json` reports "1 insertion(+), 1
deletion(-)" on a 14.8MB single line: the entire dataset replaced as one
indivisible hunk. A reviewer of a `data(vendor):` commit cannot see what
changed.

The naive improvement - list the offerIds that appeared and disappeared -
is almost as useless, because `offerId` is a SHA-256 over the offer's
whole content. Change one price and the row does not "change": it
vanishes and a different hash appears, so a raw added/removed pair list
turns every repricing into two unrelated-looking hex strings.

Re-pairing by (merchant, output item) is what the hash does not preserve
but a human reads instantly. The converse case matters more, because a
`VendorOfferHasher` hash-format change does it to every row at once: a row
whose content is unchanged but whose id is not has not been repriced. One
such migration reported 48,750 of 53,544 rows as repriced, each printing
an identical before and after, and cross-paired rows differing only in
`OutputCount` into price moves that never happened. Counting those as
rehashed rather than listing them is what keeps the report readable.

### T.4 `Program.MergeIntoBaseline`: why an incomplete batch never replaces

Wholesale replacement of a merchant's rows on the strength of a fresh
scrape is correct only when the fresh set is complete. It has silently
deleted shipped offers before, when a pass returned rows with
`GameId <= 0` that the GameId filter then dropped. Hence the
`merchantsWithSkippedRows` opt-out: those merchants union instead of
replace. A possibly-stale baseline row surviving an extra run is visible
and fixable; a silent deletion is neither.

One kind of stale row is not fixable by an extra run, though, and the
union has to drop it: a second price for a sale this pass already
priced. The wiki writes a vendor's coin price in gold, silver or copper,
so a run that reads the unit differently records a different number of
copper for the same sale. Both rows then ship and the solver buys at the
lower one. `ComputeSameSaleKey` names a sale by its merchant, what it
hands over, and what it charges other than coin; a protected merchant's
baseline row goes when this pass produced a row for that sale and no row
at that price. A baseline row this pass produced no row for is still
kept, and so is one whose price this pass agrees with. The rule never
collapses two rows from the same pass, so a vendor that really does sell
one item at two prices keeps both.

`merchantsWithSkippedRows` also never empties for a pass that reads the
wiki cache rather than the wiki. `--resolve-item-currencies-only` builds
the set from cache rows with `GameId <= 0`, and those rows are in the
cache until a fresh scrape replaces them, so the same ~880 merchants are
protected on every such run. Their rows are the ones that need the rule
above; the "re-run once every row resolves a game id" advice in the
merge's own warning only reaches merchants a live scrape can fix.

### T.5 `Program.ResolveSeasonalFestivalValuesAsync`: opt-in, budgeted, page-keyed

**Why opt-in.** Every other field on `WikiVendorResult` comes from SMW
"ask" printouts already fetched by
`QueryVendorItemsAsync`/`ResolveItemGameIdsAsync`. There is no Semantic
MediaWiki property for a page's `{{Temporary}}` template, so unioning a
distinct-page wikitext parse into every full refresh would add one HTTP
request per distinct vendor page - thousands, for a from-scratch scrape -
on top of the existing two-pass budget, silently changing the cost and
time profile of the default `./tools/refresh-vendor-data.sh` workflow. A
developer who wants full coverage passes `--tag-seasonal-festivals`
explicitly.

**Why the cache is keyed by stripped page title.**
`WikiVendorResult.PageName` is the SMW subject key of the vendor's "Sells
item" SUBOBJECT, not the vendor's own wiki page title - confirmed live
(`api.php?action=ask` against `[[Has vendor::Candy Corn Vendor
(Weekly)]]`): every row's subject is "Candy Corn Vendor (Weekly)#vendor1",
"...#vendor2", and so on, one subobject per sold item. The fetchable page
title is everything before the first `#` (`StripSubobjectSuffix`).
Caching and fetching by the stripped title is also what keeps the pass
cheap: one wikitext fetch per distinct VENDOR, not per sold item.

**Why the budget is self-healing rather than fatal.** An over-budget run
fetches up to the budget, saves the cache, and logs how many pages remain.
The next run's `toFetch` list is smaller, so repeated runs converge on
full coverage instead of every run past the first throwing on the same
unmet budget.

**Why the budget is scoped to this run's query.** `wikiResults` at the
caller's call site is the FULL merged `wiki_vendor_cache.json` (Step 2's
`MergeWikiCache` union), not just this run's query. Scoping the fetch
budget to it meant a narrow `--query` on a real dev-machine cache
(thousands of distinct vendor pages) computed thousands of "uncached"
pages, exceeded `--max-seasonal-pages`, and threw `SafetyLimitException`
BEFORE Steps 4-6 ever wrote output, discarding the scoped run's
already-completed live work. `queryScopedResults` scopes the budget to the
pages this run's `--query` actually returned, and is null for
`--resolve-item-currencies-only`, which has no `--query` and processes the
whole cache by design. The cache-apply loop still runs over the full
`wikiResults` either way, since applying an already-cached tag is a
dictionary lookup, not a fetch.

### T.6 `WikiSmwClient.FetchWikitextAsync`: the redirect that looked like an answer

`action=parse` does not resolve redirects by default, unlike `action=ask`'s
SMW queries. Without `&redirects=1`, a vendor page whose SMW subject title
is a redirect returned "#REDIRECT [[Target]]" as its wikitext, in which
`TemplateRegex` then correctly found no `{{Temporary}}` template - so the
caller cached `""` ("checked, no tag"), which looked identical to a real,
deliberate absence and was never retried.

That is also why a null return and an empty wikitext body are not
interchangeable at the call site. `ResolveSeasonalFestivalValuesAsync`
warns about and leaves uncached the "wikitext came back null at all" case
(missing or renamed page, API error object), precisely because a null does
not mean "checked, no template" the way an empty body legitimately can.

### T.7 `VendorOfferHasherGoldenVectorTests`: why the fixture lives in `tests/shared/`

`tests/shared/vendor_offer_hasher_vectors.json` was originally a
CROSS-PROJECT net: the module carried its own copy of the hasher under
`Services/`, and both suites replayed these same rows so the two copies
could not drift. That copy had no callers anywhere in the module and has
been deleted, leaving one implementation, so the fixture's job is now
regression pinning over time rather than agreement between two files. It
stays outside either project's `Helpers/` because it is still the right
home for a hash contract that keys shipped data, and because a second
consumer may return.

### T.8 The festival tagging pass behind `SeasonalFestivalRoundTripTests`

The shipped `ref/vendor_offers.json` baseline carries `seasonalFestival`
on 57 offers across all six known festivals, not just the three
hand-tagged Candy Corn Vendor (Weekly) ecto offers it started with.
Dragon Bash Merchant (Weekly), Wintersday Trader (Weekly), Festival
Rewards Vendor (Weekly), Gauntlet Ticket Vendor, New Year Vendor and
Super Adventure Box Weekly Trader were live-tagged by a scoped
`--tag-seasonal-festivals --merge-into` run targeting exactly those six
merchants.

Candy Corn Vendor (Weekly) was deliberately excluded from that scoped
query. A fresh scrape of ANY merchant recomputes new OfferIds for that
merchant (see `VendorOfferHasher`'s own doc comment on the Astral Acclaim
hash-format migration), so touching it would have broken the test's "the
three known offer IDs survive identically" requirement - and with it the
evidence that a `--merge-into` run does not silently drop tags. Coverage
is deliberately partial: thousands of non-festival vendor pages remain
untagged, since the pass covered the known festival vendor list rather
than a full re-scrape (KNOWN-ISSUES #63).

### T.9 `WikiSmwClient` partitioning: what the `~` glob actually matches

A query past the SMW API's ~5500-result offset limit is split by vendor-name
prefix, `[[Has vendor::~As*]]`. Four properties of that glob decide which
characters the split has to cover, and all four are readable in Semantic
MediaWiki's own source rather than inferred from behaviour:

- `~` is `SMW_CMP_LIKE`, which `ComparatorMapper::mapComparator` maps to SQL
  `LIKE`, rewriting `*` to `%` and `?` to `_`.
- `ValueDescriptionInterpreter::interpretDescription` builds the condition as
  `smw_sortkey <comparator> <value>` against the entity ID table for every
  comparator except `=`. The match is on the sortkey, not the page id.
- `TableSchemaManager` declares `smw_sortkey` as `FieldType::FIELD_TITLE`
  unless the wiki opts into the `SMW_FIELDT_CHAR_NOCASE` feature flag, and
  `MySQLTableBuilder` maps `title` to `VARBINARY(255)`. `LIKE` against a
  binary column compares bytes, so **the match is case-sensitive**. The
  opt-in alternative is `VARCHAR(255) ... COLLATE utf8_general_ci`, which
  would be case-insensitive; the GW2 wiki does not have it on.
- `WikiPage::getSortKey` returns the DB key with underscores replaced by
  spaces, and `ComparatorMapper` escapes a literal `_` in the value to `\_`.
  So a **space** is a real character to split on and an underscore is not:
  no page title carries one.

The consequence is why `PartitionCharacters` is not just `A-Z0-9`. Vendor
names are Title Case, so a single-character prefix always matches, but every
character after the first is usually lowercase: `[[Has vendor::~AS*]]`
matches nothing at all, while the sixteen `Astral Ward *` merchants sit
under `As`. A depth-2 split over an uppercase-only set therefore reported
every sub-prefix as empty and kept only the first 5,500 rows of each
overflowing letter, which is how a whole family of merchants stayed out of
`ref/vendor_offers.json`.

That failure was silent because "the probe returned no rows" and "no vendor
starts with these characters" were recorded identically. The split now
asserts the arithmetic instead: a partition that overflowed has more rows
than it returned, so at least one child must hold rows. If every child
answers with none, the partition is recorded UNRESOLVED rather than
accepted, and the coverage gate blocks the write.

### T.10 Vendor requirements: classifying free-form wiki prose

Every wiki vendor row can carry a `Has requirement` value, and 9,337 of the
70,644 scraped rows do, across 1,236 distinct strings. The property is prose
written by editors, and it records every kind of gate the wiki knows about:
achievements, mastery levels, expansions, festivals, renown hearts, wardrobe
unlocks, held items. Nothing in the data marks which kind a string is, and
the kinds are not distinguishable by shape - "Radiance of the Sun God" is an
achievement, "Nuhoch Language" is a mastery level, and the two are the same
sort of noun phrase.

`VendorRequirementClassifier` therefore matches whole values against GW2 API
name lists rather than parsing them. A value is classified only when, after
one enclosing wiki link is unwrapped, the WHOLE of it is an exact API name:

- an achievement name from `/v2/achievements`, or a page anchor of the form
  `Page#achievement<id>`, which the wiki's own achievement tables emit and
  which names the achievement's API id outright;
- a mastery LEVEL name from `/v2/masteries` (the wiki cites the level, not
  the track, so the track id alone would not answer the question);
- one of the five expansion titles `/v2/account` is documented to report in
  its `access` array.

Three rules keep it from inventing an answer. A name two achievements share
is dropped from the index rather than resolved to one of them - 193 of the
live list's 8,230 names are shared. A value that matches two different KINDS
is left unclassified and reported, because either answer could be the wrong
gate. And an expansion the `access` array has no documented flag for, such
as Visions of Eternity, stays unclassified rather than being given a flag
derived from its title.

Measured over the full scrape: 1,284 rows resolve to an achievement by name,
115 by anchor, 965 to a mastery level, 400 to an expansion, 1 distinct value
(5 rows) is ambiguous, and 6,568 rows carry prose nothing matches. So about
30% of requirement rows can be checked against an account and the rest
cannot. Every row keeps its text either way: a player who is told "this
vendor requires Radiance of the Sun God" can act on that whether or not the
module can verify it.

The account side costs one scope. `/v2/account/achievements` and
`/v2/account/masteries` both need `progression`; the `access` array comes
from `/v2/account` on the `account` scope the module already requires, so an
account that declines `progression` still gets its expansion gates checked.
One documented quirk is coded for: an account that received Heart of Thorns
by buying Path of Fire does not carry the `HeartOfThorns` flag, so
`PathOfFire` satisfies a Heart of Thorns requirement.

None of this changes a plan. A gated offer is selected, priced and routed
exactly as before; the requirement is reported as a notice row beside the
vendor purchase caps, which are handled the same way.
