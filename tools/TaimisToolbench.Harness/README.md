# TaimisToolbench.Harness

A console harness for exercising the crafting-plan pipeline
(`Services/CraftingPlanPipeline.cs`, `Services/PlanSolver.cs`) directly,
without running inside Blish HUD. Useful for checking plan output, timing,
and cache behavior after a solver/pipeline change without a manual
in-game/in-Blish test pass for every iteration.

It references `TaimisToolbench.csproj` directly (`ProjectReference` with
`SetPlatform=x64`), so building it also builds the main module.

## Quick Start

```
dotnet run --project tools/TaimisToolbench.Harness/TaimisToolbench.Harness.csproj -- --profile 1
```

## CLI Reference

| Flag | Default | Description |
|------|---------|-------------|
| `--profile <n>` | required | Which built-in item profile to plan for (see below) |
| `--iterations <n>` | 1 | Re-run the plan this many times (for warm-cache timing) |
| `--live` | off | Use the real GW2 API clients instead of null/offline stubs |
| `--raw` | off | Print raw plan output |
| `--print-cache-stats` | off | Print recipe cache hit/miss counters after planning |
| `--clear-overlay-cache` | off | Clear the on-disk overlay recipe cache before running |
| `--dump-tree` | off | Dump the full recipe tree, not just the top-level decision |
| `--classify` | off | Bucket every terminal of the solved tree by the route the solver committed to, then print per-item counts, the unpriceable terminals by name, and a cross-item aggregate |
| `--force-craft-root` | off | With `--classify`: when the plan's target is itself buyable and the solver buys it outright, re-solve with the root pinned to Craft so there is a tree to classify at all |
| `--homestead-tier <0\|1\|2>` | pipeline default (tier 0) | Homestead Refinement efficiency tier, applied uniformly to Fiber/Metal/Wood (the live module exposes these three independently via the Settings tab; this harness applies one tier to all three for simplicity) |

## Profiles

Built-in profiles are small, fixed item lists defined in `Program.cs`
(`GetProfileItems`) - useful because they give a reproducible before/after
comparison point for a solver change:

