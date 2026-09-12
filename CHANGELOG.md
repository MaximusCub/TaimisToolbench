# Changelog

Every version deployed to a live Blish HUD install gets an entry here and a
matching `v<version>` git tag on the release commit, so any two shipped
builds can be compared with `git diff v0.2.0..v0.2.1`. The About tab shows
the running version.

## 0.4.0 - 2026-09-12

Legendary gear finally tells you what is socketed into it, the Shopping
List and the Crafting Steps get windows of their own, and the account
snapshot refreshes in a fraction of the time it used to take.

### Added
- **Socketed runes and infusions show up as their own rows.** Upgrades
  fitted into equipped legendary gear were invisible to the Account
  Snapshot before; each now gets a row that reads
  `Equipped: <character> (<item>)`, so a rune sitting in a weapon counts
  as one you own.
- **Pop-out windows for the Shopping List and the Crafting Steps.** Either
  section opens in its own window that you can move, resize and sort
  independently. Ticks carry across, the window survives the main one
  closing, and it remembers its opacity.
- **Equipment templates count as a storage location.** Gear held only in a
  template is no longer missed, and a legendary shared by several
  templates is still counted once.
- **Required Recipes gained Cost and Sold By columns.** Each missing
  recipe sheet shows its live price and the merchant who actually sells
  it, with the same detail written into Plan Notes.
- **Item tooltips read what is in a stack.** They show the upgrades and
  infusions socketed into it, its empty upgrade slots, the base item under
  a transmuted skin, and the rune bonuses a character has active.
- **The Account Snapshot says where you hold each item.** Hovering a row
  gives the full line: which characters wear it, which bags and bank tabs
  hold it, and which characters an item in the Legendary Armory is
  equipped on.
- **A gated vendor now says what it wants.** When a merchant needs an
  achievement, mastery or other unlock before it will trade, the plan says
  so instead of quoting a price you cannot pay.

### Changed
- **The Account Snapshot refreshes in 6 to 8 seconds** instead of about 36.
  The fetch now runs its account-wide calls, its characters and its lookups
  in parallel rather than one at a time, and it only loads the icons you
  can actually see before filling in the rest.
- **The snapshot filters are two sets, Locations and Characters.** Each set
  has its own All checkbox. Shift-clicking a box isolates it within its
  set, and shift-clicking it again inverts that set.
- **The module's settings moved into its own Settings tab.** Blish's
  settings panel now carries one line and a button that opens the tab.
- **Homestead Refinement settings are dropdowns** rather than numbers you
  type.
- **The Crafting Ranker's saved list is your wishlist.** Its currency lines
  now read what you hold over what you need, for example "1,000/1,500",
  instead of telling you only how far short you are.
- **Crafting Plan notes carry item icons, names and links**, with the links
  styled so they read as clickable.
- **The About tab was rebuilt**, with a new description of the module, a
  Credits section broken into subsections, and links that work.
- **The Snapshot tab is now the Account Snapshot tab.**
- **The Shopping List reads amount first.** Amount sits at the far left and
  the Each and Total figures are right-aligned under their headers.
- **The plan tells you when your account data is overdue for a refresh**,
  and raises the stale-data notice once per outage rather than on every
  Generate.

### Fixed
- Right-clicking an item opened two wiki tabs. It opens one.
- The Crafting Ranker's recipe completion percentage was wrong. It now
  scores the same recipes the plan lists, and a gate it could not measure
  reports as unscored instead of 100%.
- Generate Plan pressed straight after opening the module failed with a
  refresh error. It now waits for the game to hand the module its API key.
- A character whose gear failed to load no longer costs you that
  character's items; the snapshot says which character it could not read
  rather than quietly reporting less than you own.
- A lone search result no longer has the bottom of its item icon clipped.
- Pop Out could throw before its window was built and open nothing at all.
- The Shopping List no longer shows a per-unit price for a row that has
  none.

### Removed
- **Four recipes came out of the shipped seed data.** They described the
  Infinite Trebuchet Blueprint and the three WvW items that unlock it, and
  they did not come from the official GW2 API or the wiki. Three of the
  four items are still priced from the shipped vendor offers. The Infinite
  Trebuchet Blueprint now shows UNKNOWN.

## 0.3.0 - 2026-08-26

