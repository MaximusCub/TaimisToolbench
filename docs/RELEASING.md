# Releasing

This document describes the current, actual state of packaging and release
for Taimi's Toolbench - not an aspirational process.

**What the project actually practices today** (the v0.2.x in-game testing era):
a release is a CHANGELOG entry, a `manifest.json` version bump, and a
matching `v<version>` git tag on the release commit, deployed by copying
the built `.bhm` into a live Blish HUD install. `CHANGELOG.md` states the
convention in its own header, and `v0.2.0` through `v0.2.4` exist and are
pushed to origin (measured 2026-08-25: `git ls-remote --tags origin`).

**What changed since:** `.github/workflows/release.yml` now builds
Release/x64 on any pushed `v*` tag and publishes
`bin/x64/Release/TaimisToolbench.bhm` as a GitHub Release asset, with the
matching `CHANGELOG.md` section as the body. It refuses to publish if the
tag does not match `manifest.json`'s version, or if `CHANGELOG.md` has no
section for it. Pushing a tag is therefore the whole release action.

**What still does not exist:** a Blish HUD module-repository listing, so
the download is still manual. And `gh release list` returned nothing as of
2026-08-26 - the workflow has not yet been exercised by a pushed tag, so
the first release will be the one that proves it end to end. See the
v0.3.0 runbook below for what has been verified without a tag and what
only a real tag can prove.

Everything below reflects what a contributor can do today with the tools
already in the repo, and is measured against the current build unless
labelled otherwise.

## The release protocol, step by step

1. Land the work on `master`.
2. Re-run the recipe seeder from the repo root and commit the refreshed
   `ref/` seeds:

   ```
   dotnet run --project tools/TaimisToolbench.RecipeSeeder/TaimisToolbench.RecipeSeeder.csproj -- --output-dir ref --force
   ```

   Roughly 1-2 minutes against the live API (measured 2026-08-25: 1m53s
   cold, 59s on an immediate re-run). `--output-dir ref` is not optional:
   the tool's own default writes into its `bin/` folder, not the repo's.
   Why it is a release step: the seed pins the GW2 build id it was built
   against, and once that id no longer matches the live build every
   negative row in the seed stops counting as a cache hit, putting every
   user on the slow live-API path for their first plan of each session.
3. **Consider refreshing the vendor offers, and review what comes back.**
   One command, from Git Bash at the repo root:

   ```
   ./tools/refresh-vendor-data.sh
   ```

   It builds the tool, runs both wiki passes (~15 min, rate-limited),
   snapshots the baseline first, and ends by printing the `--diff-summary`
   that the section below requires in the PR body.
   `ref/wiki_vendor_cache.json` and `ref/item_id_cache.json` are gitignored
   dev-local inputs - absent, the run just re-scrapes from scratch.

   Unlike step 2 this is a judgement call rather than an unconditional
   step, and the reason is not laziness: the GW2 Wiki is a volunteer
   service and a full refresh is ~2,000 requests against it, while the
   dataset only actually moves when a patch adds vendors or an editor
   corrects a page. Refresh on an expansion or a Wizard's Vault season
   boundary, not on every point release. Skipping it ships stale data;
   running it unreviewed can ship *wrong* data, which is worse -
   `ref/vendor_offer_exclusions.json` exists because a decade-dead vendor
   row nearly shipped. Read the `added`/`removed`/`repriced` rows before
   committing. If nothing moved, the report says so and only
   `ref/vendor_offers_manifest.json` changes.
4. Bump `manifest.json`'s `version` (the About tab reads it live). Check
   that `manifest.json`'s `description` still matches the GitHub repo
   description - it is the sidebar text, the search-result snippet, and the
   Open Graph card used every time the link is pasted into Discord or
   Reddit, and it is the only sentence most people will ever read.
5. Add the matching `CHANGELOG.md` entry - `## <version> - <date>`, in the
   user-facing voice the existing entries use, not commit-message voice.
   The release workflow uses this section verbatim as the release body and
   fails the build if it is missing.
