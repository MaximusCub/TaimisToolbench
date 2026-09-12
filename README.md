# Taimi's Toolbench

[![tests](https://github.com/MaximusCub/TaimisToolbench/actions/workflows/tests.yml/badge.svg)](https://github.com/MaximusCub/TaimisToolbench/actions/workflows/tests.yml)

A [Blish HUD](https://blishhud.com/) module that aims to be your in-game crafting
companion for Guild Wars 2: plan the most efficient way to craft anything from
simple items to legendaries, rank a wishlist by how close you are to finishing
each item, search your whole account for an item, and keep your shopping list and
crafting steps in a pop-out window while you play. At its core is a crafting-plan
**solver** that answers "what's the cheapest way to get N of this item", node by
node, the way [gw2efficiency](https://gw2efficiency.com)'s crafting calculator
does. Type in an item, and the module walks its full recipe tree and decides -
for every single ingredient, not just the top-level item - whether to craft it,
buy it off the Trading Post, buy it from a vendor, or use what you already own,
then lets you override any of those decisions by hand and see the total cost
update live.

This is not an inventory viewer with a calculator bolted on. The solver is the
product; the account-data tab exists to feed it (and to let you search/inspect
what you're carrying).

## What it does

- **Per-node buy/craft/vendor decisions.** Every ingredient in the recipe tree is
  independently solved, not just the requested top-level item - a deep Legendary
  precursor tree gets a real decision at every intermediate node.
- **Interactive override pills.** Each node shows its available sources (CRAFT /
  TP / VENDOR / IGNORE) as clickable pills; click one to force a different
  decision and the plan re-solves and re-totals immediately, with tree-wide
  economics (parent costs, currency totals) recomputed correctly. "Best Path",
  "Craft All", and "Buy All" presets are one click away, and Ignore lets you
  drop a subtree from the plan entirely (e.g. "I already have this, stop
  costing it").
- **Owned-materials aware.** Toggle "Use Own Materials" to have the solver treat
  what's already in your bank, shared inventory, material storage, and
  character bags as a competing free source against buying or crafting.
- **Multi-item batches.** Plan several items in one pass; shared ingredients are
  pooled across the batch and vendor purchase batching/rounding is computed
  once for the combined total rather than per item.
- **Sell-side economics.** Where relevant, the plan shows what selling
  intermediate materials would net you (Trading Post sell value net of fees) so
  you can see the real opportunity cost of consuming a material instead of
  selling it.
- **Vendor caps and timegates.** Vendor offers carry weekly/seasonal purchase
  caps and Homestead-refinement/timegate data (scraped from the wiki ahead of
  time - never at runtime); the plan warns when what you're asking for exceeds
  what a vendor will actually sell you in the relevant window.
- **Multiple price bases.** Solve against Buy Orders or Sell Listings, matching
  how you actually intend to acquire the item.

The full normative behavior this module targets is written up in
[`docs/gw2e-parity-spec.md`](docs/gw2e-parity-spec.md) (researched from
gw2efficiency's own open-source calculation libraries at dev time only -
gw2efficiency is never called by the running module). The durable "why" behind
the trickier pieces of the implementation (scroll/relayout handling, the
merged vendor-batch math, and so on) is in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
[`docs/README.md`](docs/README.md) indexes the rest - the roadmap, the
issue tracker, the release protocol, the research notes, and a map of which
folder holds what. Released versions are listed in
[`CHANGELOG.md`](CHANGELOG.md); what is and is not planned is in
[`docs/ROADMAP.md`](docs/ROADMAP.md).

Underneath that sits the full engineering record - every milestone record,
the pre-M38 fix-pass diary, the closed audits and the designs that were
never built - in [`dev/`](dev/README.md). It is dated evidence rather than
documentation, and it is kept in-repo so a `grep` for a constant finds the
session that measured it.

## Screenshots

The Total Cost band - materials value, what your own materials cover, and
the actual coin cost, with the currencies the plan needs:

![Crafting plan Total Cost band with currency requirements](docs/images/plan-total-cost.png)

Recipe tree with per-node decision pills (Craft / TP / Vendor / Ignore) and
per-row costs:

![Crafting plan recipe tree with decision pills](docs/images/plan-recipe-tree.png)

Item tooltips mirror the in-game look - measured from live captures, down
to the font, the background texture and cursor-adjacent placement:

![GW2-authentic item tooltip on a recipe tree row](docs/images/item-tooltip.png)

Account snapshot - items and currencies across bank, material storage,
shared inventory and every character, with search and source filters:

![Account snapshot with per-source item locations](docs/images/snapshot.png)

Plan History - every generated plan kept with its cost and date; open a
saved plan exactly as it was, or re-solve it at today's prices:

![Plan History with View, Open and Re-solve](docs/images/plan-history.png)

## Tabs

- **Snapshot** - your account's items and wallet currencies across bank, shared
  inventory, material storage, and every character's bags, with search and a
  source filter (Bank / Material Storage / Character / etc.).
- **Crafting Plan** - the solver described above: enter an item and quantity,
  generate a plan, and interact with the tree.
- **Log** - the module's own diagnostic log, with a "Copy" button for
  attaching recent log lines to a bug report.
- **Plan History** - every generated plan, recorded: view a frozen summary,
  reopen the exact saved plan, or re-solve the same request at today's
  prices. Rows can be pinned; Settings caps how many are kept.
- **Crafting Ranker** - a persistent watchlist ranking tracked items by how
  close each is to craftable, with higher-priority items claiming shared
  coin, currencies and materials first.
- **Settings** - price basis, owned-materials toggle default, Homestead
  Refinement efficiency tiers, and diagnostic switches.
- **About** - module version, author/contributors, source link, and the
  on-disk data directory (useful for attaching `snapshot.json`/`status.json`
  to a bug report).

## Installing

1. Download `TaimisToolbench.bhm` from the
   [Releases page](https://github.com/MaximusCub/TaimisToolbench/releases).
2. Drop it into your Blish HUD installation's `modules` folder
   (`Documents\Guild Wars 2\addons\blishhud\modules` in a default install).
3. Start or reload Blish HUD and enable the module, then authorize an API key
   with the permissions listed below.

Releases are built and published automatically from a `v*` tag by
[`.github/workflows/release.yml`](.github/workflows/release.yml), from the
same Release/x64 build a developer would run locally. If the Releases page is
empty, no tag has been pushed since that workflow landed - build it yourself in
the meantime, following the build instructions in
[`CONTRIBUTING.md`](CONTRIBUTING.md) and copying the resulting
`bin\x64\Release\TaimisToolbench.bhm` as in step 2 above.

There is no Blish HUD module-repository listing yet, so the download is manual.
[`docs/RELEASING.md`](docs/RELEASING.md) records exactly what the packaging
step does and does not cover.

### GW2 API key permissions

When Blish HUD prompts you to authorize an API key for this module, it needs:

- **account** - account info and coin balance
- **characters** - character list and inventory access
- **inventories** - bank, shared inventory, and material storage
- **wallet** - wallet currency data
- **unlocks** *(optional)* - lets the plan show which required recipes you've
  already learned

## Building from source / contributing

Windows only. The module targets .NET Framework 4.8 inside Blish HUD's XNA
host, and CI runs on `windows-latest`. From a fresh clone:

```
nuget restore TaimisToolbench.sln
dotnet build TaimisToolbench.csproj -p:Platform=x64
dotnet test TaimisToolbench.sln
```

The restore step is not optional, and **`dotnet restore` will not do it**:
this is a classic `packages.config` project, which only `nuget.exe` restores
(download it from <https://www.nuget.org/downloads>, or install it with
`winget install Microsoft.NuGet`). Skipping it fails the build with
`The missing file is packages\BlishHUD.1.3.0\build\BlishHUD.targets`, which
is what a missing restore looks like rather than a broken checkout.

`dotnet test TaimisToolbench.sln` runs all three test projects, the same
set CI runs; see [`CONTRIBUTING.md`](CONTRIBUTING.md) for the per-project
commands, project structure, and pull request expectations.

## How this was built

This module is AI-assisted and human-reviewed, and it says so on purpose: the
commit history carries `Co-Authored-By` trailers rather than hiding them.

What that is worth depends entirely on the process around it, so here is the
process, all of it checkable from this repository:

- **Nothing ships untested.** Three test projects - 3,084 for the module plus
  233 across the seeder and vendor-updater tools (measured 2026-08-26; the
  badge above is the live answer) - run on every pull request and every push
  to `master` on
  [CI](https://github.com/MaximusCub/TaimisToolbench/actions/workflows/tests.yml),
  and again inside the [release workflow](.github/workflows/release.yml),
  which will not publish a tagged build until they pass. That same CI run also
  builds the shipping `Release|x64` configuration and checks the `.bhm` came
  out, so the packaging path is exercised before a tag is ever cut rather than
  for the first time on the file players download.
  The tests are Blish-free - CI fails the build on a Blish HUD or Gw2Sharp
  reference under `tests/` - and they run against real production code paths,
  no contract mirrors and no fake I/O. That second half no machine can check:
  it is stated as an invariant in [`CONTRIBUTING.md`](CONTRIBUTING.md) and
  checked by a human at review, on a checklist line every pull request has to
  tick.
- **Risky changes are characterized before they are made.** Where a rewrite
  touches behavior the suite does not already pin, the pinning test is written
  and committed against the *old* implementation first. The 14.8MB vendor
  dataset, for instance, is pinned byte-for-byte against the writer that
  produces it.
- **The build is warning-clean, and the compiler enforces it.**
  `TaimisToolbench.csproj` sets `TreatWarningsAsErrors`, so a build that
  prints anything failed. The StyleCop rules not yet satisfied are a named
  list in that file's `<NoWarn>` - 26 rule IDs, each a bounded cleanup job -
  and [`CONTRIBUTING.md`](CONTRIBUTING.md) states the rule that the list
  only ever shrinks.
- **The repo's other rules are enforced by CI, not memory.** The `invariants`
  job in [`tests.yml`](.github/workflows/tests.yml) fails the build on
  non-ASCII characters in source, a `.cs` file missing its `<Compile Include>`
  entry, a doc or comment citing a file that does not exist, and a broken
  relative markdown link. Comment length is ratcheted per file
  ([`docs/comment-budgets.txt`](docs/comment-budgets.txt)): a file may not
  gain over-length comment blocks beyond its pinned count.
- **UI changes are checked in the running game**, not asserted from a diff, and
  what was actually observed is recorded: each milestone record under
  [`dev/records/`](dev/records/) ends in an explicit `Gate:` line - PASS,
  FAIL, or "not required" with the reason - naming the live sandbox session
  that verified it, and a claim with no gate behind it says so.
- **Every change is adversarially reviewed** against a written checklist in
  [`CLAUDE.md`](CLAUDE.md) - null inputs, empty collections, cancellation, API
  failure, race conditions, invariant violations - with findings classified and
  the blocking ones fixed before the change lands.
- **The docs record what was measured, not what was intended.**
  [`docs/RELEASING.md`](docs/RELEASING.md) is explicit that it describes "the
  current, actual state of packaging and release - not an aspirational process",
  and [`docs/KNOWN-ISSUES.md`](docs/KNOWN-ISSUES.md) keeps the failures.

## License

[MIT](LICENSE)

### Third-party assets

UI glyphs are rasterized from [Bootstrap Icons](https://github.com/twbs/icons),
(c) 2019-2024 The Bootstrap Authors, MIT License. The notice ships inside the
module alongside the artwork - see
[`ref/THIRD-PARTY-NOTICES.txt`](ref/THIRD-PARTY-NOTICES.txt). Regenerate the
atlas with `python3 tools/build-glyph-font.py --fetch --out-dir ref`.
