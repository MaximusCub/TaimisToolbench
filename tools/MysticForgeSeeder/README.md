# MysticForgeSeeder

Offline tool that scrapes Mystic Forge recipes from the
[GW2 Wiki](https://wiki.guildwars2.com/) API and resolves the item names it
finds to GW2 item IDs, producing `ref/mystic_forge_recipes.json` - the
Mystic Forge recipe data the module (and `tools/TaimisToolbench.RecipeSeeder`,
which merges it into the main recipe search index) reads at build/seed time.
The official GW2 API does not expose Mystic Forge recipes, which is why this
tool goes to the wiki instead.

This is a standalone .NET 8 console project with no dependency on the main
`TaimisToolbench.csproj` - it does not need the module's `packages/`
restored to build.

## Quick Start

```
dotnet run --project tools/MysticForgeSeeder/MysticForgeSeeder.csproj
```

Run from anywhere inside the repo - it walks up from the current directory
looking for a `.git` folder to find the repo root, then reads/writes under
that root's `ref/` directory.

## CLI Reference

| Flag | Default | Description |
|------|---------|-------------|
| `--dry-run` | off | Query the wiki but skip writing output (used to preview counts) |
| `--force-resolve` | off | Re-resolve every item name to an ID instead of skipping names already in the cache |
| `--delay <ms>` | 250 | Delay between wiki API requests |
| `--max-requests <n>` | 200 | Safety limit on total HTTP requests to the wiki |

## What It Does

1. Queries the wiki for all Mystic Forge recipes (`WikiRecipeClient.QueryMysticForgeRecipesAsync`).
2. Collects every unique output/ingredient name across those recipes
   (case-insensitive dedup).
3. Resolves each new name to a GW2 item ID via the wiki, skipping names
   already present in `ref/mf_item_id_cache.json` unless `--force-resolve`
   is set. Unresolved names are cached as a miss sentinel and printed (up
   to 50) so they can be investigated.
4. Builds recipe objects. Each output and ingredient takes the ID its name
   resolved to; where the name resolved to nothing it falls back to the ID
   the wiki's own recipe fields assert (`Has output game id` /
   `Has ingredient with id`). A recipe is skipped whole if any ingredient
   count is `<= 0` or any output/ingredient still has no ID - never emitted
   with the unresolved ingredient dropped, which would price it below what
   it really costs. Skips are printed with the reason.
5. Drops recipes whose content duplicates one already kept (two wiki pages
   can document the same forge recipe), then restores any hand-authored
   `expectedOutputCount` override from the file it is about to overwrite.
6. Writes `ref/mystic_forge_recipes.json`, numbering from -100000 downwards.

## How It Treats a Wiki Refusal

Every query carries `maxlag=5`, so the wiki declines the request while its
databases are lagging instead of serving it slowly. That refusal arrives as
HTTP 200 with an `error` object in the body rather than as a status code;
`tools/MysticForgeSeeder/WikiApiRefusal.cs` reads it. The tool waits and
sends the request again, up to four attempts. The wait is whatever
`Retry-After` asks for, in either its seconds or its HTTP-date form, and at
least five seconds when the response asks for nothing.

Three things end the run with an error rather than quietly: a refusal that
survives all four attempts, an error code that means the query itself is
wrong, and a page carrying neither results nor an error. None of them may
end the scrape early, because step 6 rewrites
`ref/mystic_forge_recipes.json` whole - a short scrape would drop recipes
from the shipped seed and report success.

## Data Files

| File | Role |
|------|------|
| `ref/mystic_forge_recipes.json` | Resolved Mystic Forge recipes. Committed and consumed by the module and by `tools/TaimisToolbench.RecipeSeeder`. |
| `ref/mf_item_id_cache.json` | Name -> item ID cache (including unresolved-name sentinels). Gitignored - dev-only, avoids re-resolving known names on every run. |

## When to Re-run

- After a game update adds, removes, or changes Mystic Forge recipes.
- After the wiki's Mystic Forge recipe pages are corrected/updated.
- Followed by re-running `tools/TaimisToolbench.RecipeSeeder`, since that
  tool merges `ref/mystic_forge_recipes.json` into the search index it
  produces - a stale seed here means stale Mystic Forge search results in
  the module.

## Two things a rerun must not lose

The tool rewrites `ref/mystic_forge_recipes.json` whole and renumbers every
row, so anything it cannot reproduce is lost. Two kinds of content are at
risk, and each is handled by construction rather than by remembering.

**Recipes on multi-variant equipment pages.** Every WvW and PvP legendary
armour piece is forged from its own ascended version plus three gifts, and
none of the 90 reached the module before 2026-08. The SMW query returns them
all; they died in step 4. A page like `Ardent Glorious Armguards` covers an
ascended and a legendary item, so it carries no page-level `Has game id` at
all - every ID it has lives on an `equipment variant table row` subobject -
and its recipe's `Has canonical name` is `Ardent Glorious Armguards
(legendary)`, which is no wiki page. The output name resolved to nothing and
the whole recipe was skipped. (The anchored ingredient name in the same
recipe, `Ardent Glorious Armguards#item1`, was never the problem: an anchor
is an SMW subobject ID and `[[Page#item1]]` resolves through the ordinary
name query.) Step 4's fallback to `Has output game id` - the recipe
template's own explicit `output item id` parameter - resolves them.

Name resolution still wins where it succeeds, because `Has output game id`
is only stated explicitly when the template carries that parameter; without
it the wiki derives the value by name lookup and picks one arbitrary member
of a same-name pair. GW2 ships several (`Recipe: Satchel of Mighty
Embroidered Armor` is both 9960 and 9962), and the page's own declared ID is
the better answer there.

**Hand-authored `expectedOutputCount`.** Nothing on the wiki expresses
expected output. Recipe -1591's 0.31 for the 1-clover Mystic Forge gamble
came from a community study and was written into the file by hand. Step 5
carries every such override forward, matched on recipe content rather than
on ID (IDs renumber), and prints a loud warning for any override whose
recipe the new run no longer produces.

## Recipe ID space

Generated IDs start at -100000 and descend. `ref/recipes_seed.json` also
carries negative-ID rows that no generator rebuilds - currently four
synthetic Merchant/achievement rows at -1592..-1595 - and
`tools/TaimisToolbench.RecipeSeeder` merges the two into one dictionary
keyed by recipe ID, taking the forge block first, so an overlap replaces a
hand-authored row rather than colliding with it. The two producers own
disjoint halves: hand-authored rows take [-99999, -1], the generated block
takes -100000 and below. Growth moves the generated block away from the
hand-authored half rather than into it.
`MysticForgeSeedIdSpaceTests` fails the build if the shipped data ever
breaches the partition, and `MergeMysticForgeRecipes` refuses a forge row
that lands in the hand-authored half.