Two new tabs land - a craftability ranker and a plan history - and the
recipe cache stops forgetting what it already knows.

### Added
- **GW2 Crafting Helper is now Taimi's Toolbench.** The module, its
  namespace, assemblies and repository all carry the new name from this
  version on. This is a rename only - no behaviour changes ride along
  with it. (The old name in this entry is the one deliberate mention
  the rename leaves behind.)
- **Crafting Ranker tab**: a persistent watchlist that ranks the items
  you track by how close each is to craftable. Higher-priority items
  claim your shared coin, currencies and materials first, and every row
  scores readiness across four gates: materials (at buy-order prices),
  account currencies, time-gated daily crafts, and your characters'
  crafting disciplines.
- **Plan History tab**: every successful Generate is recorded. Each row
  offers View (a frozen summary, no network), Open (restore that exact
  plan with its pills live) and Re-solve (the same request at today's
  prices). Rows can be pinned to survive the cap; a Settings entry
  (default 25) controls how many are kept.
- **Restored plans regenerate without retyping.** After a restart the
  item rows, Use Own Materials and price basis that produced the plan
  come back with it, so Generate Plan works immediately instead of
  answering "Add at least one item".

### Changed
- **A game patch no longer wipes the recipe cache.** Recipes do not
  change when the build number does (measured: 13,371 of 13,371 seed
  recipes byte-identical across a 275-build gap), so the cache survives
  patches; one cheap background check per new game build verifies the
  recipe corpus against the live API and repairs any additions. "Not
  craftable" answers now ship in the seed and are served instantly -
  the cold misses that used to hit the API on a first plan each session
  are gone (measured on Gift of Fortune: 26 misses to 0). The Clear
  Cache button now clears the recipe cache as well.
- **Tooltips finish their fidelity pass against the real game client.**
  The full rarity palette is sampled from lossless live captures (the
  rarity word itself now carries its rarity colour), the background
  draws the game's own canvas texture instead of a flat tint, the
  upgrade-bonus blue matches the measured value, the content width
  matches the game's wrap, nourishment sub-effects go grey under a
  white first line, and consecutive infusion lines get their spacing.
- **The module opens on the Crafting Plan tab.** It is the one tab that
  works with no API key at all; Snapshot, the old first tab, could only
  ask for a key. Tab order is now Crafting Plan, Snapshot, Log, Plan
  History, Crafting Ranker, Settings, About.

### Fixed
- The recipe cache no longer deletes itself on every launch. It was
  stamped with build id 0 and treated every start as a game patch, so
  everything learned in a session was thrown away on the next.
- One failed recipe search no longer permanently marks an item
  uncraftable, and one failed price batch no longer marks a whole
  plan's items unpriceable.
- Use Own Materials no longer claims materials it cannot see: without
  account data the checkbox is disabled instead of silently wrong.
- Restoring a plan no longer flags "Settings changed" or offers to
  regenerate a plan you just got back.
- A saved plan from an older version of the module is reported as
  "starting fresh", not as file corruption.
- Learned recipes are forgotten when the API key changes, so a second
  account does not inherit the first one's unlocks.
- A game-build lookup that hangs at startup is abandoned and retried
  instead of stalling the status line, and closing the module no longer
  pulls objects out from under work still in flight.

### Internal
- The style gate (StyleCop analyzers, warnings as errors) now covers
  every project in the solution; CI enforces the repo invariants that
  were previously prose-only (ASCII-only source, csproj/disk sync,
  Blish-free tests, file-size budgets, citation resolution, a
  public-surface ratchet); `internal` is the default visibility. The
  module test suite grew from 2,808 to 3,178 tests (plus 233 tool
  tests), all exercising real production code paths.

## 0.2.4 - 2026-08-25

The rest of the module catches up with the crafting planner, plus a
second round of in-game testing.

### Changed
- **Every tab now uses the width it occupies.** Settings becomes a
  two-column board (inputs aligned, descriptions at a readable measure,
  Save and the "applies immediately" markers anchored right), the Log
  gains a Time / Tag / Message gutter with the message owning the
  remaining space, About becomes a two-column document, and the Snapshot
  header stops hugging the left edge. No more stranded empty half-panel.
