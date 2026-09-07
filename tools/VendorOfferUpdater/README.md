# VendorOfferUpdater

Offline tool that scrapes vendor-sold items from the [GW2 Wiki](https://wiki.guildwars2.com/) Semantic MediaWiki API, resolves currency names via the official [GW2 API](https://api.guildwars2.com/), and writes a `vendor_offers.json` baseline file consumed by the Blish HUD module.

## Quick Start

The easiest way to refresh vendor data is the wrapper script. Requires **Git Bash on Windows** and the **.NET 8 SDK**.

```bash
# Full refresh - wiki scrape + currency resolution (~15 min)
./tools/refresh-vendor-data.sh

# Currency resolution only - uses cached wiki data (~3 min)
./tools/refresh-vendor-data.sh --pass2-only
```

The script builds the tool, runs the appropriate passes, and prints a summary with file size and offer count.

## Two-Pass Architecture

A full refresh takes ~15 minutes because the GW2 Wiki rate-limits API requests. To keep individual runs manageable and allow recovery from interruptions, the tool splits work into two passes:

**Pass 1 - Wiki scrape** (`--skip-item-resolution --tag-seasonal-festivals --merge-into ref/vendor_offers.json`):
Queries all vendor items from the wiki via Semantic MediaWiki `action=ask`. Saves raw results to `ref/wiki_vendor_cache.json` (merges with any existing cache). Re-resolves each vendor page's seasonal festival tag. Merges into the existing `ref/vendor_offers.json` (rather than replacing it wholesale) and generates a partial output without item-based currency resolution.

**Pass 2 - Currency resolution** (`--resolve-item-currencies-only --merge-into ref/vendor_offers.json`):
Loads the cached wiki results from `ref/wiki_vendor_cache.json`. Resolves item-based currency names (e.g. "Mystic Coin", "Glob of Ectoplasm") to GW2 game IDs by querying the wiki. Merges into `ref/vendor_offers.json` again and generates the final output.

If Pass 1 is interrupted (safety limit, rate-limit block, timeout), the wiki cache preserves all partial results. Re-running Pass 1 merges new results into the existing cache. Once the cache is complete, Pass 2 can be run independently.

## Seasonal Tag Preservation

Both passes of the wrapper script (`tools/refresh-vendor-data.sh`) pass `--merge-into ref/vendor_offers.json` against their own output file (a self-merge: the current on-disk baseline is read before it is overwritten - see `Program.MergeIntoBaseline`'s doc comment). This is required, not optional, for the default refresh to be safe:

- Without `--tag-seasonal-festivals` on Pass 1, freshly-queried `WikiVendorResult` rows never carry a seasonal value, and `Program.MergeWikiCache` overwrites any existing cache entry for a re-queried page in full - including a previously-resolved seasonal value, which gets nulled out with nothing to restore it.
- Without `--merge-into` on either pass, that pass's `finalOffers` is simply `uniqueOffers` (Program.cs's own "merge into an existing baseline, if requested" step is skipped entirely) - a full, `--query`-less refresh's fresh batch touches every merchant, so this wholesale-replaces the whole dataset with whatever this run resolved, dropping any offer (tagged or not) this run's own scrape/resolution did not reproduce.
- `Program.MergeIntoBaseline`'s protected-merchant and OfferId-collision rules both prefer whichever side of a collision carries a `SeasonalFestival` tag, so even a page whose wikitext fetch transiently fails mid-refresh (left uncached, warned, and retried on the next run - see `ResolveSeasonalFestivalValuesAsync`) does not lose a previously-shipped tag: the merge carries the baseline's tag forward onto the surviving fresh row. This holds for every merchant, not just protected ones: an ORDINARY merchant's replaced baseline rows are harvested for their tags (keyed by OfferId and by content, to survive a hash-format migration) before being dropped, and any harvested tag is applied onto that merchant's untagged fresh rows - never overwriting a fresh row that already carries its own tag.

Running Pass 1 or Pass 2 manually (not via the wrapper script) without both flags reproduces the wholesale-replace behavior above - always pass `--tag-seasonal-festivals` and `--merge-into <output-path> <output-path>` together for any refresh that should preserve existing seasonal tags.

## Prerequisites

- .NET 8 SDK
- Internet access (no API key needed - both endpoints are public)
- Git Bash on Windows (for the wrapper script)

## CLI Reference

```bash
dotnet run --project tools/VendorOfferUpdater/VendorOfferUpdater.csproj -- [options] [output-path]
```

The tool auto-detects the repository root by walking up the directory tree looking for a `.git` folder, then writes to `ref/vendor_offers.json` relative to that root. Pass an explicit path as the first positional argument to override.

### Options

| Flag | Default | Description |
|------|---------|-------------|
| `--skip-item-resolution` | off | Skip item-based currency resolution; generate partial output and save wiki cache |
| `--resolve-item-currencies-only` | off | Load wiki cache instead of scraping; resolve currencies and generate final output |
| `--query <condition>` | `[[Sells item::+]]` | Override the SMW query condition (e.g. `[[Has vendor::"Miyani"]]`) |
| `--max-depth <n>` | 2 | Max prefix partition depth for SMW queries. See "Splitting an oversized query" below |
| `--max-requests <n>` | 2000 | Safety limit on total HTTP requests |
| `--max-runtime <minutes>` | 30 | Safety limit on total execution time |
| `--delay <ms>` | 250 | Delay between wiki API requests (minimum enforced: 200 ms) |
| `--max-attempts <n>` | 5 | Attempts one wiki request gets before its section is recorded unresolved. Counts the first try |
| `--allow-coverage-drop` | off | Write the dataset even though the coverage check objected. See "Coverage check" below |
| `--recheck-misses` | off | Drop every remembered item-name miss from `ref/item_id_cache.json` so this run asks the wiki about those names again. See "Remembered misses" below |
| `--dry-run` | off | Print query plan only, no HTTP calls to wiki |
| `--tag-seasonal-festivals` | off | Fetch each distinct vendor page's wikitext and tag offers whose page carries a `{{Temporary\|...\|seasonal=}}`/`{{Temporary\|...\|event=}}` value matching one of the six known GW2 festivals. Opt-in: adds one extra HTTP request per distinct, not-yet-cached vendor page (see `--max-seasonal-pages`) |
| `--diff-summary <old> <new>` | off | Read-only. Reports what changed between two vendor datasets and exits without touching the wiki, the API, or any file. See below |
| `--max-seasonal-pages <n>` | 500 | Self-healing per-run budget on how many new (uncached) vendor pages `--tag-seasonal-festivals` will fetch in one run - if there are more uncached pages than the budget, it fetches up to the budget, saves the cache, and leaves the rest for a subsequent run (only a value `<= 0` is rejected outright) |

### Environment Overrides (wrapper script)

The `refresh-vendor-data.sh` script accepts these environment variables:

| Variable | Default | Used in |
|----------|---------|---------|
| `MAX_RUNTIME` | 20 | Pass 1 `--max-runtime` |
| `MAX_REQUESTS` | 4000 | Pass 1 `--max-requests` |
| `MAX_DEPTH` | 2 | Pass 1 `--max-depth` - raise it if a run reports a partition truncated at max depth |
| `DELAY_PASS1` | 250 | Pass 1 `--delay` |
| `DELAY_PASS2` | 1500 | Pass 2 `--delay` |
| `MAX_SEASONAL_PAGES` | 2500 | Pass 1 `--max-seasonal-pages` - sized to cover a from-scratch sweep of the measured ~2,088 distinct vendor pages in one run |
| `ALLOW_COVERAGE_DROP` | unset | Set to any value to pass `--allow-coverage-drop` to both passes |
| `RECHECK_MISSES` | unset | Set to any value to pass `--recheck-misses` to Pass 2 |

Example:

```bash
DELAY_PASS1=500 MAX_RUNTIME=30 ./tools/refresh-vendor-data.sh
```

### `--diff-summary`: making a vendor refresh reviewable

`git diff ref/vendor_offers.json` reports `1 insertion(+), 1 deletion(-)`. The
payload is one 14.8MB line, so that single "changed line" is the entire dataset
that prices every vendor in the game. A reviewer has no way to tell a three-row
price correction from a scrape that dropped half the merchants.

```bash
dotnet run --project tools/VendorOfferUpdater/VendorOfferUpdater.csproj -- \
    --diff-summary /path/to/old_vendor_offers.json ref/vendor_offers.json
```

```
=== Vendor offer diff: old.json -> new.json ===
  offers:   59,414 -> 59,414 (+0)
  added:    1
  removed:  1
  repriced: 1
  retagged: 1
  rehashed: 0

--- Repriced (1) ---
  Quartermaster (Drizzlewood Coast) | item 93817 x1 | 1000x currency 58, 200x item 93371, daily cap 1 -> 1999x currency 58, ...
```

`refresh-vendor-data.sh` snapshots the baseline before it overwrites it and runs
this automatically at the end of a refresh, so the summary is already printed by
the time you go to write the PR body. `docs/RELEASING.md` requires it there for
any `data(vendor):` commit.

A note on how it classifies: `offerId` is a SHA-256 over the offer's whole
content, so a price change does not modify a row - it deletes one hash and
creates another. Reported literally that would turn every repricing into two
unrelated hex strings. The report re-pairs them by (merchant, output item) and
shows the old and new cost side by side; only rows with no counterpart are
reported as genuine additions or removals. `seasonalFestival` is the one field
outside the hash, so a change to it keeps the `offerId` and is reported as a
retag. Counts in the header are always exact even when a listing is truncated.

The same reasoning runs the other way, and that case is not hypothetical:
`VendorOfferHasher.ComputeOfferId`'s own comment records that changing the hash
format gives every row in the dataset a new id at once, with no data change at
all. Such a row is counted as **rehashed** rather than listed as a repricing.
Before that distinction existed, the 2026-08-25 refresh's summary reported
48,750 of 53,544 rows as repriced - each line printing an identical before and
after - and reported `retagged: 0` for a run that in fact took seasonal tags
from 57 to 597, because the retags were buried in that false repriced bucket.
The same pass also matches rows within one (merchant, item) group by content
first, so a merchant selling the same item at several output counts (the live
`"Cannibal" | item 67389` rows are x1/x3/x8) can no longer have its rows
cross-paired into price moves that never happened. Re-run against the same two
datasets, the report is `repriced: 371, retagged: 492, rehashed: 48379`.

## Data Files

| File | Size | Role |
|------|------|------|
| `ref/vendor_offers.json` | ~14.8 MB | **Baseline vendor offers** - loaded by the Blish HUD module at runtime. Contains deduplicated, ID-resolved vendor offers. Committed to repo and embedded in the `.bhm` package. Marked `-diff -merge linguist-generated` in `.gitattributes`. |
| `ref/vendor_offers_manifest.json` | ~130 B | **Provenance record** for the file above - schema version, source, offer count, and the run's `generatedAt`. Everything run-scoped lives here so the payload stays byte-stable across a no-op refresh. Committed to repo. |
| `ref/wiki_vendor_cache.json` | ~19.6 MB | **Wiki query cache** - raw SMW results from Pass 1. Used by Pass 2 for currency resolution. Supports incremental merging across multiple scrape runs. Gitignored (dev-local) since PR #92, and excluded from the packed `.bhm` since M38/WP-29 - see `docs/RELEASING.md`. |
| `ref/item_id_cache.json` | ~40 KB | **Cost name cache** - settles each wiki cost name as an item's GW2 game ID, as the name of the wallet currency it turns out to be, or as a dated miss the wiki answered no id for. Avoids re-resolving settled names on subsequent runs. See "Resolving a cost name" and "Remembered misses" below. Gitignored (dev-local), same as the wiki cache above. |
| `ref/vendor_offers_unresolved.json` | small | **Unresolved sections** from the last run - the queries the wiki never answered, for a follow-up run to re-target. Written only when a run leaves something unresolved, and deleted by the next clean run. Gitignored (dev-local, run state rather than data). |
| `ref/seasonal_wikitext_cache.json` | small | **Seasonal festival tag cache** - maps vendor page name to its raw wiki `{{Temporary\|...}}` seasonal/event value (or `""` for "checked, not tagged"). Only populated by `--tag-seasonal-festivals`. Gitignored (dev-local, like `wiki_vendor_cache.json`/`item_id_cache.json`). |

## What It Queries

1. **GW2 API** `/v2/currencies` - loads all currency IDs and names so wiki currency strings (e.g. "Coin", "Volatile Magic") can be mapped to numeric IDs.
2. **GW2 Wiki API** `action=query&redirects=1` - asks which page a cost name actually names, following redirects and title normalization, 50 names to a request. See "Resolving a cost name" below.
3. **GW2 Wiki SMW API** `action=ask` - queries vendor subobject pages (`[[Sells item::+]]`) and pulls:
   - `Sells item.Has game id` - item's GW2 game ID
   - `Sells item` - item page name
   - `Has item quantity` - output count (defaults to 1)
   - `Has item cost` - record type with `Has item value` (amount) and `Has item currency` (name)
   - `Has vendor` - NPC vendor page
   - `Located in` - location pages
   - `Has daily purchase cap` - daily purchase limit (absent = uncapped)
   - `Has weekly purchase cap` - weekly purchase limit (absent = uncapped)
   - `Has seasonal purchase cap` - Wizard's Vault seasonal purchase limit (absent = uncapped or not a Vault offer)

## Rate Limiting

- Configurable delay between wiki requests (default 250 ms, minimum 200 ms).
- Every `action=ask` request sends `maxlag=5`, which asks the wiki to refuse
  the query while its database replicas are lagging rather than serve it and
  fall further behind. A refused query is retried like any other failure.
- The `User-Agent` names the repository, per MediaWiki's User-Agent policy, so
  an operator can open an issue instead of blocking the address.
- **HTTP 403** (wiki rate-limit block): 30-second base cooldown with exponential backoff and jitter.
- **HTTP 429 / 5xx**: exponential backoff (1 s / 2 s / 4 s / 8 s). Respects `Retry-After` header.
- **HTTP 200 with an `error` body**: the same backoff as 429. See below.
- Every one of those gets `--max-attempts` tries (default 5) before the section
  is recorded unresolved.
- Both query and currency resolution methods return partial results on failure rather than losing work.

## When the wiki refuses a query

`action=ask` answers in three shapes, and all three arrive as HTTP 200:

| Body | Meaning |
|---|---|
| `{"query":{"results":{...}}}` | rows exist |
| `{"query":{"results":[]}}` | genuinely no rows |
| `{"error":{"code":...,"info":...}}` | the API refused the query |

The tool reads all three through one reader (`WikiAskResponse`), so a refusal
can never be mistaken for an empty result set. A refusal is expected rather
than exceptional, and the goal is the most complete scrape possible, so it is
retried on the same backoff ladder as an HTTP 429. Every refusal is logged in
full: the error code, the info text, the query condition and the attempt.

A section that is still refused after its last attempt is recorded as
**unresolved** and the run continues. Unresolved sections are listed at the end
of the run and written to a sidecar file beside the dataset:

```jsonc
{
  "generatedAt": "2026-09-05T10:02:41.7712030Z",
  "sections": [
    {
      "kind": "partition",
      "label": "As",
      "prefix": "As",
      "condition": "[[Sells item::+]][[Has vendor::~As*]]",
      "errorCode": "maxlag",
      "reason": "Waiting for 10.64.16.79: 6.9 seconds lagged.",
      "attempts": 5
    }
  ]
}
```

`condition` is the query that failed, so a follow-up run can re-target exactly
those sections rather than scraping the whole namespace again:

```bash
dotnet run --project tools/VendorOfferUpdater/VendorOfferUpdater.csproj -- \
  --query "[[Sells item::+]][[Has vendor::~AS*]]" \
  --merge-into ref/vendor_offers.json \
  ref/vendor_offers.json
```

The sidecar is deleted by the next run that resolves everything, so its
presence always describes the latest run.

One refused section is worth carrying on past. Three in a row is the wiki
declining to answer this address at all, and the run stops there rather than
spending a full attempt ladder per section to be told the same thing 36 times.
Whatever was collected up to that point is kept and the wiki cache is saved.

## Splitting an oversized query

The SMW API pages through at most ~5,500 results for one query condition. Past
that, the scrape splits the query by vendor-name prefix: `[[Has vendor::~A*]]`,
then `[[Has vendor::~Ab*]]`, and so on, up to `--max-depth`.

Which characters the split uses is not a free choice. SMW compiles `~As*` to
`smw_sortkey LIKE 'As%'` against a `VARBINARY(255)` column, so the comparison
is byte-wise and **case-sensitive**, and the sortkey is the page title with
underscores turned back into spaces. Vendor names are Title Case, so the first
character is upper case but the ones after it usually are not: `~AS*` matches
nothing at all, while the sixteen `Astral Ward *` merchants sit under `As`. The
character set therefore spans upper case, lower case, digits and the
punctuation that appears in real names, including a space, an apostrophe, a
slash, a parenthesis and a leading double quote. `docs/ARCHITECTURE.md` section
T.9 records where each of those properties is readable in SMW's own source.

Each child costs one request whether or not it holds rows, so the size of that
set is the price of an overflow: 73 requests per partition that overflows. A
level is only reached where the level above it overflowed.

**The arithmetic is checked.** A partition that overflowed returned more rows
than one query can page through, so at least one of its children must hold
rows. If every child answers with none, that is a contradiction: the split is
not reaching the names rather than the names not existing. The partition is
recorded UNRESOLVED and the coverage check blocks the write. A partition that
overflows at `--max-depth`, whose remaining rows this run will never ask for,
is recorded the same way; raise `--max-depth` (or `MAX_DEPTH` in the wrapper
script) to split it further.

## Resolving a cost name

A vendor's price is one of two things: an item, or a wallet currency. The wiki
gives it as display text in the SMW property `Has item currency`, typed `_txt`,
and that text and the canonical page title disagree far more often than they
agree. Matching the text exactly against the GW2 API's currency names and then
against wiki page titles left 40 names unresolved across 1,639 cost lines, and
every one of those is a price the module cannot read, so the whole offer is
dropped.

Two questions are now asked in order.

**Which page does this name point at?** `action=query` with `redirects=1`
answers it, 50 names to a request. The response carries two separate arrays and
a name can appear in one, in the other, or in the first and then the second:

| Array | What it reports | Example |
|---|---|---|
| `normalized` | title normalization, such as collapsing a doubled space | `Ancient  Coin` to `Ancient Coin` |
| `redirects` | the page a redirect points at | `Ectoplasm` to `Glob of Ectoplasm` |

Both are followed hop by hop, so a name that is normalized and then redirected
lands on its real page. This reaches what no string rule can: `Convergences:
Mount Balrior Wayfinder's Choice Chest` is a redirect to `Convergence: Mount
Balrior Commander's Choice Chest`, a rewording rather than an inflection. The
wiki has to answer, which is the same conclusion `FetchWikitextAsync` reached
on the other endpoint - see `docs/ARCHITECTURE.md` section T.6.

**Is that page an item or a currency?** The page title is matched against the
GW2 API's currency names first. A title that matches nothing there is asked for
`Has game id`, and a page that answers with one is an item. A page that answers
with no id is neither an item nor an exactly-named currency, and only then is
the title matched against the currency list a second time with every word's
trailing `s` dropped on both sides. That last step is what closes the wiki's
singular page title against the API's plural currency name - `Tale of Dungeon
Delving` against `Tales of Dungeon Delving` - and it is deliberately last,
because a name that is really an item never reaches it. A stem two currencies
share is not matched at all.

A currency the API does not name does not resolve, and must not. `Glory` and
`Influence` still have wiki pages and neither is in the wallet; an offer priced
in a currency no account can hold is not a route. The same rule drops a cached
currency name the API has stopped listing, so a currency retired mid-life is
asked about again rather than priced for ever.

### What it costs

The name pass is batched to MediaWiki's documented limit of 50 titles per
request, against the item pass's 10 per SMW `ask`. For `N` names the wiki has
never settled, the worst case is `ceil(N / 50)` requests on top of the
`ceil(N / 10)` the item pass already made, each with the same `--max-attempts`
ladder. A measured cold cache carried 1,106 such names, so 23 requests on top
of 111. A warm cache with 40 unsettled names pays one.

A request that fails is not an answer. A name whose page could not be looked up
is asked about no further and cached neither way, exactly as an unanswered item
batch is - see "Remembered misses" below.

## Remembered misses

A cost name the wiki answers about with no item id and no currency is cached as
a miss, so that every future run does not ask about it again. What is left
after the two passes above is a genuine absence: a name the wiki has no page
for, or a page that is neither an item nor a currency an account can hold.

A cached miss is permanent - the resolution pass only asks about names the
cache has never settled - so it must only ever be recorded for a name the wiki
actually answered about. `ResolveItemGameIdsAsync` reports which names were in
a batch the wiki answered, alongside the ids it resolved, and a name in a batch
that was refused, failed, or was never sent is cached neither way. It is
reported at the end of the pass and asked about again next run.

Every miss records the date it was written:

```jsonc
{
  "cacheVersion": 3,
  "ids": { "Mystic Coin": 19976 },
  "currencies": { "Tale of Dungeon Delving": "Tales of Dungeon Delving" },
  "misses": { "Glory": "2026-09-05T21:02:41.7712030Z" }
}
```

A name is settled in exactly one of the three sections, and `Contains` covers
all three, so a name in any of them is not asked about again. The `currencies`
section stores the API's name rather than its id: the API is loaded fresh every
run and is the authority on which currencies are live, so an entry it no longer
names is dropped instead of going on pricing offers. A cache written before
that section existed loads unchanged, with its names re-resolved once.

A run prints how many misses it is carrying and how old the oldest is.
`--recheck-misses` drops them all so they are asked about again, which is how
an operator retries a stale one without hand-editing the file. Resolved ids are
untouched by it.

A cache written in the older flat format (`{"name": 12345, "other": -1}`) is
migrated on load rather than discarded: `-1` becomes a miss with no date,
reported as undated, since that format recorded none. A missing cache file is
an ordinary cold start, not an error.

## Coverage check

Before the dataset is written, the run compares it against the file it would
replace, on total offers and on distinct merchants. The write is blocked when
the run left any section unresolved, or when either count fell by more than 2%.
The merge step's data-loss guard does not cover this: it protects rows already
in the baseline, and says nothing about rows a run never fetched at all.

Pass `--allow-coverage-drop` to write anyway. The reasons are printed either
way; the flag decides whether they stop the write.

## Output Schema

```jsonc
{
  "schemaVersion": 1,
  "source": "gw2wiki-smw",
  "offers": [
    {
      "offerId": "a1b2c3...",       // SHA-256 hash (deterministic dedup key)
      "outputItemId": 12345,        // GW2 item ID
      "outputCount": 1,             // quantity produced
      "costLines": [                // one or more costs
        { "type": "Currency", "id": 1, "count": 100 }
      ],
      "merchantName": "Miyani"
      // "locations", "dailyCap", "weeklyCap", "seasonalCap" omitted when null
    }
  ]
}
```

Offers are deduplicated by `offerId` and sorted alphabetically. Null fields are omitted from the output.

The payload carries **nothing that varies per run**. The run's timestamp and a
provenance summary go in a sibling `ref/vendor_offers_manifest.json` instead:

```jsonc
{
  "manifestVersion": 1,
  "schemaVersion": 1,
  "source": "gw2wiki-smw",
  "offerCount": 59414,
  "generatedAt": "2026-08-25T14:09:11.2810521Z"
}
```

This is what makes a no-op refresh visible: re-scrape unchanged wiki data and
`ref/vendor_offers.json` is byte-for-byte unchanged, so `git status` shows only
the manifest. An embedded `generatedAt` used to guarantee a fresh 14.8MB blob on
every run whether or not a single price had moved.

## Exit Codes

| Code | Meaning | Action |
|------|---------|--------|
| 0 | Success - offers written | Commit updated `ref/vendor_offers.json` |
| 1 | Error (network failure, unexpected exception) | Check error message; retry if transient |
| 2 | Safety limit exceeded (max requests or max runtime) | Partial results saved to wiki cache. Increase `--max-runtime` or `--max-requests` and re-run |
| 3 | Coverage check blocked the write | Nothing was written. Re-run the sections named in `ref/vendor_offers_unresolved.json`, or pass `--allow-coverage-drop` if the loss is intended |
| 130 | Cancelled (Ctrl+C) | Partial results may be saved to wiki cache |

## When to Re-run

- **Game patches** that add new vendors, items, or currencies
- **Wiki updates** when the community documents new or corrected vendor data
- **Periodically** (e.g. quarterly) to pick up gradual wiki improvements
- After modifying the VendorOfferUpdater tool itself, to verify output correctness
- **Each Wizard's Vault season** - the live "Wizard's Vault" page's current-season
  stock/prices/caps can change at a season boundary. See "Wizard's Vault Rotation"
  below for the scoped refresh command.
- **After adding new printouts/fields to `WikiSmwClient`** - Re-running Pass 2 alone
  (`--resolve-item-currencies-only`) against `ref/wiki_vendor_cache.json` reuses the
  old cached `WikiVendorResult` shape and will silently omit the new fields forever;
  a full Pass 1 re-scrape is required to fetch them. Pass 1's cache merge overwrites
  any existing entry for a re-queried `PageName` with the freshly-fetched result (see
  `Program.cs`), so a normal full re-scrape backfills new fields for every page without
  needing to delete `ref/wiki_vendor_cache.json` first. Deleting the cache first is only
  needed if a page must be dropped from the cache entirely (e.g. it no longer resolves
  on the wiki) rather than refreshed.

## Wizard's Vault Rotation

The Wizard's Vault (Astral Acclaim) reward store spans three distinct wiki
pages, all captured by our scrape under three distinct `merchantName` values.
The naming itself already tells you which "kind" of page an offer came from -
no separate field is needed to distinguish them:

| `merchantName` | What it is | Rotation behavior |
|---|---|---|
| `Wizard's Vault` | The **current season's** live store. | Overwritten each season - prices/caps/stock for THIS page can change at a season boundary. This is the page that carries `seasonalCap` for capped rows (e.g. Mystic Coin 60/season, Mystic Clover 20/season). |
| `Wizard's Vault/Historical Astral Rewards` | Wiki-maintained archive of **past seasons'** rotated rewards (mostly one-off cosmetics/unlocks that have since left the live page). | Grows over time; a row appearing here does not mean it's currently purchasable. |
| `Wizard's Vault/Legacy Rewards` | Cosmetics that rotated out of the live store into a permanent "Legacy" tab. | Grows over time; separate from the seasonal-cap system entirely (no `seasonalCap` values observed on this page). |

**Confirmed live (2026-07-22):** `Has seasonal purchase cap` is used
*exclusively* by pages under this `merchantName` prefix wiki-wide - but
only TWO of the three pages actually carry it: a `[[Has seasonal
purchase cap::+]]` probe with no vendor filter returned 29 rows total,
split between `Wizard's Vault` and `.../Historical Astral Rewards` only
(zero on `.../Legacy Rewards`, consistent with that page's "separate
from the seasonal-cap system entirely" note in the table above). No
other vendor on the wiki uses this property, so those 29 rows are the
complete set that exists to seed.

**Known wiki-documentation quirk (Bag of Coins tiering):** the two-tier
"Bag of Coins (1 Gold)" item is represented as two *separate* wiki item
pages/game-IDs, not one row with two prices. As of this writing, only the
discount tier ("Bag of Coins (1 Gold) (limited)", 8 AA, seasonal cap 100)
appears as a `{{vendor table row}}` on the live `Wizard's Vault` page; the
continuation tier ("Bag of Coins (1 Gold) (unlimited)", 35 AA, uncapped) is
currently only machine-readable via the `Wizard's Vault/Historical Astral
Rewards` page's subobjects, even though it is still a live, purchasable deal
in-game. Both rows are seeded (from their respective pages) so this doesn't
lose data, but a from-scratch reader expecting the "unlimited" tier to be
tagged with the live merchant name will not find it there - this is a wiki
editorial gap, not a scraper bug. See `docs/research/aa-tier-findings.md` for
the full investigation.

**To refresh Wizard's Vault data for a new season** (scoped, keeps the diff
reviewable - mirrors the `--query`/`--merge-into` pattern from KNOWN-ISSUES.md
item 24's Homestead Refinement seeding):

```bash
dotnet run --project tools/VendorOfferUpdater/VendorOfferUpdater.csproj -- \
  --query "[[Has vendor::~Wizard's Vault*]]" \
  --merge-into ref/vendor_offers.json \
  ref/vendor_offers.json
```

`~Wizard's Vault*` is a prefix wildcard match on `Has vendor` that covers all
three page names above and nothing else (verified: `Wizard's
Gobbler`/`Portable Wizard's Tower Exchange`, which share the "Wizard's" word
but not the "Wizard's Vault" prefix, are not matched), and leaves every
other merchant's offers byte-for-byte unchanged - but matching the wildcard
pattern is not the same as coming back with rows to merge. The 2026-07-22
seeding pass's run of this exact command only actually returned/replaced
offers for `Wizard's Vault` and `.../Historical Astral Rewards`;
`.../Legacy Rewards` came back with no rows that run and its existing
offers passed through untouched (see KNOWN-ISSUES.md item 33). Do not
assume this command refreshes all three merchants every time it is run -
check which merchant names actually changed in the resulting diff.

**Stale-offer sweep status:** no automated stale-offer detector exists for
Wizard's Vault specifically (the general sweep is manual - see
KNOWN-ISSUES.md item 28). If/when that tooling is built, the `merchantName`
convention above ("Wizard's Vault" = current, `.../Historical Astral Rewards`
= archived, `.../Legacy Rewards` = rotated-out cosmetics) is sufficient to
tell current-season offers apart from historical ones without any new code -
a sweep should only ever treat `Wizard's Vault` (current) rows as
"unexpectedly missing => investigate," never the historical/legacy pages,
which are expected to retain old rows indefinitely.
