# Contributing to Taimi's Toolbench

Thanks for your interest in contributing. This document covers what you
actually need to build, test, and submit a change to this repository. It is
intentionally short; the deeper "why" behind some of the module's less
obvious code lives in `docs/` (see `docs/KNOWN-ISSUES.md` and
`docs/gw2e-parity-spec.md`).

## Prerequisites

- **Windows.** The module targets .NET Framework 4.8 inside Blish HUD's XNA
  host; CI runs `windows-latest`. There is no Linux/macOS build.
- .NET SDK 8.0 (the main module targets .NET Framework 4.8 / MonoGame via
  Blish HUD, but the SDK used to drive `dotnet build`/`dotnet test` is 8.0 -
  see `.github/workflows/tests.yml`)
- On Windows, if `dotnet` is not already on your `PATH`, the SDK installer
  normally places it at `C:\Program Files\dotnet\dotnet.exe`.
- **`nuget.exe`** - required, and not interchangeable with `dotnet restore`;
  see Building below. Download it from <https://www.nuget.org/downloads> and
  put it on your `PATH`, or install it with `winget install Microsoft.NuGet`.

## Building

```
nuget restore TaimisToolbench.sln
dotnet build TaimisToolbench.csproj -p:Platform=x64
```

**The restore step is separate and mandatory on a fresh clone.** This is a
classic `packages.config` project (24 pinned packages, every reference
resolved through a `<HintPath>packages\...` entry) and `packages/` is
gitignored, so nothing is on disk until it is restored. The `dotnet` CLI has
never supported `packages.config` restore: both `dotnet restore` and
`dotnet msbuild -t:restore` print a success-shaped "Nothing to do. None of
the projects specified contain packages to restore." and leave the build
broken. Only `nuget.exe restore` populates `packages/`. Without it the build
fails with:

```
error : This project references NuGet package(s) that are missing on this
computer. ... The missing file is packages\BlishHUD.1.3.0\build\BlishHUD.targets.
```

`.github/workflows/tests.yml` runs the same `nuget restore
TaimisToolbench.sln` step for the same reason.

The `Platform=x64` argument is required - the project only defines output
paths for the `x64` platform (see `TaimisToolbench.csproj`'s
`Debug|x64`/`Release|x64` property groups). Building without it fails
outright with "The BaseOutputPath/OutputPath property is not set", not a
silently-working-but-wrong build.

A successful build also produces a `.bhm` file next to the built DLL (e.g.
`bin\x64\Release\TaimisToolbench.bhm`) - see `docs/RELEASING.md` for how
that packaging step works and what it currently does and does not cover.

### The build emits zero warnings, and that is enforced

`TaimisToolbench.csproj` sets `TreatWarningsAsErrors`, so a build that
prints anything at all is a build that failed. **Do not report warning
counts in a commit message.** There is nothing to count and nothing to
compare against a remembered number; if the build succeeded, it was clean.

This replaces a convention that ran for months: commit bodies carried a
hand-maintained tally ("1745 -> 1744", "1782 pre-existing warnings, none
new"). That is a person doing a compiler's job, it does not survive being
handed to someone new, and by the time it was removed the last quoted
figure was off by roughly 940.

Getting there meant admitting what the analyzer noise actually was. The
rules still outstanding sit in that project's `<NoWarn>`: 21 StyleCop rule
IDs covering 1,385 module diagnostics (2,754 across the solution), down
from 38 rules and 2,723 (measured
2026-08-26). Each ID is a bounded piece of work, not a judgement that the
rule is wrong. To take one on: delete the ID, fix what the build then
reports, and commit the two together - the diff is confined to that one
rule and the build proves it stays fixed. **The list only ever shrinks.**
Suppressing a new rule to make a build pass is not a use of this list.

The same gate covers the test and tool projects: each SDK-style project
imports `StyleGate.props`, which references the same ruleset and carries a
copy of the module's `<NoWarn>` list (the module cannot import it - see the
comment in that file). Taking a rule on therefore means deleting its ID
from both `TaimisToolbench.csproj` and `StyleGate.props` in the same
commit, and fixing what every project then reports.

The three largest remaining are `SA1117` (252, parameter layout), `SA1401`
(210, public fields) and `SA1201` (190, member ordering). None has a
mechanical fix that leaves the code better than it found it, which is why
they are still here and the whitespace rules are not.

To **run** what you built rather than develop against it, build in Release
for `x64` and copy `bin\x64\Release\TaimisToolbench.bhm` into your Blish
HUD installation's `modules` folder, then reload Blish HUD. Players do not
need any of this - `README.md`'s Installing section points them at the
`.bhm` attached to a GitHub Release, which is the same artifact built by
`.github/workflows/release.yml`.

