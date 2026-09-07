> **Milestone record - 2026-09-06, branch `w40-reflow-on-release`.**
> Point-in-time evidence - it describes the code as it stood that day and may not describe current code. Current documentation is [`docs/`](../../docs/README.md).

## The input strip still reflowed on a pause, not on the release

Dragging the window's resize corner on the Crafting Plan tab still moved
the item-entry row before the drag was over. The report was that the row
reflowed after a brief pause in mouse movement, which felt clunky, and
that it should wait for the button to come up.

### The defect: the drag was not recognised as a drag

`Services/DeferredReflowGate` already released a reflow on the pointer
coming up. It also released one that no pointer was involved in, after a
quiet interval, so a burst of window-driven resizes still coalesced.

Which of the two applied was decided by `CraftingPlanView.PointerHeld`,
which read `GameService.Input.Mouse.State.LeftButton`. When that reads
Released during a real drag, the drag falls into the pointer-free branch
and the quiet interval releases it - which is the reported symptom.

### The fix: read the window's own resize flag, and drop the interval

`ResizeDragActive` replaces `PointerHeld`. It walks up from the tab panel
to the `WindowBase2` hosting it and reads `Resizing`.

Blish sets that flag when the left button goes down on the window's
resize handle and clears it from a handler on the global
left-button-release event. `WindowBase2.UpdateContainer` writes the
window size only while the flag is set. So every resize event a corner
drag produces arrives on a frame where the flag reads true, wherever the
pointer has moved to, and the first frame it reads false is the release.

The left button is still read, but only as a second term, for the resize
this window does not drive: dragging the game client's own border resizes
the sprite screen, and `ResizableTabbedWindow` refits the window to each
new screen size.

With the drag recognised, the quiet interval has nothing left to release
and is gone. `DeferredReflowGate` no longer takes a settle interval and
`TryTake` no longer tracks whether a pending width belonged to a drag: it
hands the width back on the first take where the drag is not active.

### What still bounds the wait

`StripReflowStallMs` rises from 2000 to 10000. It is not a debounce. It
fires only when a drag flag outlives its drag, and there is one way that
happens: Blish clears `Resizing` from a handler that returns early while
the window is hidden, so hiding the window mid-drag strands the flag. Ten
seconds is far longer than any pause a hand makes mid-drag, which is the
point - the ceiling must never be reachable by a person holding the grip
still.

The ceiling runs from the last width observed, so a drag that keeps
moving keeps pushing it back.

### What the change trades

Unchanged from the previous attempt. While the button is held, a
narrowing drag leaves the rightmost cell clipped by the input panel and a
widening one leaves dead space at the right. Both resolve on release.

### Regression coverage

`tests/TaimisToolbench.Tests/Services/DeferredReflowGateTests.cs` is
rewritten for the new release rule.

- A pause mid-drag is offered the old 150ms interval sixty times over and
  releases nothing.
- A drag flag stuck at active releases at the ceiling and not before.
- A resize no drag drove is applied at the first take, with no wait.
- The 500px to 1800px sweep now runs longer than the ceiling, so it also
  proves a moving grip never trips it.
- A new case pins the pairing the strip depends on: the row count from
  `ItemInputGridLayout` and the reserved height from
  `TopRegionLayoutMath` are both taken from the gate's applied width, so
  they hold still together during the drag and move together at release.

`docs/file-budgets.txt`: `Views/CraftingPlanView.cs` 5200 to 5230.

Build 0 warnings. Suite 4290 + 3 + 336.

Gate: NOT RUN - no live game session is recorded on this branch. To
confirm in game, drag the window's resize corner on the Crafting Plan
tab, pause mid-drag for several seconds, and check the item-entry row
does not repack until the mouse button is released.