6. **Sweep the prose that names a version.** `manifest.json` is not the
   only place a version number is written down, and the others drift
   silently because nothing reads them: this file (the v0.2.x paragraph at
   the top, the `manifest.json` fields section, the "what a real release
   process would still need" list) and `docs/ROADMAP.md`'s current-phase
   bullet all state which release is newest. `grep -rn "0\.2\." docs/
   *.md` finds them; update each to the version being shipped, and stamp
   any claim you re-checked with `(measured YYYY-MM-DD)` rather than "at
   the time of writing". This step exists because ROADMAP.md and
   RELEASING.md both still said v0.2.3 was newest after v0.2.4 shipped.
7. **If the plan view changed, refresh `docs/images/`.** The README's
   screenshots are the only proof a visitor has that the product works.
   They went stale once already: the shots taken 2026-07-23 showed columns
   packed hard left with a wide empty band, which is the exact layout the
   0.2.3 entry in `CHANGELOG.md` describes as removed. Retake against the
   current build at full window width, cropped to whole rows.
8. Clear `bin/` and `obj/`, then build Release/x64 (see the clean-build
   rule in the addendum - it is not optional).
9. Tag the release commit `v<version>` and push the tag. That triggers
   `.github/workflows/release.yml`, which rebuilds Release/x64 on CI and
   publishes the `.bhm` to GitHub Releases. Check the run succeeded and the
   asset is attached.
10. Copy `bin/x64/Release/TaimisToolbench.bhm` into the live Blish HUD
    install's `modules` directory and reload Blish HUD.

Because every deployed build has a tag, any two shipped builds can be
compared with `git diff v0.2.0..v0.2.1`.

## v0.3.0 first-release runbook (staged 2026-08-26)

The release-prep pass staged protocol steps 4-6 on this branch:
`manifest.json` says `0.3.0`, `CHANGELOG.md` carries the `## 0.3.0`
section the workflow publishes verbatim, and the version prose is swept.
What remains after the in-game testing pass:

```
# 1. Refresh the recipe seeds (protocol step 2); commit if anything moved.
dotnet run --project tools/TaimisToolbench.RecipeSeeder/TaimisToolbench.RecipeSeeder.csproj -- --output-dir ref --force

# 2. If release day differs from the staged CHANGELOG date, update the
#    "## 0.3.0 - <date>" line and commit.

# 3. Tag the release commit and push the tag. This is the whole release.
git tag v0.3.0
git push origin v0.3.0

# 4. Watch the run and confirm the asset attached.
gh run watch
gh release view v0.3.0
```

Protocol step 7 also applies to this release: the plan view's tooltips
changed and two new tabs shipped since the README screenshots were taken.

### Verified without a tag (2026-08-26)

- The release job's build steps were replicated locally in a clean
  worktree of `master`: `dotnet build TaimisToolbench.csproj
  -p:Platform=x64 -c Release` produced
  `bin/x64/Release/TaimisToolbench.bhm` - exactly the path the
  workflow asserts and uploads - and the file is a valid zip (38
  entries) containing `manifest.json` with the matching version and
  none of the four excluded `ref/` cache files (measured by listing the
  zip's entries).
- The workflow's CHANGELOG-extraction awk was run byte-for-byte locally
  against `CHANGELOG.md`: it returns the correct section for `0.2.0`,
  `0.2.4` and the new `0.3.0` heading, and returns nothing (a hard
  failure in the workflow) for a version with no section.
- The tag-vs-manifest gate is plain `bash` + `jq`; both are preinstalled
  on `windows-latest` runners (inferred from the runner image manifest,
  not proven by a run).
- `tests.yml` declares `workflow_call`, so `release.yml`'s `uses:` test
  gate resolves, and the same suite is green on `master` (run
  33001423467, measured 2026-08-26). Its `changes` job special-cases tag
  refs to always run the full suite.
- `tests.yml` already builds Release/x64 and asserts the `.bhm` path on
  every master push, so the artifact location is CI-proven - just not by
  the release workflow itself.
- `gh release list` still returns nothing (measured 2026-08-26): the
  workflow has never run.

### What only a real tag can prove

- The `on: push: tags: v*` trigger firing and the job checking out the
  tag ref.
- The reusable-workflow test gate executing in a tag context end to end.
- `softprops/action-gh-release` creating the release and uploading the
  asset under the workflow-level `contents: write` permission.

### PROPOSAL, not executed: rehearse the workflow with v0.3.0-rc.1

The release workflow has never run, and the three items above are only
provable by a pushed tag. The first pushed tag can be a rehearsal
instead of the release:

1. On a throwaway commit (not necessarily on `master`): set
   `manifest.json` to `0.3.0-rc.1` and add a one-line
   `## 0.3.0-rc.1 - <date>` section to `CHANGELOG.md` (both gates
   require the exact match).
2. `git tag v0.3.0-rc.1 <that commit> && git push origin v0.3.0-rc.1`.
3. The workflow now marks any hyphenated tag as a GitHub prerelease
   (added 2026-08-26), so the releases page keeps `v0.3.0` as its only
   full release and "latest" never points at the rehearsal.
4. After the run proves the pipeline, optionally remove the rehearsal:
   `gh release delete v0.3.0-rc.1 --yes` and
   `git push origin :refs/tags/v0.3.0-rc.1`.

Caveats: Blish HUD parses the manifest version as SemVer and a
prerelease suffix is valid SemVer, but a `0.3.0-rc.1` manifest has not
been proven in a live Blish install (inferred); and the rehearsal tag
is public while it exists. The alternative is to accept the real
`v0.3.0` tag as the first proof, with every locally provable step
already verified above and the known failure gates (tag/manifest
mismatch, missing CHANGELOG section, missing `.bhm`) all failing closed
before the publish step.

## Required: an offer diff on every `data(vendor):` pull request

`ref/vendor_offers.json` is 14.8MB on a single line, so `git diff` on a
vendor refresh reports `1 insertion(+), 1 deletion(-)` - the whole file
that prices every vendor in the game, replaced as one indivisible hunk.
A reviewer facing that has two options: rubber-stamp it, or hand-write a
JSON differ. This repo already has the scar tissue from the first option
(`ref/vendor_offer_exclusions.json` exists because a stale row shipped).

**A pull request containing a `data(vendor):` commit must carry the
`--diff-summary` output in its body.** `tools/refresh-vendor-data.sh`
snapshots the baseline before overwriting it and prints the summary at the
end of the run, so it is already on screen when the PR is written. To
produce it by hand from any two dataset copies:

```
dotnet run --project tools/VendorOfferUpdater/VendorOfferUpdater.csproj -- \
    --diff-summary <old vendor_offers.json> <new vendor_offers.json>
```

It reports offers added, removed, repriced, retagged and rehashed, keyed by
merchant and item rather than by the content hash - see
`tools/VendorOfferUpdater/README.md` for why the raw `offerId` set is not
usable for this. A refresh that changed nothing prints "No offer changed",
and `ref/vendor_offers.json` will be byte-for-byte unmodified: only
`ref/vendor_offers_manifest.json` moves. That is the intended no-op signal.

### Why this is not a scheduled workflow

The refresh is one command and needs no secrets, so a nightly job that ran
it and opened a PR would be easy to write. It is still the wrong shape,
for a reason about review rather than automation:

- **A wrong row is worse than a stale one.** Stale vendor data prices an
  item the way it was priced last quarter. A wrong row prices a legendary
  component from a vendor that has not existed since 2016, and the plan
  looks equally authoritative either way. The 2026-08-25 refresh scraped
  exactly such a row off a live wiki page; what stopped it was a human
  reading `AcquisitionHintSeedVendorAgreementTests`' trip-wire.
- **The review is the expensive half, and it cannot be scheduled.** The
  diff summary makes a refresh reviewable, but somebody still has to read
  the changed rows and decide whether the wiki is right. A bot opening a
  PR every week trains the reviewer to skim, which converts the one
  safeguard that has actually caught a bad row into a rubber stamp.
- **The dependency is a volunteer service.** ~2,000 requests per full run
  against wiki.guildwars2.com is fine occasionally and rude on a cron.

The recommendation is therefore a **scheduled staleness check, not a
scheduled refresh**: if anything is automated later, make it a job that
runs `--diff-summary` between the shipped baseline and a scoped re-scrape
of a few high-value merchants, and opens an *issue* saying the wiki has
moved and a refresh is worth considering. That puts a human at the point
where judgement is actually required and keeps the wiki traffic
proportionate to the value.

## How a `.bhm` is actually produced

A `.bhm` file comes from the `BlishHUD` NuGet package's own build logic,
imported via:

```
<Import Project="packages\BlishHUD.1.3.0\build\BlishHUD.targets" ... />
```

That imported `BlishHUD.targets` file (installed under
`packages/BlishHUD.1.3.0/build/BlishHUD.targets` once NuGet packages are
restored) defines an `AfterTargets="Build"` target named
`BuildBlishHUDModule` that runs automatically **on every build**. As of
M38/WP-29, `TaimisToolbench.csproj` redeclares that same-named target
after the import (the one and only hand-written pack/zip logic in this
repo's own csproj) so its own version wins, purely to add a six-file
`Exclude` - see the addendum below. Otherwise it does exactly this,
unconditionally:

1. Copies `manifest.json` into the build output directory (`$(OutDir)`).
2. Copies the **entire** `ref/` folder (recursively, everything under it)
   into `$(OutDir)ref/`.
3. Copies the whole output directory into a temp subfolder and zips it into
   `<OutDir>\<ProjectName>.bhm` (e.g.
   `bin\x64\Release\TaimisToolbench.bhm`), overwriting any previous
   `.bhm`.

This was verified directly (not inferred) by running:

```
dotnet build TaimisToolbench.csproj -p:Platform=x64 -c Release
```

which produced `bin/x64/Release/TaimisToolbench.bhm` (a zip), and then
listing its contents. There is currently **no other step required** to
produce a `.bhm` - building the module in Release is sufficient.

### Measured finding: the packed `.bhm` includes files that never ship via `<Content Include>`

The `BuildBlishHUDModule` target copies `ref/**` wholesale, independent of
which `ref/*.json` files are wired into `TaimisToolbench.csproj` via
`<Content Include>`/`CopyToOutputDirectory`. Inspecting the actual `.bhm`
produced by the build above shows it contains, under `ref/`:

- The thirteen files the module actually ships, measured from the zip's own
  entry list on a clean Debug rebuild (2026-08-25):
  `acquisition_hints_seed.json`, `corner-icon.png`,
  `daily_cooldown_items.json`, `emblem.png`, `icon.png`,
  `item_name_seed.json`, `mystic_forge_recipes.json`,
  `recipe_search_seed.json`, `recipe_seed_manifest.json`,
  `recipe_sheet_items.json`, `recipes_seed.json`, `vendor_offers.json`,
  `vendor_offers_manifest.json`.

  This list used to be written as "the files also wired into the csproj as
  `<Content Include>`", and it named nine. That framing was the bug: the
  `<Content Include>` list never determined what shipped, so the doc
  inherited its omissions - `corner-icon.png`, `daily_cooldown_items.json`,
  `recipe_sheet_items.json`, `vendor_offers_manifest.json` and
  `vendor_offer_exclusions.json` were all in the `.bhm` and absent here.
  The `<Content Include>` entries have since been deleted outright and the
  packing target is the only owner, so the way to answer "what ships?" is
  `ref/` minus that target's `Exclude`, or simply to list the zip.
- **`item_id_cache.json` and `wiki_vendor_cache.json`** - these are *not*
  wired into the csproj as `<Content Include>` items (they exist purely as
  developer-side inputs to `tools/VendorOfferUpdater`), but because the
  BlishHUD packer copies the entire `ref/` directory regardless of csproj
  content wiring, they end up inside the shipped `.bhm` anyway. Measured
  directly from the zip's entry metadata: `wiki_vendor_cache.json` is
  ~19.6 MB uncompressed (~1.1 MB as stored in the zip, since it compresses
  well) and `item_id_cache.json` is ~38 KB. So the *download size* impact
  of the uncontrolled `wiki_vendor_cache.json` inclusion is smaller than
  its raw file size suggests (~1.1 MB, not ~19.6 MB, of actual `.bhm`
  bytes) - but it is still bytes a player downloads and Blish HUD extracts
  for a file with zero runtime consumer in the shipped module. This was a
  real packaging gap at the time this finding was recorded. **Update
  (M38/WP-29): fixed.** `TaimisToolbench.csproj` now overrides the
  imported `BuildBlishHUDModule` target with an `Exclude` on its `ref/**`
  copy for `ref/wiki_vendor_cache.json` and `ref/item_id_cache.json`, so
  neither file is ever copied into the output directory or the `.bhm`,
  regardless of whether they exist in the working copy. See the addendum
  below for details. The list has grown since; the addendum below tracks
  what is on it now.

## `manifest.json` fields

- `name`, `version`, `namespace`, `package` - standard Blish HUD module
  identity fields. `version` is bumped per release under the CHANGELOG +
  tag convention above (`0.3.0` staged, measured 2026-08-26; `0.2.4` is
  the newest shipped). The release workflow enforces the tag side of the
  bump - a `v*` tag that does not match this field fails before
  publishing - but nothing forces the bump commit itself to exist.
- `dependencies.bh.blishhud` - minimum Blish HUD host version
  (`>=1.3.0`).
- `url`, `contributors` - metadata shown in Blish HUD's module browser and
  in this module's own "About" tab (`Views/AboutTabContent.cs` reads these
  live from the manifest via reflection).
- `api_permissions` - the GW2 API scopes this module requests, each with an
  `optional` flag and a human-readable `details` string.
- `directories: ["data"]` - **measured to be load-bearing, not vestigial.**
  This is unrelated to the `ref/` content directory described above. It
  declares that Blish HUD should provision a writable, per-module "data"
  directory, which the module resolves at startup via
  `DirectoriesManager.GetFullDirectoryPath("data")` (`Module.cs`,
  `Initialize()`). That path is then used to construct the module's real
  runtime storage: `ModuleLogStore`, `SnapshotStore`, `StatusStore`,
  `VendorOfferStore`, and `OverlayRecipeCacheStore` all write into it, and
  it is displayed to the user in the About tab's "Data directory" row. The
  earlier open question (from the public-repo readiness pass) of whether
  this key was a stale leftover from the original `ModuleTemplate` fork is
  resolved: it is not stale, and it should not be removed. No manifest
  change was made as a result of this verification.

## Installing a built module today

The supported path is the `.bhm` attached to a GitHub Release, dropped
into Blish HUD's `modules` folder - that is what `README.md`'s "Installing"
section documents, and the two should stay consistent. There is still no
in-app Blish HUD module-repository listing, so the download is manual.

Building it locally instead produces the identical artifact:

1. Clear `bin/` and `obj/`, then build in Release, `x64`:
   `dotnet build TaimisToolbench.csproj -p:Platform=x64 -c Release`.
2. Locate the produced `.bhm` (e.g. `bin\x64\Release\TaimisToolbench.bhm`).
3. Copy it into your Blish HUD installation's `modules` directory and
   (re)load Blish HUD.

This is also exactly what the release workflow does on CI - the tag and
CHANGELOG entry are what make a given copy of those three steps
identifiable after the fact.

## Addendum: the ref/ cache-file packaging gap (fixed, M38/WP-29)

The "Measured finding" above establishes that the `BuildBlishHUDModule`
target copies all of `ref/` into the `.bhm` regardless of `<Content
Include>` wiring, and that `ref/wiki_vendor_cache.json`/
`ref/item_id_cache.json` were untracked and gitignored (PR #92) precisely
because of this. Untracking them only closes the gap for a **clean
checkout** - a `git clone` never has them, so its `.bhm` never contains
them. It does **not** retroactively remove them from a developer's
existing working copy: anyone who ran `tools/VendorOfferUpdater` before
PR #92 (or has run it since, since the tool still writes those files
locally as its own working cache) still has both files sitting in `ref/`
on disk, gitignored or not, and a build from that same working copy still
picks them up via the wholesale `ref/` copy and ships them - gitignore
only controls what `git` tracks, not what MSBuild's `CopyToOutputDirectory`
-equivalent packaging step reads off disk.

This was directly measured on the M38 WP-25 branch (recorded in the
internal history under the WP-25 entry, 2026-07-23): a worktree
created **after** PR #92 (so never had the caches materialize at all)
produced a `.bhm` of 6.0 MB, versus 7.2 MB from an equivalent build on a
working copy that still had them - and the smaller, cache-free build
loaded and ran the module's full interaction surface (recipe tree render,
decision-pill overrides, Ignore toggle, presets) with no missing-file
errors or behavior difference, confirming these two files have no runtime
consumer in the shipped module at all.

**Fixed (M38/WP-29):** the csproj exclusion described as a follow-up item
above has landed. `TaimisToolbench.csproj` now redeclares the imported
`BuildBlishHUDModule` target (same name, declared after the `BlishHUD.targets`
import, so it wins) with an `Exclude` added to the `ref/**` copy for
`ref/wiki_vendor_cache.json` and `ref/item_id_cache.json` - and, since
`MysticForgeSeeder` landed, `ref/mf_item_id_cache.json`, making it three
files.

**Four, as of the repo-hygiene branch:** `ref/vendor_offer_exclusions.json`
joined them. It is the odd one out in being *tracked* - it is hand-verified
data, not a regenerable cache - but it is still a build-time input read only
by `tools/VendorOfferUpdater` (`Program.cs`, `ApplyExclusions`), with no
reader anywhere in `Services/`, `Views/` or `Models/`, so it has no business
in a player's download. It had been shipping in every `.bhm` since it was
created, because it appeared in no `<Content Include>` entry and the packing
glob never consulted that list anyway. Those `<Content Include>` entries are
now gone entirely and this target is the sole owner of what ships.

**Six, as of the dropped-fields audit branch:**
`ref/seasonal_wikitext_cache.json` and `ref/vendor_offers_unresolved.json`
joined them. `tools/VendorOfferUpdater` writes both. The first caches each
vendor page's wikitext for the seasonal-tag pass. The second names the
sections a scrape could not resolve. Each was added to `.gitignore` when it
was introduced and to this `Exclude` never. `release.yml` builds from a clean
checkout, so no published `.bhm` ever carried them. A locally built one did,
which is the artifact step 10 above installs for in-game testing. The
seasonal cache measured 69,672 bytes on one such machine on 2026-09-07. That
is the third time the two lists have drifted apart, and nothing in CI yet
compares them, so adding a `ref/` line to `.gitignore` still means adding the
same file to this `Exclude` by hand.

None of the six is
copied into `$(OutDir)ref` or zipped into the `.bhm` any more, regardless of
whether a developer's working copy has them sitting on disk from running
`tools/VendorOfferUpdater`. Building from an active `VendorOfferUpdater`
development workspace is no longer a concern for this specific gap; a
clean checkout is no longer required to get a cache-free `.bhm`. However,
because the pack target's staging-dir copy zips whatever is already sitting
in `$(OutDir)` rather than only the files this target itself just copied,
a release `.bhm` must still be built after clearing `bin/` and `obj/` -
otherwise a stale, pre-exclusion copy of a cache file left over from an
earlier incremental build can be re-zipped into the output regardless of
the `Exclude` list above.

## What a real release process would still need

These are listed as concrete gaps, not committed-to future work:

- ~~A CI job that builds Release/x64 on a tag and attaches the resulting
  `.bhm` as a GitHub Release asset~~ - done:
  `.github/workflows/release.yml`. Untested against a real tag push at the
  time of writing; the first release exercises it.
- A Blish HUD module-repository listing, so the module is installable from
  inside Blish HUD rather than by downloading a file. This is now the only
  remaining friction in a non-developer install.
- ~~A convention for bumping `manifest.json`'s `version` per release~~ -
  done: the CHANGELOG + `v<version>` tag protocol at the top of this file,
  practiced across v0.2.0 through v0.2.4.
- ~~A decision on the `ref/wiki_vendor_cache.json` / `ref/item_id_cache.json`
  packaging gap~~ - resolved as of M38/WP-29; see the addendum above.
- The two stale `v1.0.0`/`v2.0.0` tags inherited from the original
  `blish-hud/ModuleTemplate` fork (both point at the same 2020 template
  commit, unrelated to this module's actual history) have already been
  removed from the GitHub remote (verified via `gh api
  repos/<owner>/<repo>/tags` returning `[]`). Local clones and worktrees
  predating that deletion may still carry the stale local refs; run
  `git tag -d v1.0.0 v2.0.0` in any such clone to prune them before
  starting a real version tag sequence.
