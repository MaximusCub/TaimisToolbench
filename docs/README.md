# Documentation index

Everything written down about this module, in one screen. Counts and file
sizes on this page were measured 2026-08-26; run the command beside each if
you want to re-check it.

Start at [`../README.md`](../README.md) for what the module does,
[`../CONTRIBUTING.md`](../CONTRIBUTING.md) for how to build and test it
(including the `nuget restore` step a fresh clone cannot build without), and
[`../CHANGELOG.md`](../CHANGELOG.md) for what shipped when.

## The three tiers

The doc tree is split by *lifetime*, not by topic. Knowing which tier you
are in tells you whether a page is safe to edit.

| Tier | What it is | Editable? |
| --- | --- | --- |
| **Current state** | What is true right now: the tracker, the roadmap, the release protocol, the normative solver spec. | Yes - keep it current. |
| **Durable why** | The reasoning behind mechanisms that outlive any one bug: architecture, decisions, research. | Yes, when the reasoning changes. |
| **Frozen record** | What happened, dated: milestone records, the pre-M38 diary, the M38 plan set, unbuilt proposals. Not in this directory - it is all under [`../dev/`](../dev/README.md). | **No.** Correct these by adding a live note elsewhere that links to them, never by editing the record. |

## Current state

- [`ROADMAP.md`](ROADMAP.md) - where the project is and what is explicitly
  not being built.
- [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md) - the numbered issue catalog, the
  DEFERRED list of genuinely-open items, and the ledger indexing the
  milestone records under [`../dev/`](../dev/README.md). Production `.cs`
  files cite it by number and only by number (`KNOWN-ISSUES #24`), which CI
  checks resolves to a real heading.
- [`RELEASING.md`](RELEASING.md) - what packaging and release actually do
  today, measured, including what still does not exist.
- [`gw2e-parity-spec.md`](gw2e-parity-spec.md) - the normative
  gw2efficiency behavior the solver targets, per rule.
- [`gw2e-considerations.md`](gw2e-considerations.md) - the decision on
  each convergence row: adopt, preserve, or already equivalent.

## Durable why

- [`ARCHITECTURE.md`](ARCHITECTURE.md) - eleven mechanisms that look like
  over-engineering until you read why they exist. Sections 1 and 2 are the
  best artefact in the repo: root causes traced by decompiling the shipped
  Blish HUD binary (the `ContinueWith`/ThreadPool path that puts `Build()`
  off the main thread, the `ControlCollection` lock correction, the
  `WheelDelta` `N*120-65536` sign-unwrap bug).
- [`api-client-contracts.md`](api-client-contracts.md) - per host, what the
  published contract asks of a client (User-Agent, rate limits, `maxlag`,
  `Retry-After`, concurrency, caching), which rules this repository meets
  and where, and which it breaches. Every rule cites the page that states
  it; every limit not published anywhere cites the request that measured
  it. Also says which parts of the runtime API path are Blish HUD's rather
  than ours.
- [`DECISIONS.md`](DECISIONS.md) - designs that were considered and
  rejected, with the reasoning and a link to the full record. Kept separate
  from ARCHITECTURE so that document can stay a map rather than a defence.
- [`research/`](research/) - dev-time investigations into gw2efficiency and
  the GW2 Wiki, plus one study of this module's own layout
  ([`research/minimum-window-width.md`](research/minimum-window-width.md),
  which derives the shipped 1378px minimum from glyph ink measured off the
  installed Menomonia bitmap fonts). It has
  [its own index](research/README.md).

## Frozen record

The whole third tier lives outside this directory, in
[`../dev/`](../dev/README.md), and has [its own index](../dev/README.md).
Nothing under `dev/` is current documentation; it is the dated engineering
record, kept because the measurements in it cannot be re-derived cheaply.
In short:

- [`../dev/records/`](../dev/records/) - one milestone record per branch.
  Where new records land.
- [`../dev/archive/known-issues/`](../dev/archive/known-issues/) - 69 older
  records, rotated out of `KNOWN-ISSUES.md` verbatim in one 2026-08-24
  pass, before per-branch files existed.
- the pre-M38 fix-pass diary, under the same item numbers - internal
  history, not published in this repository.
- [`../dev/proposals/`](../dev/proposals/) - design write-ups. Four describe
  tabs that shipped and are cited from source as the reasoning behind them;
  the rest were never built. That directory's README says which is which.

## Where the code lives

`TaimisToolbench.csproj` is a classic non-SDK project that lists every
file explicitly, so folders are navigation only - they are not namespaces
and not compilation units. File counts measured with `ls <dir>/*.cs | wc -l`.

