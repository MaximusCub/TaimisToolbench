# TaimisToolbench - Roadmap

> Supersedes all prior roadmap revisions (the detailed per-milestone
> planning template this document used through M17 is preserved in git
> history; this revision is a short, current-state summary instead).

## Status

- **M37 - gw2efficiency parity: complete as specified by the July spec,
  with a ratified convergence queue in flight.** The crafting-plan solver
  targets full behavioral parity with gw2efficiency's crafting calculator
  for every node (buy/craft/vendor decisions, owned-materials reduction,
  multi-item batches, sell-side economics, vendor purchase caps/timegates,
  Homestead Refinement efficiency tiers, achievement-bit ingredient
  dedup). The normative spec this targets is
  [`docs/gw2e-parity-spec.md`](gw2e-parity-spec.md). The August 2026
  convergence audit
  ([`docs/research/gw2e-convergence-matrix.md`](research/gw2e-convergence-matrix.md)
  plus the per-row decisions in
  [`docs/gw2e-considerations.md`](gw2e-considerations.md)) then found a
  queue of genuine ADOPT gaps against the live calculator; those are what
  the current wave of branches is implementing.
- **M38 - cleanup wave: the work landed; one result did not hold.**
  Structural cleanup across the whole codebase: test-infrastructure
  consolidation, analyzer/style hygiene, the `CraftingPlanView` God-class
  decomposition into per-section renderers plus a `TreeSectionController`
  (see [`docs/ARCHITECTURE.md`](ARCHITECTURE.md) section 5),
  pipeline/solver structural splits, coverage gaps closed, and this
  documentation restructure. One deliverable from the wave's own plan was
  **not** built - the `Services/` folder split - and is recorded in the
  DEFERRED list in [`docs/KNOWN-ISSUES.md`](KNOWN-ISSUES.md) with the
  reason.

  The decomposition itself did not stay done. `CraftingPlanView` was
  ~2,802 lines when the wave closed on 2026-07-23 and 5,281 lines by
  2026-08-25 - past the ~4,802-line baseline the refactor started from.
  It was 5,615 lines on 2026-09-12. Nothing in CI measures it. A per-file
  line budget was tried and removed. Each entry was raised to the file's
  current size by the same commit that grew the file. Read the measured
  figure in ARCHITECTURE.md section 5, not a status word here.
- **M39 - core tabs shipped.** Snapshot search/filter, the Log tab (with
  the JSONL log store and rotation), the Settings tab, and the About tab
  all landed and are in normal use - see the README's "Tabs" section for
  what each one does today.
- **Current phase - releases for in-game testing + the convergence-adoption wave.**
  Since 2026-08 the project ships stamped builds to a live Blish HUD
  install: `v0.2.0` (2026-08-23) through `v0.2.4` (2026-08-25, the
  app-wide typography and layout pass), each with a `CHANGELOG.md` entry
  and a matching git tag pushed to origin (measured 2026-08-26:
  `git ls-remote --tags origin`). `v0.3.0` (the Ranker and Plan History
  tabs, the recipe-cache staleness rework) is staged - manifest and
  CHANGELOG bumped, tag not yet pushed, pending the in-game testing pass -
  see [`CHANGELOG.md`](../CHANGELOG.md) and
  [`docs/RELEASING.md`](RELEASING.md). Work arrives as one branch per
  milestone, each ending in a live sandbox check; the feedback loop is
  in-game testing the deployed build. The queue is driven by the
  convergence matrix's ADOPT rows and by in-game findings, not by a
  fixed milestone plan.

## What's not done

- Further tab feature proposals (deeper snapshot search and similar
  ideas) live in-repo at [`dev/proposals/`](../dev/proposals/).
  Being written down does not make them committed roadmap items: `d1`
  (snapshot/about/settings) is partly implemented, and `d3` (plan
  history) and `d4` (crafting ranker) shipped as the Plan History and
  Crafting Ranker tabs (PRs #190, #185 - staged for v0.3.0). Treat any
  tab beyond the seven that exist as unplanned until it actually ships.
- Localization (en/de/fr/es) is deliberately deferred - see the DEFERRED
  list in `docs/KNOWN-ISSUES.md`.

## Where the detailed history lives

- Milestone-by-milestone bug fixes and live-verification records, in three
  tiers: [`docs/KNOWN-ISSUES.md`](KNOWN-ISSUES.md) is the current-state
  tracker (numbered catalog, the open list, and a ledger of every rotated
  milestone record); the full milestone records live one file each under
  [`dev/archive/known-issues/`](../dev/archive/known-issues/); and the pre-M38
  fix-pass diary is internal history (not published in this repository).
- The original per-phase project plans (Phase A through the M14-M17
  navigation/visual-parity work, each with full scope/acceptance-criteria
  templates) are archived at
  [`dev/archive/plans/2026-02-15/`](../dev/archive/plans/2026-02-15/).
- Durable architecture rationale: [`docs/ARCHITECTURE.md`](ARCHITECTURE.md);
  designs proposed and deliberately not built:
  [`docs/DECISIONS.md`](DECISIONS.md).
- Everything else is indexed from [`docs/README.md`](README.md).

## Backlog

- **First-run experience (NUX / welcome).** Requested 2026-08-24. A new
  user opens the module to empty tabs and no explanation of what an API
  key buys them; the first-load snapshot now fills the data, but nothing
  yet introduces the tabs, the plan flow, or what to do without a key.
