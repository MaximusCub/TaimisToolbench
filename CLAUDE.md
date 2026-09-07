# TaimisToolbench - Project Rules

## Tool Paths

`dotnet` and `gh` may not be on the shell PATH. Try each location in order; use the first one that works. Do NOT search the filesystem unless all listed paths fail.

### dotnet

1. `"/mnt/c/Program Files/dotnet/dotnet.exe"` (WSL)
2. `dotnet` (native Windows / on PATH)

When invoking Windows `dotnet.exe` from WSL, pass **Windows-style project paths** (e.g., `C:/Dev/Blish/TaimisToolbench/...`) -- MSBuild cannot resolve `/mnt/` paths.

### gh (GitHub CLI)

1. `"/mnt/c/Program Files/GitHub CLI/gh.exe"` (WSL)
2. `"/c/Program Files/GitHub CLI/gh.exe"` (Git Bash / MSYS2)
3. `gh` (native Windows / on PATH)

---

## Build & Test

- Restore (every fresh clone AND every fresh worktree, MANDATORY): `./tools/bootstrap.sh`
  - `packages/` is gitignored and this is a classic `packages.config` project, so nothing is on disk until `nuget.exe` restores it. **`dotnet restore` does NOT do this** - it and `dotnet msbuild -t:restore` both print "Nothing to do. None of the projects specified contain packages to restore." and leave the build failing on `The missing file is packages\BlishHUD.1.3.0\build\BlishHUD.targets`. Windows-only build; CI runs `windows-latest`, whose image already carries `nuget.exe`, so CI calls `nuget restore` directly and needs no bootstrap.
  - `tools/bootstrap.sh` fetches `nuget.exe` once into a shared cache outside the tree and then restores. It is idempotent, so running it on an already-restored tree costs nothing. A NEW WORKTREE NEEDS IT TOO: package `HintPath`s are relative (`packages\...`), so `packages/` must sit at each worktree's own root and cannot be shared between them.
- Build: `<dotnet> build TaimisToolbench.csproj -p:Platform=x64`
- Tests: `<dotnet> test TaimisToolbench.sln`
  - There are THREE test projects and CI runs all three (`tests/TaimisToolbench.Tests`, `tests/TaimisToolbench.RecipeSeeder.Tests`, `tests/VendorOfferUpdater.Tests`). Testing only the first misses the golden-vector suite that pins `tools/VendorOfferUpdater/VendorOfferHasher.cs` (the `offerId` contract for `ref/vendor_offers.json`) against `tests/shared/vendor_offer_hasher_vectors.json`. The first two target `net48`, the third `net8.0`.
- `<dotnet>` refers to whichever dotnet path resolved above
- `.csproj` uses explicit `<Compile Include>` - new `.cs` files must be registered
- Changes must be incremental with logical git commits
- Prefer one commit per logical step (e.g., refactor, behavior change, tests, UI polish)
- **Browser automation**: Do not use Chrome/browser MCP tools without asking the user first

---

## Code Style

