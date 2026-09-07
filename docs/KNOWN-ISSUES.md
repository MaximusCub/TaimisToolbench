# Known Issues

The issue tracker for Taimi's Toolbench. It holds three things and
deliberately nothing else:

1. The **[numbered issue catalog](#numbered-issue-catalog)** - every issue
   this project has logged, kept under its original number, with a short
   resolution summary. `.cs` source comments cite these as
   `KNOWN-ISSUES #N` (or a decimal sub-item like `#20.1`). That is the only
   citation shape in the codebase: a comment that names this file names a
   number, and the number resolves to a heading below.
2. The **[DEFERRED](#deferred-recorded-not-implemented)** list: items that
   are genuinely still open.
3. The **[milestone record ledger](#milestone-record-ledger)**: one line per
   milestone record, carrying the exact names source cites that record by
   and the path to the full record.

The records themselves are not in this file and are not under `docs/` at
all. They are the engineering record, in [`../dev/`](../dev/README.md):
one file per branch in [`../dev/records/`](../dev/records/), the 69 older
ones in [`../dev/archive/known-issues/`](../dev/archive/known-issues/), and
the pre-M38 fix-pass diary - hypotheses, instrumentation, root-cause traces,
dated gate records, under these same item numbers - in the module's internal
history (not published in this repository).

**Looking for *why* a piece of code is shaped the way it is**, rather than
the story of how a specific bug was found and fixed?
[`ARCHITECTURE.md`](ARCHITECTURE.md) distills the durable rationale
for the handful of mechanisms (scroll preserve/restore, the resize
relayout registry, the wheel-delta sanitizer, merged-ceil vendor batching,
solver decision rules, and so on) that this history produced. Unlike a
record, it is kept current, so it is the right thing for source to cite
when the point is the reasoning rather than the day it was worked out.

## How this file works

A branch that finishes a milestone writes its record to
`dev/records/<branch-slug>.md` and adds one line to the ledger below. One
file per branch is the whole convention: two branches never write the same
file, so two records never conflict, and there is no merge choreography to
remember. Four rules carry weight.

1. **Numbers are permanent.** A fixed issue keeps its number and gains a
   resolution line; nothing is renumbered, merged or deleted. Over 200
   source comments cite these numbers, and CI checks that every cited
   number resolves to a heading here.
2. **A ledger line repeats the names source cites.** If a `.cs`, test or
   `ref/` file cites a record by a quoted phrase rather than by number, the
   ledger line for that record must repeat the phrase verbatim, so a grep
   of this file still lands one hop from the full record. Better: give the
   record a catalog number and cite that.
3. **A record is evidence, not documentation.** It describes the code on
   its own date and is never brought up to date. A correction is a new
   record. Two things may be added to an existing record because neither
   changes what it says: the provenance banner at the top, and a relative
   link repaired after a directory move.
4. **Every record ends in a gate line.** The gate is the live sandbox
   session that runs the change against the real game - the only check this
   project has for anything a test cannot reach, which is most of the
   rendering. The line says `Gate: PASS`, `Gate: FAIL`, `Gate: PARTIAL
   PASS` with what failed, or plainly that it has not run. A record whose
   gate never ran says so and stays saying so; that is why the ledger has
   entries reading "gate not yet run live" years after the fact.

**Size.** CI fails the build when this file passes 250KB. Measured
2026-08-25, after the records moved out and the citation anchors moved in:
the catalog, the DEFERRED list and the ledger cost **72KB** between them, and a merged branch now adds
a ledger line rather than a record - hundreds of bytes, not the ~11.5KB a
branch used to add when records lived here. So 250KB is headroom against
catalog growth, not a schedule; if it ever fires, the pass to run is a
ledger consolidation.

That threshold replaces a ~100KB one, and the replacement is the point
rather than the number. The old rule was written into a file that had just
finished a rotation at 112KB, so its condition was true the moment it was
written and stayed true forever after - it read as rigorous (a shell
command, a measured growth rate, a named precedent) and could never fire.
A threshold below its own floor is not a tripwire, and neither is one that
depends on somebody remembering to run `wc -c`, which is why this one is a
CI step.

## Policy: code pinned by expensive evidence

Some files behave the way they do because of measurements that cost a lot
to take, and a change that looked harmless has broken them before:
`Services/ModuleLog.cs`, `Services/PlanContentHeightMath.cs`,
`Services/PlanRelayoutMath.cs`, the scroll/resize/wheel machinery in
`Views/CraftingPlanView.cs`, and `Services/VendorBatchSolver.cs`'s
merged-ceil batching math. Changing them is allowed, but the change has to
carry its proof - characterization tests pinning current behavior BEFORE
the change (for visual/geometry code: the pixel-scanner and a live sandbox
check), the standard adversarial review pipeline, and an explicit statement
of what improved with evidence of zero regression. The burden of proof
scales with the file's regression history; it never becomes prohibition.

An earlier rule froze those files outright - no change permitted at all.
That freeze was retired on 2026-08-17 and replaced by the paragraph above.
Records below that describe a file as frozen are narrating what applied to
that one past change on the date it was made, and are left in their
original wording to preserve the record.

---

## Numbered issue catalog

### Items 1-11: M30 wave (initial hands-on pass, post-M29)

All eleven were fixed in M30. Briefly: (1) pill-click scroll reset -
restore/verify is now driven by a per-frame `FrameTicker` rather than a
same-frame `QueueMainThreadUpdate` re-queue. (2) resize-drag flicker/
transient tree collapse - full dispose+rebuild on every drag tick replaced
with a 150ms trailing debounce. (3) Total Cost currency rows gained icons.
(4) more vertical spacing between major sections. (5) collapsed Recipe
Tree containers now contract correctly (no stale whitespace). (6) Shopping
List Amount/Each/Total columns gained inter-column spacing. (7) row
dividers now use a consistent higher-contrast color. (8) Ball of Dark
Energy (and four HoT map-completion Gifts) - unpriceable items now render
a wiki-verified acquisition-hint tooltip instead of a misleading "only
available source" message. (9) rarity text brightened for parchment-
background contrast. (10) window content region resized to 684px high /
26px margin to stop texture bleed-through. (11) decision pill labels now
render in white instead of their own border color.

### 12. Fast wheel-up scroll: net-downward stutter (FIXED in M33, reopened and root-caused in M36)

Rapid wheel-up bursts could scroll up then jump back down further than
they went up. Root cause (M33): the old scroll-verify loop required
content height to be stable frame-to-frame before trusting the scrollbar's
live value, and nested `AutoSize` convergence kept height fluctuating for
several frames after every rebuild - closed by making container heights
synchronous (`PlanContentHeightMath`) and having the verify window yield
immediately on any observed wheel event. Reopened in M36: a real defect in
the *shipped* Blish HUD v1.3.0 binary (`MouseEventArgs.WheelDelta`
mis-unwraps a Windows-coalesced multi-notch wheel event as a huge negative
delta) was root-caused by decompiling the vendored binary and fixed
module-side with `WheelDeltaSanitizer`. See `docs/ARCHITECTURE.md`
sections 1-3.

### 13. Resize UX rework: live reflow, no settle stutter (FIXED in M33)

Live in-place relayout during a resize drag (no dispose+rebuild), driven
by the `_relayoutActions`/`_reellipsisActions` registry described in
`docs/ARCHITECTURE.md` section 5.

### 14. Pill-click viewport flash (jump to top and back) (FIXED in M33)

Closed by the same synchronous-height-contract fix as item 12.

### 15. Shopping tag text contrast (VENDOR / SALVAGE / UNKNOWN)

Logged as a contrast follow-up to the M30 #11 pill-label fix (which only
covered tree pills, not shopping-list source tags). No explicit resolution
note was recorded against this item number in the original backlog.

### 16. Vendor-source items show no price

Logged: vendor-decision rows rendered empty Each/Total cells, including
non-coin currency costs. The currency-icon rendering pipeline
(`CoinCurrencyRenderer`, `CurrencyDisplayResolver`) that this item asked
for is in production use today. No explicit "(FIXED in Mxx)" note was
recorded against this item number in the original backlog.

### 17. Seed data gaps: false UNKNOWNs in the Exordium tree (FIXED in M33)

Three of four original hypotheses were wrong (confirmed by wiki research
and an offline Harness dump): most "false UNKNOWN" nodes were actually a
solver bug (stopped evaluating siblings after the first unpriceable
ingredient), not a seed gap. The one real gap - Mystic Clover had no
Mystic Forge recipe seeded - was added (wiki-verified EV pricing). A
stale, wiki-removed vendor offer for Gift of Battle was also identified
and later removed as a Wave B follow-up.

### 18. Multi-source decision display is inconsistent

Logged: a node priced via one source (e.g. TP) could render a pill for a
different source (e.g. VENDOR). The decision-pill-must-match-committed-
source invariant this item asked for is covered today by
`DecisionPillPlannerTests`. No explicit "(FIXED in Mxx)" note was recorded
against this item number in the original backlog.

### 19. Resize-drag scroll reset on height change (FIXED in M33)

Part of the same synchronous height-contract / relayout-registry fix as
items 12-14.

### 20. M34: gw2efficiency owned-materials parity + correctness fixes

- **20.1** Obsidian Shard 179x showing Total 186 (not 180): a merged-ceil
  vendor-batch rounding bug, fixed - see `docs/ARCHITECTURE.md` section 7.
- **20.2** Vendor purchase caps no longer hard-exclude an offer; they now
  produce a warn-only notice instead of removing the offer from
  consideration.
- **20.3** Stale "Building final result..." status race between two
  independently-scheduled callbacks, fixed by `StatusUpdateGuard` - see
  `docs/ARCHITECTURE.md` section 6.
- **20.4** Owned-materials parity scope: per-node owned-quantity
  attribution, primary-option-only pool consumption, the "Value Own
  Materials" force-buy pre-pass (gw2e's `valueOwnItems` rule), owned
  currency as a display-only annotation (never fed back into the solve),
  the "Using N owned" pill, and the Ignore-pill mechanism (zeroes a node's
  own cost contribution tree-wide by item id, deliberately not cascading
  gw2e's full quantity re-derivation - an explicitly recorded, narrower
  substitute).

### 21. M35: gw2efficiency parity - multi-item plans

Multi-item batch planning: a synthetic wrapper recipe prices N selected
item roots together in one pass (Services layer), with a multi-row UI
(Views layer) showing each root's own decision. Several deliberate,
recorded divergences from gw2e's own multi-item UX (no per-node "Crafting
Profit" pill on individual roots yet; no drag-to-reorder rows).

### 22. Ignore-pill click sets status to "Best path restored" (cosmetic) (FIXED in M37, see #27)

### 23. Horizontal dividers appear/disappear with scroll position (FIXED in M36; M36b follow-up for 44px/32px rows)

Root cause: Blish's `Container.Paint` clips a divider drawn exactly at a
row's bottom edge unpredictably depending on scroll phase. M36b gave
`CreateRowDivider` a `bottomClearance` parameter (1 extra logical pixel of
gap for the vulnerable 44px/32px row types), proven immune by simulation
across every row height and all four GW2 UI Size scale factors - this is
machinery pinned by expensive evidence (see the policy note above), see
`docs/ARCHITECTURE.md` section V.26 (`LabelHelpers`), which is where the
scissor derivation lives. This entry cited section 3 until 2026-09-05;
section 3 is scroll preserve/restore/verify and says nothing about
dividers.
Required Recipes and Crafting Steps were live pixel-scan verified at
multiple scroll offsets after the fix; Required Disciplines (32px rows)
was simulation-proven at the time but not yet individually pixel-scanned
in that same pass - **this gap was closed by item 30 below** (M37 live
sandbox session, 2026-07-22, scanned Required Disciplines directly and
confirmed the same clean result the simulation predicted).

Tier-2 re-run (2026-08-27, icon-tier2 branch): the tier-2 icon change
grew the plan tab's icon-led rows to 45px (Used Materials / Shopping /
Required Recipes) and 52px (Crafting Steps). The M36b simulation was
re-derived from the decompiled `ScaleBy` floor/ceil semantics, validated
by reproducing the record above (514/5000 vanish phases for 44px/32px at
0.897 - the published ~10.2% - and 850/5000 for the then-30px header at
0.81), then run at the new heights: both are VULNERABLE at clearance 0
(45px: 18.0% at 0.81 / 7.0% at 0.897; 52px: 10.3% at 0.897) and immune
at clearance 1 at all four scales. Every icon-led row now passes
`PlanContentHeightMath.IconRowDividerClearance` (1), with the flush fit
preserved because the tier-2 heights absorb the clearance pixel in their
own derivation. The proof is now executable and runs in CI:
`tests/TaimisToolbench.Tests/Services/RowDividerScissorSimulationTests.cs`
sweeps every shipped (rowHeight, clearance) pair at all four scales and
fails on any vanish.

Padded fit (2026-09-06): the 45px and 52px above are the heights of that
day, and are no longer shipped. Reported in game, twice: the rule read as
drawing over the icon, and the Crafting Steps rule sat off centre between
rows. Every icon-led plan row is now one height,
`PlanContentHeightMath.IconLedRowHeight` (50), with the frame at y=2 and
3px of clear space over and under it. The sweep already covered 50 at
clearance 1, so the proof needed no new case.

### 39. M38 view-decomposition entries (WP-21 through WP-25)

Numbered late, on 2026-08-25, so `Views/Rendering/ISectionRelayoutSink.cs`
and `Views/Rendering/TreeSectionController.cs` can cite `#39` instead of
"the WP-23 entry". It sits here rather than at the end of the catalog
because renumbering is forbidden and this is where it was written.

Not separately numbered in the original backlog (recorded there as
`WP-22`/`WP-23`/`WP-23b`/`WP-23c`/`WP-23d + WP-24`/`WP-25` narrative
entries, plus the WP-26 cut decision). See `docs/ARCHITECTURE.md` section
5 for the durable summary of what moved where and why, and the internal
history for the full diff-evidence and live-verification record of each
increment.

### 24. Homestead refinement handling (parity gap) (FIXED in M37)

The module's seed already carried all Homestead Refinement offer rows,
unconditionally, with no tier concept - so it silently priced every
account as if it owned every efficiency upgrade at every station (the
opposite of gw2e's conservative tier-0 default), with no way to turn it
off. Fixed: a per-material (Fiber/Metal/Wood) tier setting
(`HomesteadEfficiencyTiers`, default tier 0, matching gw2e's own default),
wiki-scraped `HomesteadTier` tagging on the relevant vendor offers, and a
solver-side filter that excludes any offer above the configured tier.
Deliberately NOT implemented: a "do you even own Homestead" master gate
(gw2e has none either) and the Homestead Black Market purchase path
(wiki game-id resolution gap, still unseeded).

### 25. Multi-item sell-side economics (parity gap, deliberate M35 gap) (FIXED in M37)

M35 left sell-value/profit rollups unset for multi-item batches. Fixed:
`SellSideEconomics.ApplyBatchSellSideEconomics` sums each qualifying
root's own sellable quantity/net sale value/profit into batch totals.
Deliberately diverges from gw2e in two ways (both recorded in the internal
history with the reasoning): the rollup does not filter
out a bought-but-tradable root the way gw2e's `craft === true` filter
does, and an untradable crafted root is excluded entirely (contributes
zero) rather than silently absorbed as a hidden cost the way gw2e's own
upstream code does it.

### 26. Achievement-bit ingredient dedup (parity micro-gap) (FIXED in M37)

gw2e de-duplicates ingredients tagged with an achievement-reward
`achievement_bit` across the whole tree (relevant to 7 recovered custom
recipes - WvW siege-weapon-blueprint achievement rewards). Ported 1:1 via
a new `AchievementBitDedupPrePass` (pure, Blish-free), run once per fresh
tree build. A `DecisionPillPlanner` "COUNTED ELSEWHERE" pill replaces the
plain HAVE pill on a deduped node. Two related latent bugs found and
fixed during this work: a zeroed-but-mismatched-source node could leave a
standalone zero-cost ghost row in the plan (general `PlanSolver.Collect`
guard added), and new achievement/merchant recipe ids adjacent to the
Mystic Forge id range could NRE on a cold cache miss (fixed with a real
membership check).

### 27. Ignore-pill click status label (FIXED in M37, closes #22)

Root cause: the status-text choice used "did any overrides exist" as a
proxy for "was this the Best Path preset", so the Ignore toggle (which
never touches overrides) incorrectly printed the Best Path preset's own
label. Fixed with an explicit `isBestPathPreset` flag threaded to a new
`StatusText.ForOverrideResolve` helper.

### 28. Vendor cap data seeding + stale-offer sweep (PARTIAL - core FIXED in M37; gaps deferred)

Seeded real daily/weekly purchase caps for 689 of ~53,530 vendor offers
from wiki SMW properties (previously all null, making the M34 warn-only
cap machinery inert). A scoped stale-offer sweep (offers reachable from
seeded recipe trees only, ~10.2% of the total) found 2 genuinely
discontinued offers (removed, wiki-confirmed via patch notes) out of 394
raw candidates - the rest were scraper coverage gaps or wiki page
renames, not real removals. Character/total purchase caps remain
unseeded (no account/character concept exists in this module). See the
DEFERRED list below for the specific named gaps (Skirmish Merchant page
split, one unresolvable vendor-page rename, the wiki-drift superset).

### 29. Owned-materials UI live verification (VERIFIED in M37, live sandbox session 2026-07-22)

### 30. Required Disciplines divider pixel-scan (VERIFIED in M37, live sandbox session 2026-07-22)

Closes the gap item 23 left open: Required Disciplines (32px rows) was
scanned directly at two scroll offsets and matched the simulation's
predicted clean result exactly.

### 31. Concurrency and degradation audits (verification debt) (FIXED in M37)

Three formal audits (cross-thread await marshaling; offline/API-down
degradation per endpoint; price-cache thread-safety), 9 confirmed
findings fixed. Highlights: a Clear-Cache-races-a-background-refresh
resurrection bug (fixed with a new `SnapshotEpochGuard`); a validation
no-op that could permanently stick-disable the Generate button; a
snapshot-fetch path that silently returned an empty, fully-timestamped
snapshot on total failure instead of throwing (now throws
`SnapshotFetchFailedException`, never persisted); several unguarded batch
loops that aborted an entire fetch on one bad batch instead of degrading
per-batch; a price-fetch stampede under overlapping generate calls (fixed
with in-flight fetch tracking in `TradingPostService`).

### 32. StyleCop analyzer debt: SA1101/SA1200 suppressed, smaller rules tracked (M38 WP-02)

A code-review finding caught that two default-severity rules (SA1101
"prefix local calls with this", SA1200 "using directive must appear
within the namespace") were 61% of every warning the WP-02 ruleset
produced, fighting an established codebase convention rather than
catching drift against one. Both suppressed with the same inline-rationale
style as the pre-existing SA1309 suppression. Three smaller rules with a
non-trivial footprint (SA1413/SA1516/SA1503) are left enabled and recorded
as a future cleanup candidate.

### 33. Astral Acclaim package: Wizard's Vault seasonal purchase cap seeding (2026-07-22)

Seeded the wiki's "Has seasonal purchase cap" SMW property (item 28's
deferred fifth cap type) for Wizard's Vault offers, and wired a new
`TimegatedCapType.Seasonal` through `VendorBatchSolver.FinalizeVendorBatches`
so a seasonal cap produces its own warn-only notice independently of
daily/weekly. Confirmed Bag of Coins (1 Gold) is genuinely tiered (two
separate vendor offers/game ids, not one row with two prices) - no
model/solver change needed, the solver's existing cheapest-offer selection
already handles it. One offer (Lesser Essence of Gold) hit a reproducible
SMW property-chain resolution gap and was left byte-identical rather than
guessing its game id.

### 34. Dead live-wiki vendor-offer resolver removed (M38 WP-10)

`VendorOfferResolver`/`IWikiVendorClient`/`WikiLookupOptions` and their
test double were deleted: `Module.cs` always constructed the pipeline with
`resolver: null`, so every branch guarded on it was unreachable by
construction. Superseded by the static seed-store path
(`VendorOfferLoader`/`VendorOfferStore`, populated offline). Recorded
dissent: keep-and-comment was argued for instead of
delete; deletion was chosen (also removed the test suite's single largest
real-time cost - retry/backoff `Task.Delay`s). A clean `git revert`
restores the seam if a live-wiki resolve mode is wanted again.

### 35. M39 module log system: WP-16/WP-17 subsumption + JSONL/rotation rationale (FIXED in M39)

The M39 log-system PR fully implemented M38's planned WP-16 (persistence-
store failure logging, replacing silent `Debug.WriteLine`) and WP-17
(Module.cs catch-consistency + FrameTicker unload teardown) as a side
effect; this entry records that subsumption so neither gets duplicated.
Also records why the log store uses newline-delimited JSON rather than one
big JSON array (crash-safety: a partial trailing line is recoverable, a
mid-write corrupt array is not) and its two independent rotation caps
(2 MB size / 14 day age, both user-configurable and clamped against
out-of-range persisted values).

### 36. LogTabContent field crash: TabChanged raced Build() on two threads, corrupting a Queue<T> (FIXED, 2026-08-06)

**Field evidence:** a real user install crashed with `System.ArgumentException`
("Destination array was not long enough") from `Queue<T>.SetCapacity` <-
`Queue<T>.Enqueue` <- `LogTabContent.RebuildRows()` <- `LogTabContent.Refresh()`
<- Module.cs's `TabChanged` handler <- `TabbedWindow2.OnTabChanged` <- a
main-thread mouse click, while ThreadPool-thread WARN writes from failing
snapshot fetches (invalid API token) were landing in `ModuleLog` throughout
(`ModuleLog` itself is correctly synchronized - two-lock + `ConcurrentQueue`
- and was not implicated).

**Root cause:** Blish HUD's own `WindowBase2.ShowView` runs a tab's `Build()`
via `View.DoLoad().ContinueWith(...)` (docs/ARCHITECTURE.md Section 1) - with
no `SynchronizationContext` installed and no
`TaskContinuationOptions.ExecuteSynchronously` passed to `ContinueWith`, that
continuation is scheduled onto the ThreadPool, not the main/game thread,
regardless of whether `DoLoad`'s own task has already completed by the time
`ContinueWith` runs. (This mechanism was re-verified 2026-08-06 during this
fix-loop by decompiling the shipped Blish HUD v1.3.0 binary with `ilspycmd`
after a prior pass cited docs/ARCHITECTURE.md Section 1 for it without that
section actually containing it yet - see Section 1's own "Verified"
paragraph for the decompiled call chain. The underlying claim held up; only
the citation target was missing, and is now filled in.) `LogTabContent.
Build()`'s own tail called `RebuildRows()` directly on that ThreadPool
thread. Meanwhile, `SelectedTab` flips to the Log tab (and Module.cs's
`TabChanged` handler fires, synchronously, on the main thread) independently
of - and not necessarily after - that ThreadPool-scheduled `Build()` call
completing. `TabChanged` calls `LogTabContent.Refresh()` -> `RebuildRows()`
against the SAME `LogTabContent` instance whenever `_logContent` is already
non-null and its `_contentPanel` is already live, which `IsLive` alone does
not rule out mid-`Build()`. PR #99 (item 35's follow-up polish PR) had added
a `_buildComplete` latch guarding `PollForUpdates()` against exactly this
ThreadPool-vs-main-thread shape, on the assumption (recorded in that PR's
own review) that `TabChanged` could not fire before `_logContent` pointed at
a fully-built instance - but `Refresh()` itself never checked the latch, and
the in-game crash proves that assumption's ordering is timing-dependent and
CAN interleave. Two threads concurrently calling `Queue<(long,Label)>.
Enqueue` on the SAME `_renderedRows` field corrupted its internal
circular-buffer bookkeeping, producing the `Queue<T>.SetCapacity`
`ArgumentException` on a later `Enqueue`.

**Fix:** `LogTabContent.Build()`'s tail (the `RebuildRows()` call plus the
`_buildComplete = true` write) is now marshaled onto the main thread via
`MainThreadMarshal.Run` (`Views/MainThreadMarshal.cs`). `Refresh()`,
`PollForUpdates()`, the level-dropdown `ValueChanged` handler, the
search-box `TextChanged` handler, and `ClearView()` all now check
`_buildComplete` before calling `RebuildRows()` - a first fix pass only
gated `PollForUpdates()`/`Refresh()` and left the other three ungated (still
main-thread, so not a crash, but reachable while Build's body is still
executing and unable to state a real enforced invariant); a review pass on
2026-08-06 closed that gap. With all six `RebuildRows()` call sites either
gated on `_buildComplete` or IS the marshaled tail itself, they are
inherently serialized: a single thread cannot run two call stacks at the
same instant, so the race is impossible BY CONSTRUCTION, not merely guarded.
`_buildComplete` is KEPT (not removed as obsolete) - it no longer has a
cross-thread safety job, but still avoids a wasted, redundant `RebuildRows()`
pass on any of its five callers if they fire before Build()'s own queued
tail has landed; its `volatile` qualifier was removed since every read/write
is now main-thread-only. This does not make every field `LogTabContent`
touches main-thread-only - the eight control fields (`_toolbarPanel`,
`_levelDropdown`, `_searchBox`, `_followCheckbox`, `_clearViewButton`,
`_copyButton`, `_statusLabel`, `_contentPanel`) are still first published by
the rest of `Build()`'s body on the ThreadPool thread; every main-thread read
of one of them stays behind its existing null guard (`IsLive`, `_searchBox?`,
`_statusLabel == null`) rather than assuming it is already non-null - see
the corrected invariant note on `_buildComplete`'s own doc comment in
`Views/LogTabContent.cs`.

The same review pass found `MainView.Build()`'s own top-of-body
cancel-dispose-and-null-out of `_searchDebounceCts` (added by the
class-sweep fix below) had been left running on the ThreadPool thread
instead of being marshaled with the rest of Build's tail, while
`RebuildContent()` and `ScheduleSearchRebuild()` write that same field on
the main thread - the identical hazard shape one level down, on a field the
class sweep introduced rather than the original crash. That cleanup now also
runs inside `MainView.Build()`'s `MainThreadMarshal.Run` tail, ahead of the
existing liveness guard so a stale debounce is still cancelled even if the
tab was switched away before the queued callback runs.

**Class sweep** (Blish `Build()`/`DoLoad` continuation mutating UI state that
a main-thread path also touches):

| View | Result |
| --- | --- |
| `LogTabContent` (Log tab) | **HAZARD FIXED** - see above; all six `RebuildRows()` entry points are now either main-thread-and-gated on `_buildComplete` or are the marshaled tail itself. |
| `MainView` (Snapshot tab) | **HAZARD FIXED** - same PATTERN as `LogTabContent` (an un-marshaled `Build()` tail mutating UI state a main-thread path also touches), but NOT the same underlying mechanism: unlike `LogTabContent`'s plain unsynchronized `Queue<T>`, `Container.Children` is itself `ReaderWriterLockSlim`-guarded and cannot be corrupted by concurrent access (`docs/ARCHITECTURE.md` Section 1's `ControlCollection<T>` finding, fourth round below) - the real hazards here are the non-atomic dispose-then-add compound sequence (duplicated content, same shape as `LogTabContent`'s doubled-placeholder incident) and a `_searchDebounceCts` `ObjectDisposedException` race this branch also closed. Broader than `LogTabContent` in one respect: this instance is never recreated per tab visit (Module.cs builds ONE `MainView` in `Initialize()` and reuses it), and `Module.Update()` calls `SetSnapshot()`/`SetStatus()` on it every tick a background refresh completes, unconditional on which tab is selected. `Build()`'s tail (`UpdateCoinDisplay`/`ApplyStatusDisplay`/`RebuildContent`, plus the `_searchDebounceCts` cleanup - see above) is marshaled onto the main thread via `MainThreadMarshal.Run`, matching every other `RebuildContent`/`ApplyStatusDisplay`/`UpdateCoinDisplay`/`_searchDebounceCts` call site in the file (all already main-thread: user-input handlers, or already-marshaled async continuations). This does not make every field `MainView` touches main-thread-only - its panel/control fields are, like `LogTabContent`'s, still first published by the rest of `Build()`'s body on the ThreadPool thread, and this file's own Clear Cache/Refresh Now/checkbox/dropdown handlers (wired mid-body, so reachable while later controls are still under construction) rely on the same null-guard pattern, not exclusivity. |
| `SettingsTabContent` (Settings tab) | NO HAZARD - `Build()` re-runs off the main thread on every tab revisit, but nothing outside `Build()` (no `Module.Update()` polling, no `TabChanged` handling) ever touches this class's fields; every other mutation is a button-`Click`/`CheckedChanged` handler, which cannot fire before `Build()` has already finished and the control exists. |
| `AboutTabContent` (About tab) | NO HAZARD - static, render-once content; nothing outside its own `Build()` touches its fields. |
| Plan History / Crafting Ranker placeholders (`Module.BuildPlaceholder`) | NO HAZARD - creates one `Label` and returns; nothing else ever references it. |
| `CraftingPlanView` (Crafting Plan tab) | **HAZARD PRESENT, OUT OF SCOPE** - not modified (scroll/`FrameTicker` machinery is pinned by expensive evidence, frozen outright under M38's earlier rule, hardened M31-M36). A first sweep pass incorrectly recorded this row as no live race; a 2026-08-06 review corrected it: `Build()` calls `StopLiveTickers()` (`Views/CraftingPlanView.cs:1511`) on the ThreadPool thread; that method `Cancel()`s -> `Dispose()`s three `SpriteScreen`-parented `FrameTicker` Controls (`_scrollVerifyTicker`, `_resizeDebounceTicker`, `_wheelWrapVerifyTicker`) whose `DoUpdate` runs on the main thread and survives tab switches (they are parented to `GameService.Graphics.SpriteScreen`, not this view's own control tree - by design, per their own field comments), and zeroes `_resizeSettlePending`/`_resizeScrollRestorePending`/`_resizeScrollSavedOffset`/`_lastWheelEventUtc`, which those same main-thread ticker steps read and write. Same hazard class as the two fixed rows above; deferred to a dedicated pass that can safely touch the M31-M36 scroll machinery rather than fixed here. Follow-up (2026-08-17, tree-tooltip-composer milestone doc pass): the 2026-08-06 count of three `FrameTicker`s was itself stale by then - `_spinnerTicker` (the W3B status-strip spinner, added between the two reviews) is a fourth `SpriteScreen`-parented `FrameTicker` `StopLiveTickers()` also `Cancel()`s on the same ThreadPool-thread `Build()` call, in the identical hazard class as the other three; not independently verified live, same OUT OF SCOPE deferral as the rest of this row. |

A third review round (2026-08-06) found the second round's own fix had left
behind a documentation defect of the exact class this issue exists to
prevent: the marshaled tails' and `IsLive`'s doc comments in both files
claimed the `Parent == null` liveness guard protects against a plain "tab
switched away" case. Independently re-verified via `ilspycmd` decompilation
(now recorded permanently in `docs/ARCHITECTURE.md` Section 1, "a tab
switch detaches, it does not dispose") that this is false:
`WindowBase2.ClearView` only detaches (`Container.ClearChildren`,
`Parent = null`, no `Dispose`) the outgoing view's OWN top-level
`ViewAdapter` panel from the window, and `ViewAdapter` does not override
`Unload()` - so every control below that top-level panel, including
`LogTabContent._contentPanel` and `MainView._headerPanel`/`_contentPanel`,
keeps a non-null `Parent` after a plain tab switch. Only `Control.Dispose()`
(reached via `Module.Unload()` disposing `_mainWindow`) nulls a control's
own `Parent`. Practical effect: none of the marshaled tails were ever
unsafe against a real tab switch - a tail that lands after the user
switches away simply does slightly more work than necessary (rendering
into a real, just no-longer-visible, tree instead of a genuinely
torn-down one), which the comments now say correctly instead of
certifying a guard against a case it cannot detect. Corrected four call
sites: `Views/LogTabContent.cs` (`Build()`'s marshaled tail, `IsLive`'s
doc comment) and `Views/MainView.cs` (the Refresh Now handler's marshaled
tail, `Build()`'s marshaled tail, `RunSearchDebounceAsync`'s marshaled
tail) - all now pointing at the single canonical explanation in
`docs/ARCHITECTURE.md` Section 1 instead of repeating (and having drifted
from) it independently at each site. No behavior changed; this round is
comment-only. The class-sweep table above and the `_buildComplete`
keep-vs-remove decision were both re-checked against the current code
during this round and still hold as written - no updates needed there.

A fourth review round (2026-08-06) found two more documentation defects of
the same class, both in `Views/MainView.cs`. First, the justification
recorded for marshaling `Build()`'s tail claimed "if `Update()` ever landed
while `Build()` was still executing this tail on the ThreadPool thread, two
threads would mutate the same `Children` collections concurrently, the same
shape that corrupted `LogTabContent`'s `_renderedRows` `Queue<T>`".
Independently re-verified via `ilspycmd` decompilation of the vendored
`Blish HUD.exe` (now recorded permanently in `docs/ARCHITECTURE.md` Section
1) that this is false: `Container.Children` (`ControlCollection<T>`) holds
its own `ReaderWriterLockSlim` and takes it on every operation, so unlike
`LogTabContent`'s plain `Queue<T>`, concurrent `Children` access cannot
corrupt the collection's internals - that is precisely why the in-game crash
landed in the module's own unsynchronized `Queue<T>` and not in Blish's
`Children`. The two hazards that DO justify marshaling this tail, neither of
which the prior comment named, are (a) the compound dispose-then-add
sequence being non-atomic, so two interleaved rebuilds can each dispose
before either adds and both survive (the same doubled-placeholder shape
`LogTabContent` hit live on 2026-07-23), and (b) the top-of-Build
`_searchDebounceCts?.Cancel();?.Dispose();` this branch moved into the
marshaled tail used to run directly on the ThreadPool thread, racing
`ScheduleSearchRebuild()`/`RebuildContent()`'s main-thread writes to the
same field - `CancellationTokenSource.Cancel()` calls `ThrowIfDisposed()`,
so whichever call landed second on the shared reference could throw
`ObjectDisposedException`, a genuine crash path this branch closed without
previously claiming credit for it. Second, `RunSearchDebounceAsync`'s
marshaled tail claimed the token "is usually already cancelled" because
"`CancelSearchDebounce()` also runs at the top of every fresh `Build()`" -
this branch had itself moved that cancel out of the top of `Build()` and
into `Build()`'s own marshaled tail (see the third-round fix above), so the
cancel is no longer synchronous with a same-tab revisit; the comment
contradicted a change made in the same diff. A debounce armed on a previous
visit can therefore stay live for the window between `Build`'s ThreadPool
body finishing and its own tail draining, and could render into whichever
`_contentPanel` the field currently holds (potentially the outgoing visit's
panel, since `Build` assigns `_contentPanel` last among the fields
`RebuildContent` reads) before being superseded moments later by `Build`'s
own tail - wasted work, not a wrong final state, which the comment now says
plainly. Both call sites corrected in `Views/MainView.cs`; the
`ControlCollection<T>` finding added permanently to `docs/ARCHITECTURE.md`
Section 1, and this table's `MainView` row above corrected to stop
repeating the disproven "same shape" claim. No behavior
changed; this round is comment-only. The class-sweep table's other rows and
the `_buildComplete` keep-vs-remove decision were re-checked against the
current code during this round and still hold as written.

**Correction (2026-09-05).** Three statements above have drifted off the
code. None of them changes a verdict; each sends a reader to the wrong
place.

- "`ViewAdapter` does not override `Unload()`" is false.
  `Views/ViewAdapter.cs:250` overrides it. The override only unsubscribes
  the window's `Resized` handler - it disposes nothing and reparents
  nothing - so the conclusion it was offered in support of still stands:
  a plain tab switch leaves every control below the top-level panel with
  a non-null `Parent`. The same sentence appears in
  `docs/ARCHITECTURE.md` Section 1 and is wrong there too.
- The `CraftingPlanView` row cites `Views/CraftingPlanView.cs:1511` for
  the `StopLiveTickers()` call in `Build()`. That call is now at line
  2111; the method is defined at line 1542. It still cancels four
  `SpriteScreen`-parented `FrameTicker`s from a ThreadPool-thread
  `Build()`, so the row's OUT OF SCOPE hazard is unchanged.
- "all six `RebuildRows()` call sites" is now four direct calls
  (`Views/LogTabContent.cs:401` - the marshaled tail - plus `:504`,
  `:541`, `:582`) reached through seven entry points, because a
  `RebuildRowsIfBuilt()` wrapper (`:578`) and a delete-log-file
  confirmation callback (`:628`) arrived later. Every one of them is
  still either gated on `_buildComplete` or is the tail itself, so the
  by-construction argument holds at the new count.

**Validation:** `dotnet build -p:Platform=x64` - 0 errors. Module test suite
(`tests/TaimisToolbench.Tests`) - 1101/1101 passing. VendorOfferUpdater
suite (`tests/VendorOfferUpdater.Tests`) - 135/135 passing (re-measured a
fourth time after the 2026-08-06 comment-correction round above; counts
unchanged, since that round touches only comments and documentation, none
of which the repo invariants permit or require test coverage for).
`LogTabContent`/`MainView` are Blish HUD UI code with no test net (repo
invariant: tests must stay Blish-free) - the fix is proven by construction
(every racing path is now provably main-thread-only, and gated against
acting before Build's own tail has landed where relevant) plus the live
gate below.

**Live gate:** PASS (2026-08-06, live branch-build sandbox
session under the hardened sandbox protocol, captures logfix_01_empty.png /
logfix_02_search.png):
- Empty-state placeholder renders exactly ONCE on first Log-tab open
  (the doubled-placeholder interleave shape is gone).
- Crash-condition recreation: four rounds of Snapshot Refresh Now (each
  spawning failing-fetch WARN writes from ThreadPool threads - the exact
  field trigger, InvalidAccessTokenException per source) immediately
  followed by Log-tab opens plus Snapshot/Log tab churn; Blish alive and
  Responding=True throughout, zero FATAL lines (the 2026-08-06 field
  crash reproduced fatally on the FIRST such open on the pre-fix build).
- Log entries render with level colors; the search box filters live
  ("material" -> exactly the four matching WARN rows) - also the first
  live verification of Log search, previously blocked by synthetic-
  keystroke death in long sessions.
- MainView search-debounce hazard (the ObjectDisposedException path this
  branch also closed): typed into the Snapshot search box then immediately
  churned Snapshot->Log->Snapshot->Log to force Build-tail vs debounce
  interleaves; no exception, no error lines, process responsive.
- Blish log sweep: zero FATAL, zero ERROR, zero Unhandled/ObjectDisposed
  across the session.

### 37. Snapshot refresh failure classification + "GW2 API access is not ready" dialog (2026-08-06)

FIXED 2026-08-06. A Snapshot "Refresh Now" fired at the GW2 character-select
screen failed every source (Blish only resolves the account's API key once a
character is in-world) and reported only a bare "Refresh Failed" label.
`SnapshotFailureClassifier` now sorts a failed refresh into
`ApiAccessNotReady` / `NetworkOrApiDown` / `PartialFailure` / `Unknown`
(Blish-free, by exception type name), and `Views/ApiAccessDialog` explains
the three checks with a Retry that shares `MainView.RefreshNowAsync()`.
Full record:
`dev/archive/known-issues/2026-08-06-field-test-ux-wave-2-mysticforge-sublabel-drop-fix.md`.

### 38. Review-pass fixes on item 37: ApiAccessDialog self-defense + background-refresh status parity (2026-08-06)

FIXED 2026-08-06. Three gaps found reviewing item 37 before its gate:
`ApiAccessDialog.Show()`/`Hide()` leaned on an unrelated Module-owned
object's lifecycle instead of their own `_disposed` flag (and `Dispose()`
was not idempotent); the dialog never reset its own state; and the
background-refresh path did not reach the same status wording as the manual
one. Same full record as item 37.

---

### Entries 40-63: citation anchors added 2026-08-25

Source used to cite this file in six shapes - `#12`, `20`, `31a-F1`,
`31c-audit`, `frozen-file #13`, and bare phrases like "tree dimming rule" -
of which only `#N` resolved to anything. The entries below give a number to
every record `.cs`, test and `ref/` files were citing by phrase, so `#N` is
now the single shape and every citation resolves in one hop. Nothing was
renumbered to do it: these are new numbers on records that already existed.

### 40. Item stat tooltips (in-game-style rich hover)

The rich hover block - icon+name header, what the item does, rarity, type,
level, binding - built from `/v2/items` alone. What `/v2/items` cannot give
is nominated stat combinations: an item with selectable stats has an open
judgment call (Q4) about *which* combination to compute, so the module
computes none. Cited from `Models/ItemStatBlock.cs`,
`Services/ItemMetadataService.cs`, `Services/ItemStatBlockFactory.cs` and
`Views/Rendering/TreeSectionController.cs`. Full record:
`dev/archive/known-issues/2026-08-23-item-stat-tooltips.md`.

**The judgment call does not cover an owned copy (2026-09-05).** The
sentence above is true of `/v2/items`, which describes an item type and so
has no single answer to give. It was then read as a limit on what the
module can know, and it is not one. The account endpoints report each
owned stack's own state: `AccountItem` carries `Stats` ("the selected
stats for the equipped item"), `Upgrades`, `Infusions`, `Binding`,
`BoundTo` and `Charges` alongside `Id` and `Count` - measured in the
vendored `Gw2Sharp` 1.7.4 model this module already calls
(`Gw2Sharp/WebApi/V2/Models/Account/AccountItem.cs` at tag v1.7.4), which
is the type `V2.Account.Bank`, `V2.Account.Inventory` and
`V2.Characters[name].Inventory` all return.
`Services/Gw2AccountSnapshotService.cs` reads `Id` and `Count` off each of
those three and drops the rest (lines 104-106, 133-135, 294-296), and
`Models/SnapshotItemEntry` has nowhere to put them. So for a Snapshot row
- an item the account actually holds - there is no open question about
which combination to compute, and no question about which copy the player
is looking at either: the API names both. The same reasoning appears in
`docs/ARCHITECTURE.md` section S1.4, where it also decides the bind-line
wording ("Which copy the player is looking at is instance state
`/v2/items` cannot carry"), and it is wrong there for the same reason.
Nothing is changed here. Doing something with this needs decisions this
entry cannot make on its own - what a stack of copies with different stats
should show, and whether a snapshot schema bump is worth it (see section
12) - and it does not touch the plan tree, where an item that is not owned
yet really does have no answer.

### 41. Tooltip facility (one rich surface, four-edge clamp)

Exactly one rich tooltip surface exists for the whole module, repointed on
hover, because decompiling Blish 1.3.0 showed `Control.Dispose` never
touches its `_tooltip` field - a per-control surface would leak. Blish's own
`Tooltip.UpdateTooltipPosition` protects the top edge and shifts left, so
the module adds the two edges Blish does not clamp. Full record:
`dev/archive/known-issues/2026-08-22-tooltip-facility.md`.

### 42. Tooltip authenticity (gap ids G1-G24, and the accepted divergences)

The pass that made the rich tooltip read as a game tooltip, measured at 3x
against wiki captures and FWDekker's replica. **This is the referent for
every `gap G<N>` id in the codebase**: G1-G24 are that record's gap map, and
they appear in `Services/ItemDescriptionSanitizer.cs`,
`Services/ItemStatTooltipComposer.cs`,
`Services/TooltipContent.cs`, `Services/TooltipLayoutMath.cs`,
`Services/CoinSegmentMath.cs`, `Views/Rendering/RichTooltipSurface.cs`,
`Views/Rendering/CoinCurrencyRenderer.cs` and `Views/Rendering/RarityColors.cs`.
Comments that used to cite "spec section N.M" cited a document that is not
in this repository and never was; they now cite this number, and the
measurement each one stood on is inlined at the constant. Carries the
warhelm first-band divergence (G15). Full record:
`dev/archive/known-issues/2026-08-23-tooltip-authenticity.md`.

**Correction (FIXED): the slot lines are not a divergence, and there was
never a reason for one.** The record above accepted "Infusion Slot" in
place of the game's "Unused Infusion Slot", on the grounds that what is
socketed in a player's copy is instance state `/v2/items` cannot know, so
calling a slot unused would be a guess. That reasoning was wrong.
`/v2/items` returns an item DEFINITION, not an instance, and a definition
has nothing socketed in it, so "Unused" is the accurate word. Where a
definition DOES ship an upgrade - a weapon's `suffix_item_id`, an infusion
slot carrying an `item_id` - that slot is now left out of the tooltip
entirely rather than described as unused, which is the same distinction
drawn correctly. The tooltip also draws the game's own glyph beside each
slot line, and prints upgrade slots, which it never did before: /v2/items
has no upgrade-slot field, so the count comes from the item's type and its
`NotUpgradeable` flag (`Services/ItemSlotFacts.cs`).

**Follow-up (PARTLY FIXED): what is socketed is no longer unknowable.** The
record above lists "what is actually socketed in the player's copy" among
the things the API cannot tell us. That was true of `/v2/items`, which
describes a definition, and false of `/v2/account/*`, which reports
`upgrades` and `infusions` per owned stack. The Snapshot tab now names the
runes, sigils, infusions and enrichments in a stack it holds, each with its
own icon and effect text. A socketed component spends the definition's slot
of its own kind, so only the slots left over print an unused-slot line. An
item id whose stacks disagree reports nothing rather than one stack's
reading, because a Snapshot row sums every stack of an id.

A snapshot now reads `/v2/characters/:id/equipment` as well, so an equipped
piece records its sockets the same way a bag stack does, and the Snapshot
tab names the runes and infusions a character is wearing.

Two residuals remain. First, the `(n/m)` set counter the game prints beside
a socketed rune: the denominator is `details.bonuses.length` and is known,
but the numerator is how many pieces of that set are equipped on the
character being played. The endpoint carries what each character wears, but
a snapshot row is one item id summed over every stack of it, with no
character or slot kept, so that count is not available at the point the
tooltip is drawn and none is printed. Second, and following from it, every
tier of a socketed rune's ladder renders inactive - a divergence from the
game whenever the same rune is worn on the character being played.

### 43. Tooltip text wrapping and Blish's 500px cap (audit batches A+B+C)

Blish's own tooltip already bounds width at 500px; what the module adds is
a break point it controls and a hard split for an over-long token, rather
than overflow. The 500px cap is NOT what keeps a tooltip inside the module
window. Full record: `dev/archive/known-issues/2026-08-22-audit-abc.md`.

### 44. Craft/vendor comparability parity

Craft and vendor costs were compared across incommensurable currency mixes.
Fixed in three passes (the fix, an adversarial-review round, and an external
review that found a fourth site). The documented residual: a genuine tie
where both sides' priced-material amounts are identical, which the terminal
fallback cannot break more finely than its existing coin-tie rule. Full
records: `dev/archive/known-issues/2026-08-15-craft-vendor-comparability-parity-fix.md`,
`...-adversarial.md`, `...-external.md`.

**Follow-up (FIXED): a barter line is not worth zero.** That fix's fourth
finding accepted, as a documented limitation, that the terminal fallback
ranked a vendor offer's coin part against a craft route's real cost. Field
use showed why that is not survivable once the unvalued line is an *item*
rather than a wallet currency: Obsidian Heavy Breastplate (101521) was
recommended as a 2g95s10c vendor purchase, that being the price of the
10 Globs of Ectoplasm on Lyhr's offer, which also charges four
account-bound Gifts that folded into no coin at all. The coin part is a
partial accounting; the craft cost it was beating is a complete one. An
offer carrying a barter line can no longer win that comparison, and the
mirror-image case - an offer silently dropped from both tiers when its
comparison value overflowed - now demotes to fallback instead. The
currency-only ranking is unchanged.

Measured against the shipped corpus (14,966 seeded recipes, the real
`ref/vendor_offers.json`, Globs of Ectoplasm priced at 2,916c and nothing
else): the plan for 101521 went from a single `BuyFromVendor` step at
29,160c - the whole tree collapsed away - to `Craft` at 1,874,480c with
630 Globs of Ectoplasm bought and every Gift named with a quantity. The
vendor route stays selectable, and taking it reports 29,160c of coin
*plus* four itemised lines carrying no gold value, with
`VendorHasBarterItemCost` set so no consumer may read the coin figure as
the whole cost.

Two residuals. First, the ranking one above, unchanged. Second, and
larger: a committed vendor decision's coin cost still omits its barter
lines, so a plan total containing one is a LOWER BOUND. That is not for
want of data - 100509 Arcanum of Astral Heartbeat has no recipe but does
have an offer (1 Lesser Vision Crystal, item 49523, itself craftable) -
but because the tree expands recipe ingredients into nodes and vendor
cost lines into leaves, so a cost line is never itself solved. Closing it
means expanding cost lines into priced subtrees, against a cost-line
graph that is measurably cyclic (86094 <-> 91232 among 12 cycles found
before the search was cut short). Coverage:
`tests/TaimisToolbench.Tests/Services/PlanSolverUnpricedBarterOfferTests.cs`.

**Follow-up (FIXED): the second residual, closed by construction.** Cost
lines are now solved. A vendor offer's `Item` cost line with no Trading
Post price gets a per-unit acquisition cost from the same
`PlanSolver.Evaluate` a recipe ingredient gets, run over a quantity-1
subtree, and folds into the offer's real coin cost by the same
`unit x count` multiplication a TP-priced line already used. Only a line
nothing can price at all stays a barter line.

The guard above is no longer what produces the right answer here, and
that was measured rather than assumed: with BOTH the barter guard and the
domination check disabled, 101521 still commits Craft, because the offer's
own coin figure now exceeds the craft it mirrors. Both routes are still
fallback-tier for this item (legendary crafting bottoms out in Spirit
Shards and Karma, which carry no valuation by default), so the terminal
fallback branch is still REACHED - the guards simply no longer decide it.
They remain the answer for the cases pricing genuinely cannot reach. The cost-line graph's cycles are
cut by a visiting set and every id is memoized on first ask, resolved or
not, which makes the work linear in the number of cost items. Design,
bounds and the (i)-not-(ii) display decision: `docs/ARCHITECTURE.md`
section 7.4. A second, price-free check (`Services/VendorOfferDomination.cs`)
reaches the same verdict from the offer's shape alone. Coverage:
`tests/TaimisToolbench.Tests/Services/VendorCostLineExpansionRealCorpusTests.cs`
and `.../VendorOfferDominationTests.cs`.

Measured on the same corpus and the same single price the report above
used (Globs of Ectoplasm at 2,916c): the forced vendor route for 101521
went from 29,160c - the ectoplasm alone - to the craft route's cost plus
exactly 29,160c, which is what the wiki says Lyhr's offer is.

**Gates this model still does not have (REPORTED, not implemented).** The
wiki names three conditions that decide whether a route is available at
all. One is now implemented; the other two are not:

- **"Recipe: Legendary Obsidian Armor"**, the item that unlocks Lyhr's
  exchange (FIXED). `VendorOffer` now carries `UnlockRecipeItemId` and
  `UnlockRecipeId`: the recipe sheet the account must own, and the recipe
  that sheet unlocks. When a committed vendor step's offer names one, the
  sheet appears in Required Recipes, marked Missing! or Learned from the
  same `/v2/account/recipes` set a craft recipe is marked from. The route
  is never hidden and its cost never changes - hiding a route the player
  could actually take is the worse failure. `tools/VendorOfferUpdater`
  fills the fields from the wiki's `Has requirement` property: the value
  must be exactly a `Recipe:` title (`VendorUnlockRequirementParser`), and
  the GW2 API then supplies the recipe from the sheet item's
  `details.recipe_id`. Measured over the full 70,644-row wiki scrape, that
  rule matches 18 rows, all Lyhr's, and nothing else. The two ids are
  deliberately NOT hashed into `offerId`, exactly like `SeasonalFestival`:
  in that same corpus no two offers differ only by an item-valued
  requirement, so no id collides, and back-filling a shipped row leaves
  the id every consumer keys on untouched. `ref/vendor_offers.json` has
  not been re-scraped yet, so every shipped offer still carries both
  fields null.
- **The "Astral Heartbeat" achievement** gating 100509 Arcanum of Astral
  Heartbeat, the one cost item in this chain with no recipe at all. The
  module reads achievement *bits* (`AchievementBitDedupPrePass`,
  `RawIngredient.AchievementId/AchievementBit`) but only to dedup
  one-time reward ingredients; nothing consults account achievement
  completion, and `/v2/account/achievements` is not fetched. The
  achievement is account-wide and one-time, so the failure mode is
  narrow: a plan costs the Arcanum honestly and the player finds it
  unpurchasable until they finish the achievement.
- **Station locality** - Obsidian armour is craftable only at the
  Wizard's Tower Armorsmithing stations, not at every Armorsmith. The
  module models discipline and rating but has no concept of WHERE a
  craft happens, and neither `RawRecipe` nor the API's recipe schema
  carries one; it would be wiki-scraped data, like the vendor corpus.
  Purely informational in effect - the cost is right, the trip is longer
  than the plan implies.

None of the three changes a COST. The recipe gate was implemented by
naming the unlock rather than acting on it; the other two are not, because
acting on either turns a route from available into unavailable, and
getting that wrong hides a route the player can actually take - the
failure this whole item is about, in the other direction.

### 45. W3B: generation progress + rich logging

The plan-strip status board and the phase-by-phase progress reporting a
long solve writes. Full record:
`dev/archive/known-issues/2026-08-08-w3b-generation-progress-rich-logging.md`.

### 46. W4A: Total Cost section redesign

The two formula-band tile rows and the non-coin currency table that replaced
the old coin-total row list, plus the layout arithmetic that moved to
`Services/SummarySectionLayoutMath.cs` rather than into
`PlanContentHeightMath`, which is pinned by expensive evidence. Full record:
`dev/archive/known-issues/2026-08-15-w4a-total-cost-section-redesign.md`.

### 47. W4B: vendor cost-component leaves (the tree dimming rule)

Synthesized cost-component leaves under a vendor node, and the rule for
which tree rows dim. Full record:
`dev/archive/known-issues/2026-08-15-w4b-vendor-cost-component-leaves.md`.

### 48. Recipe-ingestion bug class: missing schema-version parameter

The API's versioned schema keys an ingredient's item id as `id`; the module
and the seeder both parsed the unversioned `item_id` shape, so every
non-Item ingredient type was silently dropped. Re-running the seeder chain
after the fix moved several committed counts, which is why the seed-count
pins in the suite carry drift comments. Full record:
`dev/archive/known-issues/2026-08-15-recipe-ingestion-bug-class-missing-schema-version.md`.

### 49. Opportunity notes: recipe-sheet savings + seasonal vendor tips

The RECIPE-SHEET SAVINGS note and the seasonal-vendor tip, including the
`_offersForRecipeSheetItem` wiring (item 3 of that record) that no test
covered until the end-to-end pipeline test was added. Gate not yet run live.
Full record:
`dev/archive/known-issues/2026-08-16-opportunity-notes-recipe-sheet-savings-seasonal.md`.

### 50. Snapshot item grid

The Snapshot tab's column-count thresholds, derived through the whole chrome
chain rather than copied. Full record:
`dev/archive/known-issues/2026-08-23-snapshot-grid.md`.

### 51. Settings dirty prompt

Blish's `TabChanged` has no pre-change event and no veto: `SetProperty`
assigns, `OnTabChanged` tears the old view down, and only then is the public
event raised - so a tab switch cannot be cancelled, and a dialog button
labelled "Cancel" would promise something it cannot do. The alternatives
that were measured and rejected are in the record. Full record:
`dev/archive/known-issues/2026-08-23-settings-dirty-prompt.md`.

### 52. Click volume slider (click-sound-gain)

Blish's `PlaySoundEffectByName` plays at a game-derived volume capped at
0.4, and zero when the game is quiet or closed - hence the reported
inaudibility - so the module plays Blish's own `button-click.wav` itself.
The accepted divergence from Blish's mute-with-game rule, and the sweep of
controls that still play at Blish's volume (Checkbox, CornerIcon, which play
the click ahead of the base call a subclass would have to skip), are in the
record. Full record:
`dev/archive/known-issues/2026-08-23-click-sound-gain.md`.

### 53. Quality-audit cleanup (findings B1-B15)

Four phases of audit-driven bug fixes and structural dedup. B2: restore-path
lists were null-checked as lists but dereferenced per entry. B3:
`mfData.LoadWarnings` was collected and never logged. B4: a merge counter
incremented against a dictionary that mutated inside its own loop. Full
records: `dev/archive/known-issues/2026-08-17-quality-audit-cleanup-phase-1-four-bug-fixes.md`,
`...-quality-phase2-mechanical.md`, `...-quality-phase3-dedup.md`,
`...-quality-phase4a-tracker.md`, `...-quality-phase4b-bundling.md`,
`...-backlog-cleanup.md`.

### 54. GuildUpgrade ingredient costing and display

The versioned schema (see #48) revealed a `GuildUpgrade` ingredient type
whose ids are a distinct id space from item and currency ids.
`VendorBatchSolver.EvaluateVendorOffers` keyed vendor offers by the raw
ingredient id with no type gate, so a guild-upgrade id could collide with an
item id and be priced as one. Guild-upgrade nodes are now leaves that are
never priced or named as items; full guild-decoration support is out of
scope. Full record:
`dev/archive/known-issues/2026-08-16-guildupgrade-ingredient-costing-display-fix.md`.

### 55. Settings restructure (audit batch G, supersedes B14)

One Save button and one status label for the whole tab, replacing four
per-section Save rows. Also the measured Blish behavior the Snapshot tab
depends on: **"The grid panel holds its unfiltered height"** - Blish's
`Scrollbar` zeroes the scroll position a frame after any content-height
change, so a repack that changes the column count snaps the list to top.
Full record: `dev/archive/known-issues/2026-08-22-audit-g-settings.md`.

### 56. Minimum width raise (1378px)

The shipped minimum window width, derived from glyph ink measured off the
installed Menomonia bitmap fonts rather than guessed. The panel width a
layout test must assert runs through the whole chrome chain including the
`ViewAdapter`, not just the window's content region. See also
`docs/research/minimum-window-width.md`, which is live and editable. Full
record: `dev/archive/known-issues/2026-08-23-min-width-1436.md`.

### 57. Shopping-list row tooltip: scope collision + swallowed hover

Blish resolves a tooltip on the control under the mouse and does not bubble
to the parent, so a row's Labels each need the tooltip as well as the row
Panel. Gate not yet run live. Full record:
`dev/archive/known-issues/2026-08-16-shoplist-have-format.md`.

### 58. W3D: plan persistence across module restarts

Originally, only `ValueOwnMaterials` was restored into its live control -
`RequestItems`, `UseOwnMaterials` and `PriceBasis` were persisted on every
save but ignored on restore, so a restored session's Generate Plan answered
"Add at least one item" until the user retyped their own request (observed
live 2026-08-26, twice). Fixed on the restore-inputs branch: all three now
reseed the input rows, checkbox and dropdown
(`RestoredRequestInputs.BuildRowSeeds` -> `ItemInputRowStrip.RestoreRows`),
with no schema change. Full record:
`dev/archive/known-issues/2026-08-09-w3d-plan-persistence-across-module-restarts.md`.

### 59. Best Path and Clear Overrides are one action

MEASURED: `TreeToolbarCommands.BestPath` and the Overrides chip's clear
action do byte-for-byte the same work - clear the same dictionary, re-solve
- and differ only in the status line they write and the dialog they ask.
Recorded as a finding rather than papered over at the
seam. Full record:
`dev/archive/known-issues/2026-08-23-plan-view-redesign.md`.

### 60. In-game fixes wave 3 (field-fixes-3)

Five independent reports from one live 0.2.3 session: zero-band
retention, scroll anchoring across a re-solve, the click-sound default, the
Mystic Forge UNKNOWN investigation (item 4 - measured: not the build bump,
and mostly not a defect), and the first-load snapshot. Full record:
`dev/records/field-fixes-3.md`.

### 61. Daily craft-cooldown notices (AUDIT ROW 56)

The notice pass keys strictly on `AcquisitionSource.Craft` steps, so an item
that is not a recipe output anywhere in the seed can never raise a notice -
the Craft-step-only limitation. Charged Quartz Crystal was removed from the
seed as dead data that read as covered. Full record:
`dev/archive/known-issues/2026-08-16-audit-row-56-daily-craft-cooldown-notices-three.md`.

### 62. Receipt and what-if captions

`ReceiptCaptionHelper` and the caption split it computes. The deepest
Blish-free seam for this path is `CraftingPlanPipeline`;
`TreeSectionController` is Blish-bound, so a render-path miss beyond that
point cannot surface in a unit test. Full records:
`dev/archive/known-issues/2026-08-16-ui-bundle-wiki-links-snapshot-status-row-receipt.md`,
`dev/archive/known-issues/2026-08-16-gate-investigation-receipt-what-if-captions-value.md`.

### 63. Festival-vendor auto-tagging (partial coverage)

Seasonal tagging covered the known festival vendor list, not a full
re-scrape: thousands of non-festival vendor pages remain untagged, and a
fresh scrape of any merchant recomputes its offer ids. Gate not yet run
live. Full record:
`dev/archive/known-issues/2026-08-16-festival-vendor-auto-tagging-follow-up.md`.

### 64. UI glyphs the shipped font cannot draw

Blish exposes one text face and Menomonia carries 226 codepoints, none of
them geometric. Five escapes shipped outside that set and drew nothing:
Plan History's pin toggle (U+25CF / U+25CB - the glyph WAS the whole
pinned-state model, so pinned and unpinned rows were indistinguishable),
its delete cross (U+2715), and three in the Ranker. A missing codepoint
also advances zero pixels, so neither a layout assertion nor a screenshot
diff catches it. Plan History is fixed here - a Blish `Checkbox` for the
pin, U+00D7 for the delete - and the class is gated by
`docs/font-codepoints.txt` plus the "UI glyph escapes exist in the shipped
font" step in `.github/workflows/tests.yml`. Full record:
`dev/records/glyph-fixes.md`.

**Correction (2026-09-05).** This entry said the Ranker's three escapes
were waived in that step until its own branch landed. They are not waived
any more: the step's `waived` set is empty, and the Ranker's reorder keys
draw `UiGlyphs.CaretUp`/`CaretDown` (`Views/RankerTabContent.cs:1534`),
which are real glyphs in the module's own atlas rather than codepoints
Menomonia lacks. The sentence about the waiver going stale described
machinery that no longer has anything to check. The rest of the entry
stands, with one thing added that was not true when it was written: the
module now ships a five-glyph font of its own (`ref/glyphs.fnt`, U+E100 to
U+E104, named in `Services/UiGlyphs.cs`), so "the shipped font" is two
fonts, and a geometric shape no longer has to be a texture.

### 65. First-paint viewport truncation (the resize that fixed it)

On opening the window, content below a certain point was not drawn at all
until the window got a nudge, which made everything appear. The hosted
view's container was sized from `Panel.ContentRegion` read back off a
panel that had just been resized; Blish refreshes that region only in
`RecalculateLayout`, which `Control.UpdateLayout` skips while the panel's
parent is layout-suspended - as the window is for the whole of its own
layout pass, including the minimum-size clamp that resizes it from inside
that pass. `Services/PanelChromeMath.cs` derives the size instead, so no
read-back can lag. Full record: `dev/records/firstpaint-truncation.md`.

### 66. Content viewport falls short of the window's bottom edge

The scroll viewport ended 74px above the bottom of the window at every
window size, while its top edge sat flush under the title bar. Not a stale
size (#65 covers that one) and not a ratio error: a constant, and the
constant is the window's own bottom margin. Blish reads the `windowRegion`
and `contentRegion` rectangles a window is constructed from as absolute
texture coordinates - `_contentMargin.Y` is `windowRegion.Bottom -
contentRegion.Bottom` - but the pair in `Module.cs` had its vertical terms
authored relative to the window region, so the 15px of clearance intended
below the content came out as 15 + `windowRegion.Top` = 41, and the same
mix-up left the top margin at 0. The premise behind the clearance was also
wrong: background 502049 is 88% opaque at the window region's own last row
and does not fade until seven rows below it. The vertical terms now live in
`Services/WindowSizing.cs` beside the chrome they produce. Full record:
`dev/records/viewport-bottom-margin.md`.

---

## DEFERRED (recorded, not implemented)

Carried over verbatim from the original backlog (full context: the
internal history), plus two additional still-open items folded in
from items 31 and 32 below (marked as such) so this list covers every
genuinely open item, not just the ones originally filed under a
"DEFERRED" heading.

- Dimmed IGNORE toggle's mark: the ignored state falls below the 3:1
  non-text contrast floor. The plain state clears it, and that half is
  closed. Re-measured 2026-09-06, because the first measurement described
  a control that no longer ships. That one was a hand-drawn `#9C7327`
  plate carrying a black glyph, faded by `Control.Opacity`, and it read
  4.90:1 at full strength, 1.87:1 over black and 2.04:1 over the row's
  backdrop. The flat plate, the separate ink colour and the
  `Control.Opacity` fade are all gone, so none of those numbers describes
  anything on screen.

  What ships is a `CloseKeyButton`
  (`Views/Rendering/TreeSectionController.cs:2037`) deriving from
  `RowActionKey`. It blits Blish HUD's own window-close texture
  `button-exit` and multiplies a tint into it: white while the row is
  plain, and `PillColors`' ignore-active amber `#9C7327` while the item is
  ignored. A dimmed row's toggle is always disabled, and a disabled key
  goes through `RowActionKey.Dimmed`, which returns `color * DisabledDim`
  with `DisabledDim` read from `PillColors.DimmedPillFactor` (0.6).
  `Color * float` scales alpha as well as RGB, so the whole key is drawn
  at 0.6 alpha, and Blish's pipeline is premultiplied, so an opaque texel
  lands on screen at `0.6 * texel * tint + 0.4 * backdrop`. The hover
  texture `button-exit-active` never appears in this state, because
  `RowActionKey.Face` picks it only while the key is enabled.

  Method, so the numbers can be recomputed. `button-exit.png` comes out of
  `ref.dat` in the Blish HUD install directory, which is a zip. It is
  32x32 RGBA. The control samples (7, 6) 21x23 of it, and inside that the
  plate is the 16x16 square at (9, 9). Splitting those 256 pixels by sRGB
  relative luminance gives two clusters: 17 mark pixels below 0.02, most
  commonly RGBA (8, 0, 0, 255), and 167 plate pixels above 0.50, most
  commonly (231, 219, 214, 255). Both representatives are fully opaque, so
  it does not matter whether the file is stored premultiplied. Nothing in
  the module paints behind a tree row, because the row panel is
  transparent and so is every panel above it, so the backdrop is the GW2
  window art, asset 502049. Over the middle 60% of that texture it is a
  flat dark grey: median (41, 40, 41), brightest opaque pixel (82, 81,
  82). A hovered row adds `Color.White * 0.07f` on top, which puts the
  median at (55, 54, 55). Ratios are the WCAG formula,
  (L1 + 0.05) / (L2 + 0.05), on sRGB relative luminance.

  The mark against its own plate:

  | key state | over black | over (41,40,41) | over (55,54,55) | over (82,81,82) |
  | --- | --- | --- | --- | --- |
  | plain, enabled | 15.34:1 | 15.34:1 | 15.34:1 | 15.34:1 |
  | plain, dimmed | 5.64:1 | 6.28:1 | 6.42:1 | 6.51:1 |
  | ignored, enabled | 3.91:1 | 3.91:1 | 3.91:1 | 3.91:1 |
  | ignored, dimmed | 2.02:1 | 2.34:1 | 2.43:1 | 2.55:1 |

  An enabled key is fully opaque, so no backdrop reaches it and its row is
  one number. **The plain dimmed key reads 6.28:1 over the real backdrop.
  It clears 3:1 and it clears 4.5:1, so that half of this entry is closed.
  The ignored dimmed key reads 2.34:1 and fails 3:1, so the entry stays
  open on that one state.** The ignored enabled key reads 3.91:1, which
  clears the 3:1 floor a mark is held to; 4.5:1 is the text threshold and
  does not apply to a mark.

  Still not changed, for the reason already recorded: the mark is
  specified black, and inverting it in one state is a design decision
  rather than a correctness fix. One fact behind that has moved. The mark
  is now part of Blish's texture rather than a colour this module picks,
  and the tint multiplies mark and plate together, so there is no ink
  colour left to invert - a white mark would need different art. The rule
  that used to encode the inversion (`PillColors.GlyphColor`) is deleted;
  this entry is where the argument it carried lives.
- Localization (en/de/fr/es via API lang param): deferred as not core
  functionality. Full-milestone scale when picked up.
- Upstream Blish HUD issue/PR for the wheel-delta wrap: REMOVED from the
  backlog entirely (2026-07-22) - no upstream posts are planned. The
  module-side sanitizer stays until a fixed Blish release
  ships, then can be retired at leisure.
- Ignore-pill cascade semantics + own-materials gating divergences
  (#20.4): revisit only on user feedback.
- Multi-item row reordering (gw2e moveRecipe): out of scope per M35.
- Skirmish Merchant-family wiki page split (#28, 18 offers): Skirmish
  Supervisor / Lionguard (Skirmish Merchant) / Mercenary (Skirmish
  Merchant) wiki pages were restructured into /Armor, /Weapons, /Others
  subpages; the items are still sold in-game under the split pages, but
  the seed's merchant-page linkage is now stale-shaped. Missing-offer/
  rename gap for a future re-scrape to follow up; not removed.
  **Correction (2026-09-05): the scrape this rests on never ran
  properly.** The 18 offers were called missing by a scraper that could
  not reach large parts of the wiki at all - see the wiki-drift bullet
  below for the defect and the proof. Whether these pages are split, or
  were simply never asked for, is not settled by anything recorded here.
  Re-check it against a validated scrape before treating the split as the
  cause.
- "Merchant (Untamed Crags)" vendor-page-name mismatch (#28, 1 offer):
  the Hydrocatalytic Reagent / 50 Research Note offer's exact vendor
  page no longer resolves on the wiki (no page, no redirect), while the
  item and cost remain valid via other crafting-material vendors.
  Deferred pending research into whether the page was renamed or the
  original scrape mislabeled the vendor. A third possibility, added
  2026-09-05: the scrape that failed to resolve it is the one described
  in the wiki-drift bullet below, which could not reach large parts of
  the wiki. Re-check against a validated scrape before spending research
  on a rename.
- Wiki-drift missing-offers superset (#28, ~5,400 offers): M37's full
  from-scratch re-scrape (for cap seeding) incidentally picked up new
  Homestead recipes and unrelated vendor page changes beyond the
  stale-offer-sweep scope. Discarded uncommitted; recorded here as a
  candidate for a future dedicated "missing offers" pass.
  **Correction (2026-09-05): this was never wiki drift.** The scrape it
  was measured against had a defect that made whole sections of the wiki
  unreachable, so the difference between two runs was partly the scraper
  changing its mind about what exists, not the wiki changing.
  `WikiSmwClient.PartitionPrefixes` held upper case letters and digits
  only. Semantic MediaWiki's LIKE comparator becomes a SQL `LIKE` over
  `smw_sortkey`, which `MySQLTableBuilder` declares `VARBINARY(255)`, and
  `LIKE` over a binary column compares bytes - so the glob is
  case-sensitive unless the wiki opts into a case-insensitive collation,
  which this one does not. A depth-2 partition therefore asked
  `[[Has vendor::~AS*]]` of names whose second letter is lower case, and
  got nothing it could ever have got. A live run logged `[A]
  sub-partitions done, 35/36 empty prefixes skipped`, naming `AS` among
  them; the one non-empty child was `AC`, and `ACLM-0403` is the only
  merchant in the corpus with two leading capitals. Fixed on branch
  `w25-scraper-error-handling` (pull request 252, open against master at
  the time of writing, not merged): the character set goes from 36 to 73,
  adding lower case and the punctuation that appears in real merchant
  names. The same partition then returned `[As] done: 825 rows in 3
  requests`, and a validated scrape recovered 192 merchants and 6,103
  offers. Until that lands and a full scrape is validated against it, no
  count in this bullet or the two above it is a measurement of the wiki.
- Character/total purchase caps (#28): the wiki's "Has character
  purchase cap" and "Has total purchase cap" SMW properties are real and
  populated (confirmed in M37) but remain deliberately unseeded - the
  module has no account/character concept at all. Left for a future
  milestone's own design pass. ("Has seasonal purchase cap" is no longer
  in this bucket - seeded for Wizard's Vault by item 33, and item 33's
  own later entry records that its runtime consumption was also wired up
  via `TimegatedCapType.Seasonal`, so nothing about seasonal caps remains
  deferred; this parenthetical is corrected from the original record,
  which predated that wiring.)
- Homestead `HomesteadUnlocked` master gate (#24): gw2e has no "do you
  even own Homestead" gate at all; this module echoes that (no gate),
  matching v1's fixed design decision. A prior research draft flagged a
  divergence option (default-off master gate, since this module runs
  in-client for players who may never have touched Janthir Wilds) -
  recorded as a future option pending confirmation, not implemented.
- Homestead Black Market path (#24): 300 purchases of 25/week per
  station, coin-only, tier-independent - confirmed still entirely
  unseeded (the live re-scrape used for tier tagging failed wiki
  game-id resolution for all 30 Black Market rows across the three
  stations). A future milestone could seed these as plain vendor
  offers once that resolution gap is separately investigated.
- **(folded in from item 31c-2, NiceToHave)** Price-cache eviction
  policy: `TradingPostService`'s price cache has no eviction - every item
  id ever priced during the module's process lifetime stays resident
  (refreshed in place, never removed). Bounded and unlikely to matter at
  current GW2 item-id cardinality; a periodic sweep or LRU/size cap
  remains a future candidate.
- **(folded in from item 32)** StyleCop SA1413/SA1516/SA1503: left at
  default severity with a non-trivial pre-existing footprint (253/174/123
  warnings respectively); a future cleanup wave candidate.
- **(M38 `Services/` foldering - recorded 2026-08-25, not executed)** The
  target folder shape named in
  the M38 architecture proposal, section 5 (internal working document) -
  `Services/Pricing/`, `Services/Planning/`, `Services/Persistence/`,
  `Services/Vendor/`, `Services/Layout/`, `Services/Api/` - was never
  built: none of the six directories exists, and `Services/` is 197
  flat files (recounted 2026-09-05; it was 141 when this was written) with
  two subdirectories (`Recipes/`, `Diagnostics/`) that arrived
  for unrelated reasons. Cut for the reason the plan itself flagged:
  `TaimisToolbench.csproj` lists every file explicitly, so each move is
  also a csproj path edit, and the plan's own sequencing note called out
  high merge-conflict potential against the branches then in flight - which
  kept arriving, so the quiet window never came. The payoff is
  navigational rather than behavioral, and `docs/README.md`'s folder map
  now delivers that part at zero merge cost. `docs/ROADMAP.md` declared M38
  complete without recording this; noted here so "complete" keeps meaning
  complete. Still available to a future wave - see `docs/DECISIONS.md`.

Extracted from milestone records during the 2026-08-24 rotation - still
open, so they live here rather than only in the archive:

- **(from the AUDIT ROW 56 record, 2026-08-16)** Shopping-list row tooltip
  caveats: the shopping-list row tooltip still carries neither price-side
  fallback caveat, in either direction. Under `PriceBasis.InstantBuy`, an
  item whose `BuyInstant` side has zero listings falls back to its
  `SellInstant` (buy-order) price and still renders as a flat `Buy` row at
  that coin figure, which reads as instantly fillable when it is not. Also
  recorded there: a `SolverDecision`-level precomputed
  `VendorItemPriceSideFellBack` field could replace the inline `.Any()`
  scan `CraftingTreeBuilder.BuildNode` re-runs per node - declined at the
  time as an unrequested new abstraction on small lists, not as a wrong
  idea. Full record:
  `dev/archive/known-issues/2026-08-16-audit-row-56-daily-craft-cooldown-notices-three.md`.
- **(from the font-and-polish gate, 2026-08-23)** Two sub-cases left to the
  live install, never closed: grip drag-resize outward-and-back (the grip
  path is synthetically uncatchable, a longstanding limitation) together
  with the 3-column snapshot threshold, and spinner-during-*automatic*
  refresh (the keyless sandbox never starts an automatic refresh, so that
  half stands on the shared two-flag wiring plus the manual path's
  evidence). Full record:
  `dev/archive/known-issues/2026-08-23-font-and-polish.md`.
- **(from the tooltip-authenticity gate, 2026-08-23)** Snapshot rows whose
  item has never been in a generated or restored plan get no rich tooltip
  at all. That is correct per the no-stats fallback rule - the name is
  untruncated, so there is nothing to show - but it means the Q5 decision
  (live per-session stat fetch) is only partly realized on the Snapshot
  surface: a bank item never planned gets no in-game-style hover, because
  the rich stat block only exists for items the plan pipeline has fetched.
  Candidate fix, unscheduled: an on-hover metadata fetch through the
  deferred builders plus `ItemMetadataService`'s side-table warm path.
  Recorded deliberate gap, not a regression. Full record:
  `dev/archive/known-issues/2026-08-23-tooltip-authenticity.md`.
- No maximum content width: every table stretches instead of capping
  (2026-08-28, observed in game at a ~2950px window on 3440x1440). Eight
  layout laws - `JustifiedColumnTracks`, `RankerRowLayout`,
  `PlanHistoryRowLayout`, `RecipesColumnMath`, `PlanRelayoutMath`,
  `AboutLayoutMath`, `SettingsSaveBarLayout`, `LogToolbarLayout` - each
  divide the FULL available content width, and nothing caps it. Measured
  at ~2950px: Ranker gate bars grow to ~480px to hold the text "0%", a
  sub-gate label and its value sit ~500px apart, and column headers drift
  far enough from their data to stop reading as labels. Correct from the
  1378px minimum to roughly 1800px, then it inverts into stretch - this is
  the over-correction of the distributed-track law that replaced the older
  packed-left layout, not a regression of it. Candidate fix, unscheduled:
  a per-tab content cap with the surplus becoming margin, since row and
  table tabs buy nothing from extra width while the genuine grids
  (Settings valuations, Snapshot items) legitimately gain a column. In-repo
  precedent for the shape: `LogToolbarLayout.SearchMaxWidth` caps at 400
  and lets the rest become space. Open questions when picked up: the cap
  value (measure the widest legitimate row content rather than guess) and
  whether capped content centres or left-aligns. Deferred 2026-08-28 as a
  distraction.

---

## Milestone record ledger

One line per milestone record, oldest first. The full record - what changed,
the adversarial-review rounds, the measured-constant derivations, the
repo-invariant sweep, the gate checklist and the gate transcript - is in the
linked file, verbatim as it was written. Names in quotes are the exact
strings `.cs`, test, and `ref/` files cite the record by; they are repeated
here so those citations resolve in this file.

Two directories hold the records, for one historical reason: everything
dated 2026-08-24 and earlier was rotated out of this file in a single pass
into `dev/archive/known-issues/`, before per-branch files existed. The
2026-08-25 pass that split the append zone put its records in
`dev/records/`, one per branch, and that is where every record goes now.

- **In-game UX wave (six S-sized display fixes, 2026-08-06)** - gate PASS 2026-08-06.
  `dev/archive/known-issues/2026-08-06-field-test-ux-wave.md`
- **In-game UX wave 2: MysticForge sublabel drop fix (2026-08-06)** - gate PASS 2026-08-06.
  `dev/archive/known-issues/2026-08-06-field-test-ux-wave-2-mysticforge-sublabel-drop-fix.md`
- **Quick wins (wave-3-quick-wins, 2026-08-06)** - gate PASS 2026-08-06.
  `dev/archive/known-issues/2026-08-06-wave-3-quick-wins.md`
- **W3B: Generation progress + rich logging (2026-08-08)** - gate PASS 2026-08-08.
  Cited as: W3B section.
  `dev/archive/known-issues/2026-08-08-w3b-generation-progress-rich-logging.md`
- **W3C: Per-character discipline display (2026-08-08)** - gate PASS 2026-08-08.
  `dev/archive/known-issues/2026-08-08-w3c-per-character-discipline-display.md`
- **W3D: Plan persistence across module restarts (2026-08-09)** - gate PASS 2026-08-16.
  `dev/archive/known-issues/2026-08-09-w3d-plan-persistence-across-module-restarts.md`
- **W4B: Vendor cost-component leaves (2026-08-15)** - gate PASS 2026-08-16.
  Cited as: tree dimming rule.
  `dev/archive/known-issues/2026-08-15-w4b-vendor-cost-component-leaves.md`
- **Craft/vendor comparability parity fix (2026-08-15)** - gate: see record.
  Cited as: "Craft/vendor comparability parity".
  `dev/archive/known-issues/2026-08-15-craft-vendor-comparability-parity-fix.md`
- **Craft/vendor comparability parity fix - adversarial review follow-up (2026-08-15)** - gate: see record.
  `dev/archive/known-issues/2026-08-15-craft-vendor-comparability-parity-fix-adversarial.md`
- **Craft/vendor comparability parity fix - external review, fourth-site finding (2026-08-15)** - gate PASS 2026-08-16.
  `dev/archive/known-issues/2026-08-15-craft-vendor-comparability-parity-fix-external.md`
- **Timestamp date display (all user-facing timestamps gain dates)** - gate PASS 2026-08-16.
  `dev/archive/known-issues/2026-08-15-timestamp-date-display.md`
- **Recipe-ingestion bug class: missing schema-version parameter (2026-08-15)** - gate PASS 2026-08-16.
  Cited as: recipe-ingestion bug class.
  `dev/archive/known-issues/2026-08-15-recipe-ingestion-bug-class-missing-schema-version.md`
- **W4A: Total Cost section redesign (2026-08-15)** - gate PASS 2026-08-16.
  Cited as: W4A entry.
  `dev/archive/known-issues/2026-08-15-w4a-total-cost-section-redesign.md`
- **GuildUpgrade ingredient costing/display fix (2026-08-16)** - gate PARTIAL PASS 2026-08-16.
  Cited as: guildupgrade-ingredients fix, the corrected "Root cause" paragraph.
  `dev/archive/known-issues/2026-08-16-guildupgrade-ingredient-costing-display-fix.md`
- **AUDIT ROW 20/38: TP price-side fallback parity (2026-08-16)** - gate PARTIAL PASS 2026-08-16.
  `dev/archive/known-issues/2026-08-16-audit-row-20-38-tp-price-side-fallback-parity.md`
- **AUDIT ROW 56: daily craft-cooldown notices + three small fixes (2026-08-16)** - gate PASS 2026-08-16.
  `dev/archive/known-issues/2026-08-16-audit-row-56-daily-craft-cooldown-notices-three.md`
- **Currency UX package: defaults, plan-scope pills, value-detail hover (2026-08-16)** - gate PASS with one deferred slice 2026-08-16.
  `dev/archive/known-issues/2026-08-16-currency-ux-package-defaults-plan-scope-pills-value.md`
- **Decision-invariant "Value Own Materials" (VOM, 2026-08-16)** - gate PASS 2026-08-16.
  `dev/archive/known-issues/2026-08-16-decision-invariant-value-own-materials.md`
- **Plan Notes section: excess/reclaim, competency, forge scope (2026-08-16)** - gate PASS 2026-08-16.
  `dev/archive/known-issues/2026-08-16-plan-notes-section-excess-reclaim-competency-forge.md`
- **UI bundle: wiki links, snapshot status row, receipt/what-if captions (2026-08-16)** - gate MIXED 2026-08-16.
  `dev/archive/known-issues/2026-08-16-ui-bundle-wiki-links-snapshot-status-row-receipt.md`
- **Opportunity notes: recipe-sheet savings + seasonal vendor tips (2026-08-16)** - gate not yet run live.
  Cited as: RECIPE-SHEET SAVINGS entry.
  `dev/archive/known-issues/2026-08-16-opportunity-notes-recipe-sheet-savings-seasonal.md`
- **Source selection simplification: competency-aware default + subdued losing pills (2026-08-16)** - gate not yet run live.
  `dev/archive/known-issues/2026-08-16-source-selection-simplification-competency-aware.md`
- **Source selection simplification: adversarial-review fix round (8 findings) (2026-08-16)** - gate not yet run live.
  `dev/archive/known-issues/2026-08-16-source-selection-simplification-adversarial-review.md`
- **Source selection simplification: adversarial-review fix round 2 (5 findings) (2026-08-16)** - gate not yet run live.
  `dev/archive/known-issues/2026-08-16-source-selection-simplification-adversarial-review-2.md`
- **Gate investigation: receipt/what-if captions + value-detail hover (2026-08-16)** - gate investigation outcome recorded 2026-08-16.
  `dev/archive/known-issues/2026-08-16-gate-investigation-receipt-what-if-captions-value.md`
- **Shopping-list row tooltip: scope collision + swallowed hover (shoplist-have-format, 2026-08-16)** - gate not yet run live 2026-08-16.
  Cited as: shoplist-have-format.
  `dev/archive/known-issues/2026-08-16-shoplist-have-format.md`
- **Festival-vendor auto-tagging follow-up (2026-08-16)** - gate not yet run live 2026-08-16.
  `dev/archive/known-issues/2026-08-16-festival-vendor-auto-tagging-follow-up.md`
- **Recorded follow-ups batch sweep (2026-08-17)** - gate not applicable 2026-08-16.
  `dev/archive/known-issues/2026-08-17-recorded-follow-ups-batch-sweep.md`
- **Freeze policy rewrite + PlanContentHeightMath dead-code sweep (high-evidence-zones, 2026-08-17)** - gate PASS.
  Cited as: high-evidence-zones.
  `dev/archive/known-issues/2026-08-17-high-evidence-zones.md`
- **Value-detail hover investigation, pipeline-level follow-up (value-detail-pipeline, 2026-08-17)** - gate not run live this pass.
  `dev/archive/known-issues/2026-08-17-value-detail-pipeline.md`
- **Merged-ceil remainder: largest-remainder apportionment + display-layer narrowing fix (2026-08-17)** - gate not applicable 2026-08-16.
  `dev/archive/known-issues/2026-08-17-merged-ceil-remainder-largest-remainder.md`
- **Quality-audit cleanup, phase 1: four bug fixes (B1-B4, 2026-08-17)** - gate PASS 2026-08-17.
  Cited as: Quality-audit B1, Quality-audit B2, Quality-audit B3, Quality-audit B4, quality-phase1-bugs, the full quality-audit rationale.
  `dev/archive/known-issues/2026-08-17-quality-audit-cleanup-phase-1-four-bug-fixes.md`
- **Tree row tooltip composer extraction + architecture doc corrections (2026-08-17)** - gate not run live this pass.
  `dev/archive/known-issues/2026-08-17-tree-row-tooltip-composer-extraction-architecture.md`
- **Annotation-detection: post-solve advisory-list characterization tests + B8 shape fixes (2026-08-17)** - gate not run live this pass.
  `dev/archive/known-issues/2026-08-17-annotation-detection-post-solve-advisory-list.md`
- **Quality-audit phase 2: safe-mechanical batch (quality-phase2-mechanical)** - gate PASS.
  `dev/archive/known-issues/2026-08-17-quality-phase2-mechanical.md`
- **Comment-minimalism sweep (repo-wide, comment-minimalism-sweep branch)** - gate PASS.
  `dev/archive/known-issues/2026-08-17-comment-minimalism-sweep.md`
- **Quality-audit phase 3: structural dedup (quality-phase3-dedup)** - gate PASS.
  `dev/archive/known-issues/2026-08-17-quality-phase3-dedup.md`
- **Quality-audit phase 4a: PlanSolver best-recipe tracker (B9, quality-phase4a-tracker)** - gate PASS.
  `dev/archive/known-issues/2026-08-17-quality-phase4a-tracker.md`
- **Quality-audit phase 4b: pure parameter bundling (B10, quality-phase4b-bundling)** - gate PASS.
  `dev/archive/known-issues/2026-08-17-quality-phase4b-bundling.md`
- **Sandbox check batch: value-detail closure + partial currency coverage (2026-08-17, live session)** - gate: see record.
  `dev/archive/known-issues/2026-08-17-desktop-gate-batch-value-detail-closure-partial.md`
- **Backlog cleanup batch (B8/B11/B13/B14/B15 + solver ctor hardening, backlog-cleanup)** - gate PASS 2026-08-17.
  `dev/archive/known-issues/2026-08-17-backlog-cleanup.md`
- **Final test polish: three Nice to Have items (final-test-polish)** - gate PASS.
  `dev/archive/known-issues/2026-08-19-final-test-polish.md`
- **Log + Snapshot UX: three small items (log-snapshot-ux)** - gate PASS 2026-08-19.
  `dev/archive/known-issues/2026-08-19-log-snapshot-ux.md`
- **Per-character source checkboxes + character-name search (char-source-search)** - gate PASS on items 2026-08-19.
  `dev/archive/known-issues/2026-08-19-char-source-search.md`
- **Nice to Have batch (nth-cleanup)** - gate PASS 2026-08-19.
  `dev/archive/known-issues/2026-08-19-nth-cleanup.md`
- **Character-name search minimum query length (char-search-min2)** - gate PASS 2026-08-22.
  Cited as: char-search-min2.
  `dev/archive/known-issues/2026-08-22-char-search-min2.md`
- **Audit batch I: log entry readability (audit-i-log)** - gate PASS 2026-08-22.
  `dev/archive/known-issues/2026-08-22-audit-i-log.md`
- **Audit batch F: input flow (audit-f-input-flow)** - gate PASS 2026-08-22.
  `dev/archive/known-issues/2026-08-22-audit-f-input-flow.md`
- **Audit batch G: Settings restructure (audit-g-settings)** - gate PASS 2026-08-22.
  `dev/archive/known-issues/2026-08-22-audit-g-settings.md`
- **Audit batch K: Plan Notes wrapping (audit-k-notes)** - gate PASS 2026-08-22.
  `dev/archive/known-issues/2026-08-22-audit-k-notes.md`
- **Audit batch E: tree interaction honesty (audit-de-cost-tree)** - gate PASS 2026-08-22.
  `dev/archive/known-issues/2026-08-22-audit-de-cost-tree.md`
- **Audit batches A+B+C tier 1 (audit-abc)** - gate PASS 2026-08-23.
  Cited as: "Audit batches A+B+C tier 1".
  `dev/archive/known-issues/2026-08-22-audit-abc.md`
- **Audit batch H: table density (audit-h-density)** - gate PASS 2026-08-23.
  `dev/archive/known-issues/2026-08-22-audit-h-density.md`
- **Tooltip facility (tooltip-facility)** - gate PASS after one gate-found Critical was fixed and re-gated 2026-08-23.
  Cited as: "Tooltip facility", tooltip-facility.
  `dev/archive/known-issues/2026-08-22-tooltip-facility.md`
- **Audit batch J: consistency sweep (audit-j-consistency)** - gate PASS 2026-08-23.
  `dev/archive/known-issues/2026-08-22-audit-j-consistency.md`
- **Cost band restyle (cost-band-restyle)** - gate PASS 2026-08-23.
  Cited as: "Cost band restyle", cost-band-restyle.
  `dev/archive/known-issues/2026-08-23-cost-band-restyle.md`
- **In-game fixes wave 1 (field-fixes-1)** - gate PASS after one gate-found fix 2026-08-23.
  `dev/archive/known-issues/2026-08-23-field-fixes-1.md`
- **Sortable plan tables (sortable-tables)** - gate PASS 2026-08-23.
  Cited as: "Sortable plan tables", sortable-tables.
  `dev/archive/known-issues/2026-08-23-sortable-tables.md`
- **Minimum width raise (min-width-1436)** - gate PASS after two gate-found fixes 2026-08-23.
  Cited as: "Minimum width raise", min-width-1436.
  `dev/archive/known-issues/2026-08-23-min-width-1436.md`
- **Settings dirty prompt (settings-dirty-prompt)** - gate PASS 2026-08-23.
  Cited as: "Settings dirty prompt", settings-dirty-prompt.
  `dev/archive/known-issues/2026-08-23-settings-dirty-prompt.md`
- **Spinner and button feedback (spinner-feedback)** - gate PASS 2026-08-24.
  `dev/archive/known-issues/2026-08-23-spinner-feedback.md`
- **Snapshot item grid (snapshot-grid)** - gate PASS 2026-08-24.
  Cited as: "Snapshot item grid".
  `dev/archive/known-issues/2026-08-23-snapshot-grid.md`
- **Item stat tooltips (item-stat-tooltips)** - gate PASS 2026-08-24.
  Cited as: "Item stat tooltips", item-stat-tooltips.
  `dev/archive/known-issues/2026-08-23-item-stat-tooltips.md`
- **Font bump and decision-round polish (font-and-polish)** - gate PASS 2026-08-23.
  Cited as: "Font bump and decision-round polish", font-and-polish.
  `dev/archive/known-issues/2026-08-23-font-and-polish.md`
- **Tooltip authenticity (tooltip-authenticity)** - gate PASS 2026-08-23.
  Cited as: "Tooltip authenticity", tooltip-authenticity. Its "Infusion
  Slot" wording divergence has since been corrected; see the correction
  under entry 42.
  `dev/archive/known-issues/2026-08-23-tooltip-authenticity.md`
- **Keyboard focus release (kb-focus-release)** - gate PASS 2026-08-23.
  `dev/archive/known-issues/2026-08-23-kb-focus-release.md`
- **Root ignore suppression and the zero-cost band (root-ignore-summary-zero)** - gate PASS with recorded partials 2026-08-23.
  `dev/archive/known-issues/2026-08-23-root-ignore-summary-zero.md`
- **Click volume slider (click-sound-gain)** - gate PASS on the render half 2026-08-23.
  Cited as: "Click volume slider", click-sound-gain.
  `dev/archive/known-issues/2026-08-23-click-sound-gain.md`
- **Plan-view redesign (plan-view-redesign)** - gate PASS 2026-08-24.
  Cited as: "Plan-view redesign", plan-view-redesign.
  `dev/archive/known-issues/2026-08-23-plan-view-redesign.md`
- **Seed integrity: the reseeder silently deleted hand-authored recipes (2026-08-24)** - gate not required (dev-tool and data change; verified by a byte-identical reseed round trip).
  Cited as: seed-integrity.
  `dev/records/seed-integrity.md`
- **Zero-band retention, scroll anchoring, click default, MF recipes, first-load snapshot (2026-08-24)** - gate PASS with two recorded partials 2026-08-25.
  Cited as: field-fixes-3, "field-fixes-3 item 4".
  `dev/records/field-fixes-3.md`
- **App-wide UI consistency wave (2026-08-24)** - gate PASS 2026-08-25.
  Cited as: app-typography.
  `dev/records/app-typography.md`
- **Vendor data refresh, and the stale row it tried to ship (2026-08-25)** - gate not required (dev-tool and data change).
  Cited as: vendor-refresh.
  `dev/records/vendor-refresh.md`
- **Remaining-tabs design pass (2026-08-25)** - gate PASS 2026-08-25.
  Cited as: tab-design-pass.
  `dev/records/tab-design-pass.md`
- **A module-owned button and a shipped glyph font (2026-08-27)** - the record names no gate.
  `dev/records/2026-08-glyph-font.md`
- **Invisible UI glyphs, the guidance behind them, and the gate (2026-08-27)** - gate owed.
  Cited as: glyph-fixes, KNOWN-ISSUES #64.
  `dev/records/glyph-fixes.md`
- **First-paint viewport truncation (2026-08-27)** - gate NOT RUN (no live session available on this branch).
  Cited as: KNOWN-ISSUES #65.
  `dev/records/firstpaint-truncation.md`
- **Barter-item valuation: the vendor offers the solver was throwing away (2026-08-28)** - gate NOT RUN (no live session available on this branch).
  Cited as: barter-item-valuation.
  `dev/records/barter-item-valuation.md`
- **Content viewport falls short of the window bottom (2026-08-28)** - gate NOT RUN (no live game session available on that branch).
  Cited as: KNOWN-ISSUES #66.
  `dev/records/viewport-bottom-margin.md`
- **The Battle Historian: a removed WvW vendor pricing legendary materials at zero (2026-08-29)** - gate not required (dev-tool and data change; verified by a byte-identical round trip and a `--diff-summary` showing 49 removed and nothing else touched).
  Cited as: w5-deadvendors.
  `dev/records/w5-deadvendors.md`
- **Two "Gaeting Crystal" currency ids: one is retired (2026-08-29)** - gate not required (retired currency 39 and item 86094 removed, live currency 77 named).
  Cited as: gaeting-crystal-duplicate-ids.
  `dev/records/gaeting-crystal-duplicate-ids.md`
- **A wave of field-test fixes: viewport, tables, icons, tree rows and dialogs (2026-08-29)** - gate NOT RUN (no live session available on that branch).
  `dev/records/wave6-ui.md`
- **Currency 77 is pinned to currency 28, and two false claims about it are corrected (2026-08-29)** - gate not required (a comment, a test assertion and two doc corrections; no runtime behaviour moves).
  `dev/records/gaeting-equality-pin.md`
- **Seven display fixes from one round of in-game field reports (2026-08-30)** - gate NOT RUN (no live confirmation recorded on that branch).
  `dev/records/wave7-fieldtest.md`
- **Defects in the sticky headers, the viewport cutoff, the tree columns and the dialog title (2026-09-02)** - gate NOT RUN (no live session recorded; the two largest changes are the ones no test can reach).
  `dev/records/m40-review-findings.md`
- **Three unrelated plan-tab defects reported from in-game use (2026-09-03)** - gate NOT RUN (no live check recorded on any commit).
  `dev/records/w13-wave2-fixes.md`
- **Sticky plan-tab headers, headings seated on the icon gutter, and Blish's own close key (2026-09-04)** - gate NOT RUN (no live check recorded on any commit).
  `dev/records/w13-sticky-headers.md`
- **Coin icons seat on the game's baseline, and Total Cost groups its non-coin rows (2026-09-04)** - gate NOT RUN (no live check recorded on that branch).
  `dev/records/w17-coin-seat.md`
- **Development-process residue removed from tracked prose and comments (2026-09-04)** - gate not required (comments, tracked prose and three test method names; no runtime surface).
  `dev/records/cleanup-public-surfaces.md`
- **Total Cost: the inventory rows get a real Have and Needed, and the group labels read as headings (2026-09-05)** - gate NOT RUN (no live check recorded on either commit).
  `dev/records/w20-total-cost-groups.md`
- **The plan tab's input strip reflowed in the middle of a drag (2026-09-05)** - gate NOT RUN (no live session recorded on that branch); superseded by `w40-reflow-on-release` below.
  `dev/records/w22-resize-stretch.md`
- **The recipe tree's IGNORE key moves into the gap between Source and Cost (2026-09-05)** - gate NOT RUN (no live confirmation recorded on that branch); superseded by `w23-ignore-trailing-column` below.
  `dev/records/w18-ignore-x-gap.md`
- **The module's currency tooltip is matched to the game's own (2026-09-05)** - gate NOT RUN (no live confirmation recorded, and the fix is a comparison against the game's own rendering).
  `dev/records/w21-currency-tooltip-match.md`
- **The coin seat hangs the art's disc on the digits, not its shadow (2026-09-05)** - gate NOT RUN (no live session recorded on that branch).
  `dev/records/w19-coin-seat-2px.md`
- **Ranker reorder keys are cut from the remove key beside them (2026-09-05)** - gate NOT RUN (no live check recorded on that branch).
  `dev/records/w24-ranker-button-consistency.md`
- **The Recipe Tree's ignore button gets a fixed column after Cost (2026-09-05)** - gate NOT RUN (no live session recorded on that branch).
  `dev/records/w23-ignore-trailing-column.md`
- **The input strip reflows when the resize drag ends, not when the hand pauses (2026-09-06)** - gate NOT RUN (no live session recorded on that branch).
  `dev/records/w40-reflow-on-release.md`