| Folder | What lives there | Open these first |
| --- | --- | --- |
| `Models/` (57) | The data shapes passed between layers: the plan result, the display tree, the view model the renderers read. | `PlanViewModel.cs`, `CraftingTreeNode.cs`, `CraftingPlanResult.cs`, `CurrencyValuation.cs` |
| `Services/` (209, flat) | Every piece of logic in the module: the solver, pricing, the offline-seed loaders, the pure layout arithmetic, and the text/decision composers the views render. | `PlanSolver.cs`, `CraftingPlanPipeline.cs`, `PlanViewModelBuilder.cs`, `VendorBatchSolver.cs`, `PlanContentHeightMath.cs` |
| `Services/Recipes/` (10) | Recipe cache stores, the committed seed readers behind them, and the corpus probe that verifies them against the live build. | `RecipeCacheSerializer.cs`, `OverlayRecipeCacheStore.cs`, `RecipeCorpusVerifier.cs` |
| `Services/Diagnostics/` (2) | Plan-generation phase timing, summarised into the plan's debug log by `CraftingPlanPipeline`. | `PlanTimingAnalyzer.cs`, `PlanPhaseTimingSummary.cs` |
| `Views/` (65) | The Blish-bound layer: one file per tab, the window, and the two main-thread primitives. | `CraftingPlanView.cs` (5,185 lines - the plan tab), `MainView.cs` (Snapshot), `SettingsTabContent.cs`, `MainThreadMarshal.cs` |
| `Views/Rendering/` (46) | Per-section renderers, the two seams they reach the view through, plus the shared drawing primitives (fonts, coin rows, rarity colors, tooltips). | `TreeSectionController.cs`, `ITreePlanHost.cs`, `SummarySectionRenderer.cs`, `UiFonts.cs`, `CoinCurrencyRenderer.cs` |
| `Contracts/` (1) | The item-search seam (`IItemSearchProvider` plus its result type) and nothing else - a directory for one file. | `IItemSearchProvider.cs` |
| `tools/` | Offline console apps that produce `ref/`. Never run by the module. | `VendorOfferUpdater/`, `TaimisToolbench.RecipeSeeder/`, `MysticForgeSeeder/` |
| `ref/` | Committed seed data the module reads at runtime, produced by `tools/`. | `vendor_offers.json` (14.8MB, one line), `recipes_seed.json` |
| `tests/` | Three test projects; see `CONTRIBUTING.md`. | `TaimisToolbench.Tests/`, `VendorOfferUpdater.Tests/` |
| `Module.cs` (root) | Blish HUD entry point: `Initialize`, tab wiring, service construction, `Unload`. | - |

**The one rule that makes all of this work:** `Models/` is entirely
Blish-free, and so is `Services/` apart from three deliberate adapters at
the edge - `Gw2AccountSnapshotService.cs` and `Gw2AccountRecipeClient.cs`
(the GW2 API, reached through Blish's `Gw2ApiManager`) and
`ModuleSettings.cs` (Blish's settings store). Everything else compiles with
no reference to Blish HUD, XNA or `Gw2Sharp` - measured 2026-08-26,
`grep -rlE 'using (Blish_HUD|Microsoft\.Xna|Gw2Sharp)' --include='*.cs'
Models Services` returns those three files and nothing else. `Views/` is
the Blish-bound layer.

That boundary is why the whole solver, all the pricing arithmetic, and
every pixel constant in the plan view are unit-tested with no rendering
harness and no running game, and why `CONTRIBUTING.md`'s STANDING RULE
routes each new tree-row feature's pure computation into a `Services/`
composer before it is wired into a view. The three adapters are named in
test files only inside comments; nothing under `tests/` references them.

## Glossary

- **M-NN** - a milestone (M14 through M39 are cited in live docs; earlier
  ones only in the archive). A milestone is one themed wave of work, ending
  in a live sandbox verification.
- **WP-NN** - a work package inside the M38 cleanup wave specifically,
  WP-01 through WP-29, defined in the M38 cleanup plan (internal working
  document). No other milestone uses WP numbering.
- **KNOWN-ISSUES #N** - an entry in the numbered catalog in
  [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md). Cited from `.cs` files as an anchor
  to the investigation behind a piece of code.
- **Gate** - the live sandbox session that verifies a milestone against the
  running game. Each milestone record ends in one `Gate: PASS/FAIL/...`
  line; a claim with no gate behind it says so.
- **Pinned by expensive evidence** - code whose behavior rests on
  measurements that cost a lot to take (a live trace, a decompilation).
  Changeable, but only with characterization tests written against the
  current behavior first. See the policy note in
  [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md).
- **ADOPT / PRESERVE / EQUIVALENT** - the per-row verdicts in
  [`research/gw2e-convergence-matrix.md`](research/gw2e-convergence-matrix.md):
  change to match gw2efficiency, keep this module's deliberate divergence,
  or already the same.