- **One type hierarchy across the app**: section titles, column headers
  and status lines now read as distinct tiers on Snapshot, Settings, Log
  and About, the way the crafting plan already did.
- **One icon treatment everywhere.** Item icons had inconsistent
  borders and hovers depending on where they appeared; every item icon
  now gets the same rarity frame, the same tooltip and the same
  placeholder when art is missing.
- The Total Cost section always shows its whole formula. It used to
  collapse to a lone "Actual Cost to Craft" tile whenever a term was
  zero - which is most plans - hiding two thirds of the section.
- Item tooltips are translucent like the game's own, with the fine
  border the game draws.
- The default click volume is 35% (it was far too loud a jump before);
  your own setting is untouched.

### Added
- Sortable column headers highlight on hover, and the whole header cell
  is clickable, not just its text.
- Snapshot rows show the same rich item tooltip the plan does.
- On a fresh install the module now takes a snapshot immediately
  instead of leaving you empty until the refresh timer or a manual
  click.
- **581 more items are recognised as vendor purchases**, including
  Visions of Eternity gifts that previously showed as UNKNOWN.

### Fixed
- **Mystic Forge recipes no longer show as UNKNOWN.** Gift of Rays and
  its relatives now plan properly - forge recipes added to the wiki data
  were invisible unless the whole seed was regenerated.
- Toggling IGNORE no longer makes the plan jump under your cursor; the
  row you are pointing at stays where it is.
- The plan's own root item can no longer be ignored.
- A plan whose cost is genuinely zero shows zeros rather than an empty
  section; costs that could not be measured say so instead of being
  dressed up as zero.
- The corner icon matches the size of the game's own top-row icons.
- Closing the module with a text box focused no longer swallows your
  keyboard in game.

## 0.2.3 - 2026-08-24