| Profile | Item(s) |
|---|---|
| 1 | Gift of Fortune (plus Zojja's Claymore when `--live` is set) |
| 2 | Exordium |
| 3 | Klobjarne Geirr - reaches Homestead Refinement via Gift of the Homesteader; pair with `--homestead-tier` to compare decisions/quantities across tiers |
| 10-27 | One representative item per legendary class (weapon Gen 1/2/3, Janthir spear, PvE/raid/WvW/PvP/fractal armour, aquabreather, trinkets, back items, rune, sigil, relic) |
| 30 | Every profile 10-27 item in one process - the sweep `--classify` aggregates over |

## Terminal Classification (`--classify`)

A terminal is any node the solver did NOT decide to craft: Craft is the only
decision that expands into real children, so a non-Craft node is where the
plan stops and the player has to go and get something. Each is bucketed as
Trading Post / vendor-for-coin / vendor-for-a-valued-currency /
vendor-with-an-unvalued-cost-line / UnknownSource / Have / currency leaf.

Run it `--live`. Offline, the null price client proves an empty Trading
Post, so every tradeable node force-crafts down to raw materials and lands
in Unknown or vendor; the same 18-item sweep produces 765 terminals live
and 13,292 offline, with 0 Trading Post terminals in the offline pass. The
offline numbers are a useful control for "what does the corpus alone
represent", not a measurement of what the module shows a player.

Findings from the first full sweep are written up in
`dev/proposals/legendary-harness-findings.md`.

## Data Files

Without `--live`, the harness loads the same `ref/*.json` seed files the
shipped module uses (vendor offers, recipe search/recipe seeds) if they're
present next to the built harness executable, and writes its own working
cache under a local `harness_data/` folder (created next to the built
executable, not under `ref/` or the repo root).

## When to Re-run

- After any change to `PlanSolver`, `CraftingPlanPipeline`, or the recipe
  cache stores, to sanity-check plan output/timing before writing or
  updating a formal test.
- With `--live` when validating against current, real GW2 API prices
  rather than the offline seed data.

## Fetch Profiler (`--fetch-profile`)

Measures how long an account snapshot takes to fetch, for several ways of
fetching one, and what each way costs in requests and bytes. It answers the
trade between request count and end-to-end time; it changes nothing in the
module.

```
GW2_API_KEY=<key> dotnet run --project tools/TaimisToolbench.Harness/TaimisToolbench.Harness.csproj -- --fetch-profile
```

The key is read from `GW2_API_KEY` only. It is never written to the console,
to the result file, or into a recorded URL. `--dry-run` prints the schedule
and needs no key.

| Flag | Default | Description |
|------|---------|-------------|
| `--fetch-profile` | - | Selects the profiler; every flag below needs it |
| `--dry-run` | off | Print the schedule and the request budget, then exit |
| `--per-minute <n>` | 55 | Requests allowed in any trailing 60 seconds |
| `--max-requests <n>` | 750 | Hard stop for the whole experiment |
| `--characters <n>` | 6 | Roster size `--dry-run` assumes; a real run probes it |
| `--only <names>` | all | Comma-separated config names to run, matched exactly |
| `--runs <n>` | per config | Override every selected config's run count |
| `--compare` | off | Fetch the account both ways and diff the two snapshots |
| `--compare-runs <n>` | 3 | How many times `--compare` fetches both ways |
| `--out <dir>` | `%LOCALAPPDATA%\TaimisToolbench\fetch-profile` | Where raw per-run timings are written |

Raw timings go to one JSON-lines file per invocation, outside the repo, so
two runs can be compared.

### What it varies

- The narrow per-character endpoints the module uses today, at several
  character fan-outs and several connection limits.
- The same, without the barrier that makes the character phase wait for
  every account-wide response.
- `/v2/characters?ids=all`, one request for the whole roster.
- `/v2/characters?page=N&page_size=N`, for a few page sizes.
- A roster padded past the real one, to stand in for a larger account. A
  padded entry re-requests a name already in the list, so those rows are a
  simulation and are named as one.

### Comparing the old shape against the new one (`--compare`)

`--compare` fetches the same account twice in one process, once with the
three narrow calls per character the module used to make and once with the
paged full record it makes now, and reports every field the two snapshots
disagree on. An empty report is the evidence that the change kept the
snapshot.

The paged side runs `Services/CharacterPagePlan.cs` and
`Services/CharacterRecordProjection.cs`, the code the module ships, so a
clean result is evidence about the module rather than about a copy of it.
The narrow side keeps this project's own projection, because that shape no
longer exists in the module.

Measured on 2026-09-08 across 5 comparisons: identical every time, 1039 item
rows, 53 wallet rows, 9 discipline rows, matching row for row and in the same
order.

### Rate discipline

The profiler holds itself under `--per-minute` by waiting between runs and
never inside one, so a throttle is never charged to an approach as slowness.
It stops at `--max-requests`. Both exist because the account it measures is
somebody's, and Blish HUD may be refreshing the same account at the same
time.

### How close it is to the module's own path

The point of the profiler is which approach is faster **through the module**,
not which endpoint answers fastest, so the measured path is the module's
wherever the module's code can be called outside Blish HUD.

In the loop, and real:

- Gw2Sharp 1.7.4, the version `packages.config` pins, with its middleware and
  its Newtonsoft deserialization into the same `Gw2Sharp.WebApi.V2.Models`
  types the module receives.
- `MemoryCacheMethod`, the cache method Blish HUD builds its connection with.
- `Services/Gw2ApiConnectionLimit.cs`, called where the module calls it.
- `Services/CharacterSnapshotCollector.cs`, for both the fan-out and the fold
  that decides when a harvest is incomplete.
- `Services/BoundedConcurrency.cs`, for the character fan-out and for the
  bulk-detail fan-out.
- `Services/EquipmentLocationPolicy.cs` and `Services/AccountItemIndex.cs`,
  so an equipped legendary is dropped from the item rows and counted as
  armory-held exactly as the module drops and counts it.
- The three resolve passes: `/v2/items` and `/v2/skins` in 200-id chunks four
  in flight, and `/v2/currencies`. A run ends with a real
  `Models/AccountSnapshot.cs` whose rows carry names, icons and rarities, so
  the wall clock is time to a snapshot the module could render.

Out of the loop, and why:

- **`Services/Gw2AccountSnapshotService.cs` itself.** It takes a Blish HUD
  `Gw2ApiManager`, which cannot be constructed outside a loaded module, so
  its orchestration is repeated in `SnapshotFetchShapes.cs` rather than
  called. Everything that orchestration calls is the real thing.
- **Blish's token bucket.** Blish wraps its connection in
  `TokenComplianceMiddleware(new TokenBucket(300, 5))`, shared across every
  module and Blish's own traffic. A snapshot spends 13 to 55 requests against
  a 300 burst, so a bucket that starts full does not throttle one; a bucket
  already drawn down by other modules would, and equally for every approach.
- **Retries.** `Gw2AccountSnapshotService` retries a failed call; the
  profiler does not, so it reports a failure where the module would have
  spent more time and recovered.
- **A warm cache.** Every run builds its own `MemoryCacheMethod`, so every
  run is a cold fetch. That is the case a timer refresh hits, and it is what
  makes the request counts below real network traffic rather than cache
  hits.

### Reading the output

`requests` is what reached the network, counted below Gw2Sharp's cache: a
cached read never reaches the counter, so a run's count is proof of what it
actually sent. `wire KB` is the compressed bytes the server sent, counted
before decoding; `body KB` is the JSON after decoding, which is what the
deserializer walks.
