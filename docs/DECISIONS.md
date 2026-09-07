# Decisions: designs considered and rejected

[`ARCHITECTURE.md`](ARCHITECTURE.md) describes what the module *is*, in the
present tense. This file holds the other half: refactors and designs that
were proposed, evaluated, and deliberately not done. They live here so the
architecture doc can stay a map instead of turning into a rebuttal brief,
and so a reader who arrives at one of these ideas independently can find
out whether it was rejected on merit or simply never considered.

Each entry names the reasoning and, where one exists, links the dated
record. An entry is a decision, not a law: bring new evidence and it can be
reopened.

---

## Splitting `TreeSectionController` into a stateful pair

**Proposal:** bisect `Views/Rendering/TreeSectionController.cs` into a
stateful collaborator (owning `_nodeOverrides`, `_ignoredItemIds`,
`_nodeExpansion`, `_treeNodeStates`) and a separate stateless renderer,
mirroring the six per-render section renderers extracted alongside it in
M38.

**Rejected.** The invariant this class exists to hold is one owner, one
lifetime. It is constructed once in `CraftingPlanView`'s own constructor
rather than freshly per render precisely because its override state must
survive a local pill-click re-solve, and a two-class split would either
duplicate that lifetime management across both halves or reintroduce a
second implicit owner - the same class of bug the single-owner primitives
in [`ARCHITECTURE.md`](ARCHITECTURE.md) section 1 exist to prevent, at the
object-graph level instead of the thread level.

The coupling argument does not favour the split either. Measured
pre-change at `ce64423`, the class is named in 14 production `.cs` files
(`Module.cs`, 9 under `Services/`, 4 under `Views/`), of which 13 are
comment-only; the actual compile-time coupling is 2 references, both in
`Views/CraftingPlanView.cs` (the field declaration and the constructor call
site). A state/render split would relocate half of that across a new seam
rather than shrink it. Doc mentions are a moving target for the same reason
- every entry naming the class, including this one, adds to the count - so
reproduce it with `git grep -c TreeSectionController -- '*.md'` rather than
citing a figure.

**The accepted alternative** is not a class bisection: per the STANDING
RULE in [`../CONTRIBUTING.md`](../CONTRIBUTING.md), each new tree-row or
decision-pill feature extracts its pure text/decision computation into a
Blish-free, unit-tested composer under `Services/` first -
`TreeRowTooltipComposer` is the latest instance of the pattern already
behind `DecisionPillPlanner`, `ValueDetailTooltipBuilder`,
`PillSubduingEvaluator`/`PillSubduingTooltipBuilder` and
`ReceiptCaptionHelper` - and keeps wiring it into `TreeSectionController`'s
existing single-owner shape.

**Record:**
[`dev/archive/known-issues/2026-08-17-tree-row-tooltip-composer-extraction-architecture.md`](../dev/archive/known-issues/2026-08-17-tree-row-tooltip-composer-extraction-architecture.md).

---

## An `ApplyAll` seam over the post-solve annotation passes

**Proposal:** collapse the three producer call sites that invoke the four
post-solve annotation calculators
([`ARCHITECTURE.md`](ARCHITECTURE.md) section 10) into a single `ApplyAll`
entry point, so a fifth pass would be one edit instead of four.

**Rejected as premature.** The four calculators do not share a signature -
they take different inputs (`learnedRecipeIds`, `vendorOffers`,
`characterDisciplines`) - so a shared seam would need its own parameter
object carrying the union of them, which no caller needs today. The cost
being avoided is one extra edit site on a change that happens rarely; the
cost being added is a permanent abstraction that every future pass has to
fit. Revisit if a fifth pass arrives and its inputs are already covered.

**Record:**
[`dev/archive/known-issues/2026-08-17-annotation-detection-post-solve-advisory-list.md`](../dev/archive/known-issues/2026-08-17-annotation-detection-post-solve-advisory-list.md).

---

## WP-26: extracting the scroll/resize/wheel controller

**Cut on 2026-07-23**, after five sibling extractions in the same wave
landed. Full reasoning in [`ARCHITECTURE.md`](ARCHITECTURE.md) section 5,
where it stays because it explains why that machinery is still in
`Views/CraftingPlanView.cs` today - it is present-tense mechanism, not only
history.

---

## Stopping wiki API access over the `robots.txt` entry

**Proposal:** stop using `https://wiki.guildwars2.com/api.php` in
`tools/VendorOfferUpdater` and `tools/MysticForgeSeeder`, because
<https://wiki.guildwars2.com/robots.txt> carries `Disallow: /api.php` for
all user agents, and find another route to the vendor and Mystic Forge
data.

**Rejected. The tools continue to use that endpoint.** The endpoint exists
to serve programmatic clients, and ArenaNet documents it for that purpose
on the same wiki, at
[API:Main](https://wiki.guildwars2.com/wiki/API:Main). A `robots.txt` entry
that keeps general crawlers off a dynamic endpoint does not describe the
documented client path. Both sources are linked above, so a reader can
weigh them without taking this entry's word for either.

**What we take on in exchange.** The reading above is only defensible while
these tools behave as the documented client path expects, so the obligations
are part of the decision rather than a separate aspiration. The page
that states each rule, and the file that meets or misses it, are in
[`api-client-contracts.md`](api-client-contracts.md) section 1; this list
is the status, not a second copy of the evidence.

| Obligation | Status |
| --- | --- |
| Identify the client, with a contact address | Met in `tools/MysticForgeSeeder/Program.cs` |
| Serial requests, paced, never parallel | Met by both clients |
| Send `maxlag` | Open in `tools/MysticForgeSeeder/WikiRecipeClient.cs` |
| Honour `Retry-After` in its delta and its HTTP-date form | Partly met: both clients read the delta form only |
| Back off on a refusal rather than retrying hard | Met for a refusal carried by the status code; open for one carried in the body, which `tools/MysticForgeSeeder/WikiRecipeClient.cs` cannot yet distinguish from an empty page |
| Cache, so the same data is not fetched twice | Met |

An obligation listed as open is a debt against this decision, not a
footnote to it.

**What would reopen it:** ArenaNet asking us to stop, or the client
guidance on
[API:Main](https://wiki.guildwars2.com/wiki/API:Main) changing so that it no
longer describes `api.php` as a path third-party clients are meant to use.

---

## M38's `Services/` foldering target

**Proposal:** split the flat `Services/` directory into `Services/Pricing/`,
`Services/Planning/`, `Services/Persistence/`, `Services/Vendor/`,
`Services/Layout/` and `Services/Api/`, as set out in the M38 architecture
proposal, section 5 (internal working document).

**Not executed.** None of the six directories exists; `Services/` is 141
flat files with two subdirectories (`Recipes/`, `Diagnostics/`) that arrived
for other reasons. Because `TaimisToolbench.csproj` lists every file
explicitly, each move is also a csproj path edit, and the plan's own
sequencing note flagged high merge-conflict potential against the branches
in flight at the time. The branches kept arriving, the move never had a
quiet window, and the benefit is navigational rather than behavioral - which
[`README.md`](README.md)'s code-layout table now delivers at zero merge
cost. Recorded in the DEFERRED list in
[`KNOWN-ISSUES.md`](KNOWN-ISSUES.md); still available to a future wave that
wants it.