## Testing

```
dotnet test TaimisToolbench.sln
```

That runs all three test projects, which is what CI runs. To run one at a
time (the same three steps, in the same order, as
`.github/workflows/tests.yml`):

```
dotnet test tests/TaimisToolbench.Tests/TaimisToolbench.Tests.csproj -c Release
dotnet test tests/TaimisToolbench.RecipeSeeder.Tests/TaimisToolbench.RecipeSeeder.Tests.csproj -p:Platform=x64 -c Release
dotnet test tests/VendorOfferUpdater.Tests/VendorOfferUpdater.Tests.csproj -c Release
```

Running only the first one is the trap worth naming: the golden-vector suite
that pins `tools/VendorOfferUpdater/VendorOfferHasher.cs` - the SHA-256
`offerId` keying every row of the 15MB `ref/vendor_offers.json` - lives in the
third project, so a change to the hasher passes locally and fails CI.
`TaimisToolbench.Tests` and `TaimisToolbench.RecipeSeeder.Tests` target
`net48`; `VendorOfferUpdater.Tests` targets `net8.0`, so a solution-level
run needs both the .NET 8 SDK and .NET Framework 4.8 on the machine.

The suite runs on every pull request; see the CI badge at the top of
`README.md` for its current state. A few rules the test suite enforces
and that any new test must follow:

- **Tests exercise real production code paths.** No contract-mirror tests,
  no fake logic tests, no fake file I/O tests. Storage-backed behavior (e.g.
  `SnapshotStore`, `StatusStore`) is tested against the real store classes
  pointed at a temporary directory, not a hand-rolled substitute.
- **Tests are Blish-free.** Nothing under `tests/` may reference Blish HUD,
  `BlishHUD.exe`, `Gw2Sharp`, or any UI code. The module's business logic
  (`Services/`, `Models/`, `Contracts/`) is deliberately factored so it can
  be exercised without a running Blish HUD host; keep it that way when
  adding new logic.

CI (`.github/workflows/tests.yml`) restores, builds the whole solution with
`-p:Platform=x64`, and runs this test project plus the two tool suites
(`tests/TaimisToolbench.RecipeSeeder.Tests`,
`tests/VendorOfferUpdater.Tests`). It runs on every pull request and on
pushes to `master`; a docs-only change skips the Windows build job, and the
`invariants` job still reports. That job fails the build on non-ASCII `.cs`
source, an em-dash in source or config, a Blish HUD or `Gw2Sharp` reference
under `tests/`, a `.cs` file missing its `<Compile Include>` entry, and a
live doc citing a source path that does not exist.
`.github/workflows/release.yml` builds and publishes the `.bhm` on a `v*`
tag; see `docs/RELEASING.md`.

## Project/Solution Structure

- `TaimisToolbench.csproj` is a **non-SDK-style** (classic) project and
  lists every source file explicitly via `<Compile Include="...">`. If you
  add a new `.cs` file, you must add a matching `<Compile Include>` entry
  yourself - the build will simply not see a file that isn't listed. This
  is unusual compared to modern SDK-style projects (which glob `**/*.cs`
  automatically); it is a consequence of Blish HUD's module template, not
  a stylistic choice, so please don't try to "fix" it as part of an
  unrelated change. The `invariants` CI job diffs the `<Compile Include>`
  list against the files on disk in both directions, so a forgotten entry
  fails the build instead of silently dropping a file from compilation.
- `tools/` contains standalone offline utilities (see below) that are
  separate console projects, most of them SDK-style, each with its own
  README where one exists. Not all of them are projects: `tools/sandbox`
  holds a PowerShell script that pins the interface scale the screenshot
  sandbox renders Blish HUD at, and `tools/sandbox/README.md` explains
  why one is needed.
- `TaimisToolbench.sln` lists every project, including the tools - if you
  add a new tool project, add it to the `.sln` too (a project that only
  exists on disk but isn't in the `.sln` is easy to miss entirely, which
  happened to `MysticForgeSeeder` before this was fixed).

## Code Style

- Allman brace style (opening brace on its own line) for C#.
- **Everything is `internal` unless Blish HUD itself must see it. Tests reach
  internals via `InternalsVisibleTo`.** This is a leaf module assembly:
  nothing on disk links against `TaimisToolbench.dll`, and Blish
  discovers exactly one type from it - the `Module` subclass, found by MEF
  through its `[Import("ModuleParameters")]` constructor. So `Module` is the
  only `public` type in the shipped assembly, and a `public` you add
  anywhere else is a promise to a caller that does not exist.
  `Properties/AssemblyInfo.cs` grants `InternalsVisibleTo` to the test
  project and to the two developer tools that reference the module
  (`TaimisToolbench.Harness`, `TaimisToolbench.RecipeSeeder`), so
  `internal` costs nothing in testability or tooling. It buys the compiler's
  dead-code detection back: an unreferenced `internal` type is a warning,
  an unreferenced `public` one is silence. Nested members keep whatever
  accessibility their enclosing type needs; the rule is about top-level
  types.