- Use Allman brace style for C#
- Keep edits focused and minimal
- Avoid unrelated refactors or formatting churn
- Follow existing patterns in neighboring files before introducing new structure
- **Comments state what the code cannot:** A comment survives only if it carries something a reader cannot get from the code in front of them plus one `git log -S` - an external fact (vendor binary behaviour, a Windows constant, a GW2 API quirk), a measurement, a derivation, or an invariant a caller can violate. Refactor provenance ("moved verbatim out of X", "no logic changes"), review rebuttals ("deliberate, not an oversight"), bug-discovery narratives and session-local jargon ("per the brief", "directive B", milestone codes like `M33 C2b`) go in the commit message or in `docs/`, never in the source - and never in a runtime string. Keep a contiguous comment block to roughly 12 lines of inline `//` prose, or 20 lines of `///` XML doc; the two numbers differ because an inline block is pure prose while a doc block spends lines on `<summary>`, `<para>`, `<list>` and `<param>` structure, so the same amount of prose costs more of them. Both are the numbers CI enforces, in `tools/comment-budgets.py`. Past either, leave the caller-facing invariant inline and move the narrative to a `docs/ARCHITECTURE.md` section the comment cites by repo-relative path. State a rule once and point at it from the other sites; do not restate it, and never claim a comment "mirrors X exactly" - nothing enforces that.
- **Cite docs by repo-relative path:** a `.md` name in a `.cs` comment must resolve against the tree (`dev/proposals/d2-log-system.md`, not `d2-log-system.md`). CI fails the build on a citation that resolves nowhere.
- **ASCII-only in source (.cs):** Source files must contain only ASCII characters (U+0000-U+007F). Do not paste raw Unicode into code, comments, or string literals. If Unicode must be shown at runtime (UI glyphs, item names, etc.), represent it using escapes (e.g., `"\u00D7"`) or data returned by the GW2 API.
- **Escaping a codepoint does not make it render:** Blish exposes one text face, and Menomonia carries exactly 226 codepoints - ASCII, Latin-1, and about thirty punctuation marks. A codepoint outside that set draws nothing *and* advances zero pixels, so it is invisible on screen, invisible to `MeasureString` and invisible to every layout assertion; five such glyphs shipped to players before this rule existed (KNOWN-ISSUES #64). Geometric shapes are absent wholesale: no triangles (`\u25B2` `\u25BC` `\u25B6`), no cross or check (`\u2715` `\u2713`), no circles (`\u25CF` `\u25CB`), no arrows, no box drawing. Present and useful: `\u2014` em dash, `\u2013` en dash, `\u2022` bullet, `\u00B7` middle dot, `\u00D7` multiplication sign, `\u2026` ellipsis, `\u2039` / `\u203A` angle quotes, `\u00B0` degree, `\u00A6` broken bar. The full set is `docs/font-codepoints.txt`, generated from the shipped font assets by `tools/dump-font-codepoints.py` and enforced by the "UI glyph escapes exist in the shipped font" step in `.github/workflows/tests.yml`. For anything geometric, use a texture or a drawn primitive rather than a character - but note that `StandardButton` forces black text over light button art and blits its `Icon` untinted, so Blish's white affordance textures are no escape hatch inside one.
- **No em-dashes in source or config:** Never use em-dash (`\u2014`) in source code, comments, string literals, config files, test code, or any non-user-facing text unless specifically required. Use a plain ASCII hyphen (`-`) or double-hyphen (`--`) instead. Em-dashes are only acceptable in correctly-encoded user presentation layers (e.g., UI text rendered via BlishHUD controls).

---

# Repo Invariants (Non-Negotiable)

These rules MUST always be followed. They override any conflicting defaults.

---

## Testing

- Tests must exercise **real production code paths**
- No contract-mirror tests
- No fake logic tests
- No fake file I/O tests
- Use real `SnapshotStore` / `StatusStore` with temporary directories when testing storage
- Tests must NEVER reference:
  - Blish HUD
  - BlishHUD.exe
  - Gw2Sharp
  - Any UI code

Tests must remain completely Blish-free.

---

## UI & Display

- Item, currency, and vendor IDs are internal-only - never display them to users
- Coin icons MUST appear to the RIGHT of the number (matching GW2 in-game style):

  `123[gold icon] 45[silver icon] 67[copper icon]`

- This applies everywhere coin amounts are shown: coin panel, tooltips, item values, vendor prices, etc.
- GW2 coin asset IDs:
  - Gold = 156904
  - Silver = 156907
  - Copper = 156902

---

## Data & APIs

- Prefer official GW2 APIs (`api.guildwars2.com`)
- Do not invent data when APIs are missing
- `gw2efficiency` is research-only - the module must NEVER call it at runtime
- Pricing logic must preserve multiple sources and avoid invalid currency comparisons

---

# Self-Review After Every Edit (Edit -> Review -> Fix Loop)

Goal: Reduce back-and-forth by enforcing a deliberate adversarial review mindset after every runtime-affecting change.

---

## Code Reviewer Mode - REQUIRED MINDSET SHIFT

When entering Code Reviewer Mode, you MUST change perspective:

- You are no longer the author.
- You are a skeptical senior engineer reviewing someone else's pull request.
- Assume the author made subtle mistakes.
- Actively try to break the code mentally.
- Look for edge cases, regressions, and invariant violations.
- Challenge assumptions.
- Look for architectural drift.
- Look for hidden coupling.
- Look for future merge conflicts.
- Do NOT defend the implementation.
- Your job is to find faults.

---

## When to Apply

Apply this loop for any change affecting:

- Code
- Tests
- Config
- Build behavior
- Runtime logic

Docs-only changes may skip the strict adversarial pass but must be checked for duplication, contradictions, and stale guidance.

---

## Per-File Review Process

After modifying ANY runtime-affecting file:

1. Pause.
2. Switch to Code Reviewer Mode.
3. Review ONLY the file you just changed (plus directly impacted call sites/tests if necessary).

During review, explicitly evaluate:

- What happens with null inputs?
- What happens with empty collections?
- What happens with large inputs?
- What happens under cancellation?
- What happens under API failure?
- Could this produce inconsistent state?
- Could this break existing tests?
- Does this violate any Repo Invariants?
- Does this introduce unintended coupling?
- Does this create future merge hotspots?
- Is error handling correct and consistent?
- Are there race conditions?
- Is duplicated logic introduced?
- Are tests proving behavior or merely mirroring implementation?

---

## Reviewer Checklist - Best Practices and Performance (Diff-Scoped)

During Code Reviewer Mode, evaluate the change **relative to the existing codebase**. The goal is to prevent introducing new problems, not to redesign the project.

### Standards and Consistency

- Does this follow existing repo patterns (naming, layout, logging, DI usage)?
- Does this match established structure in neighboring files?
- Did this introduce a new abstraction, helper, or pattern unnecessarily?
  - If yes, can it reuse an existing pattern instead?

### Scope Discipline

- Is the change narrowly scoped to the task?
- Did it sneak in unrelated refactors?
- Did it expand public surface area without necessity?
- Did it increase coupling between modules?

### Performance (Regression Prevention Only)

Focus on the delta, not a whole-project performance audit.

- Does this add new work in hot paths (UI render, plan generation loops)?
- Does it introduce new allocations inside loops?
- Does it add repeated API/network calls?
- Does it introduce blocking or long-running work on the UI thread?
- Does it increase memory retention (unbounded lists, caches, logs)?
- Does it degrade behavior on low-end systems (polling, timers, excessive updates)?

If a likely regression is detected, it is at least **Must Fix** unless clearly justified.

If performance-sensitive code was touched, the review must include at least one explicit note about allocation/work frequency impact.

### Efficiency Principle

Prefer simple, predictable solutions over clever ones.
Avoid adding infrastructure or framework-like patterns unless explicitly required by the milestone.

---

## Issue Classification

Every issue must be classified as exactly one of:

- **Critical**
  - Crashes
  - Broken build/tests
  - Incorrect logic
  - Data corruption
  - Severe regression
  - Violates repo invariants

- **Must Fix**
  - Likely bug
  - Edge case failure
  - Test gap that risks regression
  - Performance trap
  - Leaky abstraction
  - Future merge hazard
  - Misleading API surface

- **Nice to Have**
  - Minor refactor
  - Readability improvement
  - Micro-optimization
  - Non-blocking polish

---

## Mandatory Fix Loop

- Fix ALL **Critical** issues.
- Fix ALL **Must Fix** issues.
- Re-run the review mentally.
- Repeat until:
  - Zero Critical
  - Zero Must Fix

Only then proceed to another file.

---

## End-of-Milestone Adversarial Review

After milestone completion:

1. Review the entire change set as if you are an external reviewer unfamiliar with the code.
2. Evaluate:
   - Cross-file consistency
   - API coherence
   - Architecture alignment
   - Test realism (real behavior vs mirrored logic)
   - Regression risk
   - Repo invariant compliance
3. Again classify findings as Critical / Must Fix / Nice to Have.
4. Automatically fix all Critical and Must Fix.
5. Repeat until clean.

---

## Reviewer Integrity Rule

If you cannot find at least one Nice to Have item during review of a non-trivial change, assume you did not review deeply enough and review again.

The goal is defensive engineering, not perfection.

---

# PR Workflow (STRICT)

Every change ships through a GitHub Pull Request, on its own branch, with a
green build and a green SOLUTION-level test run before the PR exists.

**Pull requests are NOT reviewed, and are not to be posted for review**
(decided 2026-08-30, reaffirmed 2026-09-03). Do not stop and wait on a PR.
Once CI is green: merge it, deploy the build, and report what to look at in
game.

Green CI is the FLOOR that qualifies a build for in-game testing. It is never
the acceptance. The gate is in-game review of the functionality, so an item is
SHIPPED when a PR merges and DONE only when in-game review confirms it works.

This paragraph exists because the rule that overrides it used to live only in
an untracked file. A git worktree checks out tracked files only, so an agent
working in one could not see it and would wait for a review that never comes.

## Branch

- Create a dedicated milestone branch:
  `git switch -c <milestone-branch>`
- Branch name must reflect the milestone.

## Validation

Run (using the resolved `<dotnet>` from **Tool Paths**):

`<dotnet> build TaimisToolbench.csproj -p:Platform=x64`
`<dotnet> test TaimisToolbench.sln`

Both must pass before PR creation. The solution-level test run is what
matches CI; see **Build & Test** above for why the single-project command
is not enough.

## Commit & Push

- Commit logically grouped changes.
- Push:
  `git push -u origin <milestone-branch>`

## GitHub CLI (`gh`)

Use the resolved `gh` path from the **Tool Paths** section above.

## PR Creation

Use:

`<gh> pr create --base master --head <milestone-branch> --title "<concise milestone title>" --body-file <tempfile>`

where `<tempfile>` is `.github/PULL_REQUEST_TEMPLATE.md` with every section filled in.

### Required PR Body

Fill in `.github/PULL_REQUEST_TEMPLATE.md` - that file is the only PR body
template, for agents and humans alike, and `gh pr create` picks it up
automatically when no `--body`/`--body-file` is passed. Do not restate it
here: an inline copy drifted from it once already and silently dropped the
ASCII-only/no-em-dash row from the invariants checklist.

If a PR already exists:
- Push additional commits to the same branch.
- Update the PR body to reflect the current state.

---

# Terminal Output Rules (End of Milestone)

At milestone completion, output ONLY:

- PR URL
- Short consolidated summary:
  - What changed (high level)
  - Build/test results
  - Remaining Nice to Have items
- Any special reviewer notes

Do NOT include inline diffs, file dumps, or large pasted code blocks.

---

## Intermediate / Cache Files

- Intermediate caches (e.g., `wiki_vendor_cache.json`, `item_id_cache.json`, build artifacts) must NOT be committed unless explicitly requested.
- `ref/wiki_vendor_cache.json` and `ref/item_id_cache.json` were previously tracked in git history; they have been untracked and added to `.gitignore` (M38/WP-28) to match this rule going forward. They remain required on disk as developer-side inputs to `tools/VendorOfferUpdater` - do not delete them locally, just keep them out of git.
- If such files exist in the working tree, exclude them and mention them in the summary.

---

## Final Notes

- Never skip the immediate review after editing a runtime-affecting file.
- Update/add tests as part of Must Fix when needed to prevent regressions.
- Keep changes small and focused.
- Do not introduce any Blish HUD/BlishHUD.exe references into tests.
- Always preserve real production code path coverage.