The plan-view redesign (PR #173): the crafting planner stops wasting
your screen.

### Changed
- Every plan section now justifies to the full window width: item names
  flex, and pills, costs, amounts, levels and statuses anchor to the
  right edge at every window size - no more columns crammed left with
  stranded dead space. The minimum window width drops from 1478px to a
  measured 1378px.
- A real type hierarchy: section titles at 24 bold, column headers at
  20 bold, an 18-bold status line with a larger spinner - each tier
  visibly a step above the next. Character and discipline names render
  at full body size everywhere.
- The Total Cost section is a full-width formula band; the currency
  table's numbers right-align under proper headers.
- Required Recipes is one line per row with its own Discipline column;
  the Shopping List gains a Source column with aligned, color-coded
  badges (vendor teal, unknown red) and a sortable header.

### Added
- Overrides and Ignored counts as persistent chips with guarded Clear
  buttons: clearing confirms with plain-language consequences, and
  actions that would change nothing skip the dialog and say why.
  The status line reports events only.
- Generate Plan explains itself on hover (it clears manual decisions
  and ignore marks - now it says so before you click).

### Fixed
- Rapidly toggling a pill without moving the mouse no longer drops
  clicks (re-solves now update the existing controls in place instead
  of rebuilding them under your cursor).

## 0.2.2 - 2026-08-24

Second round of in-game testing plus the pixel-authenticity wave (PRs #163-#170).

### Added
- Item tooltips now duplicate the in-game visual style, measured
  pixel-by-pixel from real game captures: the framed item icon header,
  white identity block with the name alone carrying rarity color, teal
  flavour text, light-blue rune/sigil bonus lines, the game's own line
  order, a translucent black canvas, and the vendor value as an
  unlabelled final coin line (absent on unsellable items).
- Those rich tooltips appear on every item surface: recipe tree rows,
  Used Materials, the Shopping List, and Snapshot results - and restored
  plans fetch their stat data in the background so hovers work right
  after a restart without regenerating.
- Item stat blocks in tooltips: attributes, defense/weapon strength,
  upgrade bonuses, binding, level - computed from the same API data the
  plan already fetches (zero extra requests).
- The Snapshot tab lays items out in multiple columns when the window is
  wide enough.
- A Click Volume slider in Settings (with live readout and a Test
  button): the module's click sound was nearly inaudible at Blish's
  volume ceiling; it now plays at your chosen level, default well above
  the old cap, and 0 turns it off.
- Searching the Snapshot with a single letter that only matches a
  character name now explains why nothing shows yet.

### Fixed
- Dismissing the module window no longer strands your keyboard: closing
  with Escape, switching tabs, or pressing Enter in a box previously
  could leave GW2 ignoring all keys until you clicked. Root-caused to a
  Blish focus-slot quirk (plus one bug of ours) and fixed at every text
  box.
- The plan's own root item no longer offers an IGNORE pill, and a plan
  whose cost is genuinely zero renders the full cost formula at 0
  instead of collapsing the Total Cost section; zeros caused by
  unpriceable items stay honestly unexplained instead of being dressed
  up as profit.
- Rapid pill toggling and table re-sorts no longer lose your sort state
  on re-solve; sorting resets only when a new plan arrives.

### Changed
- All row and body text moved up a size (Menomonia 16) with every layout
  constant re-derived from measured font metrics; the minimum window
  width follows the same measurements.
- The corner icon is re-padded to match the size of GW2's own top-row
  icons (it rendered noticeably larger than its neighbors).

## 0.2.1 - 2026-08-24

First round of feedback from in-game use, fixed and shipped (PRs #156-#161).

### Fixed
- The Clear Cache confirmation dialog now fits its whole message and its
  title bar renders cleanly; a second layout bug that clipped the last
  wrapped line mid-glyph was caught on the sandbox check and fixed too.
- The Settings currency valuation boxes now read as inputs: "Currency /
  Copper per unit" column headers, each box hinting its own default, and an
  instruction line. (The override mechanics were verified working all
  along - type a number, press Save, the tag shows "was N".)
- The item search's suggestion list drops directly under the box instead of
  floating off to the right.
- Recipe Tree cost values align under the "Cost" column header for coin and
  currency rows alike.
- Letters with descenders ("y", "g") no longer render with their tails
  clipped; swept across all row labels.
- Deep crafting plans (for example +24 Agony Infusion, the deepest chain in
  the game) now restore correctly after a restart - they previously saved
  but silently failed to load.

### Changed
- Total Cost band: all three tiles use the same coin text size; "Actual
  Cost to Craft" is highlighted with a translucent gold box instead of a
  larger font; the currency table is centered.
- The minimum window width is now 1436px, sized by traversing every recipe
  in the game so the deepest tree renders without truncation (research
  in docs/research/minimum-window-width.md). Narrow screens get a
  screen-fitted floor instead of an off-screen window. The decision-pill
  column widened so the standard four-pill run always fits.
- The plan-strip and Snapshot-refresh spinners are Blish HUD's own circular
  painterly spinner instead of rotating ASCII characters.

### Added
- Used Materials and Shopping List are sortable by clicking column headers,
  with ^ / v indicators; a third click restores plan order. Mixed
  coin/currency columns sort coherently without inventing exchange rates.
- Leaving the Settings tab with unsaved edits prompts to Save or Discard
  (window close included); reverting a field to its original value counts
  as clean.
- Buttons, pills, sort headers and suggestion rows dim while pressed and
  restore on release, and clicks play Blish's click sound. (Blish 1.3.0's
  own StandardButton is silent due to an upstream asset-path bug; the
  module plays the correctly-resolved sound and documents the one-line
  removal if Blish ever fixes theirs.)

## 0.2.0 - 2026-08-23

First stamped release for in-game testing. Highlights relative to the unversioned
development era:

- Full 30-finding UX/visual audit implemented (visible tree carets, cost
  readability, interaction honesty on pills, table density, Settings
  restructure, log readability, Plan Notes wrapping, consistency sweep).
- Central tooltip facility: opaque tooltips with real coin icons, wrapped
  text, four-edge screen clamping, one shared surface (Blish never disposes
  tooltips; its own path leaks per control).
- Per-character source checkboxes and character-name search on the
  Snapshot tab (2+ letters to match names); sticky filters; setting-driven
  snapshot staleness; Delete Log File; typed item names generate without a
  suggestion pick, with ambiguous names called out.
- Truly modal confirmation dialogs, ellipsized log lines with full-text
  tooltips, and a large body of gate-verified fixes recorded in
  docs/KNOWN-ISSUES.md.

## Before 0.2.0

Unversioned development (manifest said 0.1.0 throughout). History lives in
git and docs/KNOWN-ISSUES.md.