- **ASCII-only in `.cs` source.** Do not paste raw Unicode into code,
  comments, or string literals. If Unicode must be shown at runtime (UI
  glyphs, item names, etc.), use an escape (e.g. `"\u25BC"`) or data
  returned by the GW2 API. CI fails the build on any non-ASCII character in
  a tracked `.cs` file.
- No em-dashes in source, comments, or config - use a plain hyphen (`-`) or
  double-hyphen (`--`) instead. CI fails the build on one, in any tracked
  `.cs`, `.csproj`, `.yml`, `.json` or `.config` file outside `ref/` (which
  holds generated GW2 data, not authored text).
- **A comment must carry something the code and `git log` cannot.** The
  test: could a reader get this from the code in front of them plus one
  `git log -S`? If yes, the comment is noise. What survives is the external
  fact - vendor binary behaviour, a Windows constant, a GW2 API quirk, a
  measurement, a derivation, an invariant a caller can violate. What does
  not survive: refactor provenance ("moved verbatim out of X", "was private
  static, now internal static", "no logic changes"), review rebuttals
  ("this is deliberate, not an oversight"), bug-discovery narratives, and
  session-local jargon ("per the brief", "directive B", milestone codes) -
  those belong in the commit message, or in `docs/` when a future reader
  needs them. Keep a contiguous comment block to roughly 12 lines of
  inline `//` prose, or 20 of `///` XML doc, which spends lines on tag
  structure the prose does not: past that, the invariant a caller must
  not violate stays inline and the narrative moves to
  `docs/ARCHITECTURE.md` under a section the comment then points at.
  That length is counted, not just stated:
  [`docs/comment-budgets.txt`](docs/comment-budgets.txt) pins how many
  over-length blocks each tracked `.cs` file carries - the two categories
  counted separately - and CI fails when a count goes up. After a
  cleanup, re-pin in the same commit with
  `python3 tools/comment-budgets.py > docs/comment-budgets.txt`.
  State a rule once, canonically, and point at it from the
  other sites rather than restating it - a comment claiming to "mirror X
  exactly" promises a synchronisation nothing enforces.
- **Cite documents by repo-relative path.** A `.md` filename in a `.cs`
  comment must resolve against the tree (`dev/proposals/d2-log-system.md`,
  not `d2-log-system.md`); CI fails the build on a citation that resolves
  nowhere, because a bare name sends a reader hunting for a file that may
  never have been committed.
- **XML doc where it earns its place; never as an obligation.** A
  `<summary>` belongs on a type or member whose contract is not already
  obvious from its name, and on anything carrying a measurement or an
  invariant a reader must not re-derive. It is the norm here rather than
  the exception - 200 of the 248 production `.cs` files carry at least one,
  1,247 blocks across 12,118 `///` lines (measured 2026-08-25) - but it is
  **not** mandated, which is why `TaimisToolbench.ruleset` leaves SA1600
  and SA1602 off. Do not add `<summary>Gets the item id.</summary>` to a
  property called `ItemId`; a prose `//` at the decision point is worth
  more than a doc comment that restates the signature.
- **Constructor-injected dependencies get a null-guard**
  (`_sink = sink ?? throw new ArgumentNullException(nameof(sink))`), even
  where the sole production call site cannot pass null. Every section
  renderer, `ViewAdapter`, `SettingsTabContent` and `FrameTicker` follow it:
  without the guard a null surfaces later as a `NullReferenceException`
  inside a render closure, pointing at the wrong code.
- Private fields are `_camelCase`; the naming convention is enforced by
  `.editorconfig`, and `TaimisToolbench.ruleset` suppresses SA1309 so the
  analyzer does not fight it.
- Follow the patterns already established in neighboring files rather than
  introducing new structure for a small change.
- Keep edits focused; avoid unrelated refactors or formatting churn in the
  same change as a behavior fix.
- **STANDING RULE (Recipe Tree row/pill features):** every new tree-row or
  decision-pill feature extracts its pure text/decision computation into a
  tested, Blish-free `Services/` composer BEFORE wiring it into
  `Views/Rendering/TreeSectionController.cs` - the proven pattern
  (`DecisionPillPlanner`, `ValueDetailTooltipBuilder`,
  `PillSubduingEvaluator`/`PillSubduingTooltipBuilder`,
  `ReceiptCaptionHelper`, `CurrencyDisplayResolver`,
  `TreeRowTooltipComposer`, ...). The rule also applies retroactively:
  `Services/TreeRowShapePlanner.cs` and
  `Services/PillTooltipTextComposer.cs` are the row-shape and pill-prose
  decisions extracted back out of `RenderTreeNode`/`RenderDecisionPills`
  after the fact. Only the actual Blish-bound
  Panel/Label/event-wiring belongs in `TreeSectionController` itself. This
  is not a suggestion to eventually split `TreeSectionController` into a
  stateful/stateless pair - that split was proposed and rejected, with the
  reasoning in `docs/DECISIONS.md`; the class stays a
  single owner with a single lifetime, and every new feature's logic grows
  the `Services/` side of that boundary, never a second stateful class.

## Where seed/reference data comes from

The module ships several JSON files under `ref/` (recipes, item names,
vendor offers, Mystic Forge recipes) that it reads at runtime. **None of
these are fetched by the module itself at runtime** - `gw2efficiency` is
research-only (see `docs/gw2e-parity-spec.md`/`docs/research/`) and is
never called from module code, and the GW2 Wiki is never queried by the
running module either. All of it is produced ahead of time by the offline
tools under `tools/` and committed to the repo:

- `tools/TaimisToolbench.RecipeSeeder` - queries the official GW2 API
  (`api.guildwars2.com`) to build `ref/recipes_seed.json`,
  `ref/recipe_search_seed.json`, and related files.
- `tools/VendorOfferUpdater` - scrapes the GW2 Wiki (Semantic MediaWiki API)
  plus the official GW2 API to build `ref/vendor_offers.json`. See
  `tools/VendorOfferUpdater/README.md` for the full two-pass workflow.
- `tools/MysticForgeSeeder` - scrapes the GW2 Wiki to build
  `ref/mystic_forge_recipes.json`.

If you're changing anything that touches pricing, recipes, or vendor data,
check whether the relevant tool under `tools/` needs to be re-run rather
than hand-editing a `ref/*.json` file.

These files are marked `-diff -merge linguist-generated` in
`.gitattributes`, so git and GitHub will not try to render a 14.8MB single
line (measured 2026-08-25) into your terminal or a pull request view. That also means a refresh's
diff tells a reviewer nothing, so a pull request carrying a `data(vendor):`
commit **must include the `--diff-summary` output in its body** -
`tools/refresh-vendor-data.sh` prints it at the end of a refresh. See
`docs/RELEASING.md`. The small hand-maintained files under `ref/`
(`vendor_offer_exclusions.json`, `acquisition_hints_seed.json`,
`daily_cooldown_items.json`) are deliberately left diffable, because those
you do edit by hand and a reviewer must read.

`ref/recipe_sheet_items.json` was on that list until 2026-09-07. It is now
generated by `tools/VendorOfferUpdater --build-recipe-sheet-map` and holds
1,788 entries, so do not edit it by hand. Rerun the tool instead. It reads
`ref/vendor_offers.json`, asks `api.guildwars2.com/v2/items` about every
item a vendor sells, and keeps the ones that unlock a crafting recipe. It
touches no wiki page.

## Pull Requests

- A pull request template exists at `.github/PULL_REQUEST_TEMPLATE.md` -
  please fill it in rather than deleting the sections.
- Describe what changed and why, and how you validated it (build/test
  commands run, and any manual verification for UI-affecting changes).
- Keep commits logically grouped (e.g. a refactor, a behavior change, and
  its tests as separate commits) rather than one large mixed commit, where
  practical.
- **Commit trailers.** This project is AI-assisted and does not hide it:
  where a change was co-authored by an AI agent, keep the `Co-Authored-By`
  trailer. Do **not** add a session-URL trailer (the harness default
  `Claude-Session:` line). 1,054 existing commits carry one (measured
  2026-08-25), and they
  resolve to private session IDs that 404 for every reader - a constant
  paste with no provenance value. Attribution stays; dead links do not.

- If your change touches pricing, currency comparisons, or item/vendor IDs
  shown anywhere in the UI, note that this repo has a couple of
  non-negotiable invariants: IDs are internal-only and must never be
  displayed to users, and pricing logic must preserve multiple sources and
  avoid comparing incompatible currencies. A reviewer will flag a PR that
  violates either.

## Reporting Bugs

Please use the bug report template under `.github/ISSUE_TEMPLATE/` when
opening an issue. If you can attach a log tail, the module's in-app "Log"
tab has a "Copy" button that copies recent log lines to the clipboard -
that's the quickest way to get diagnostic output into a bug report.
