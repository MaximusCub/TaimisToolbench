using System.Collections.Generic;
using TaimisToolbench.Services;

namespace TaimisToolbench.Models
{
    internal enum PlanSectionType
    {
        Summary,
        UsedMaterials,
        ShoppingList,
        CraftingSteps,
        RequiredDisciplines,
        RequiredRecipes,

        // Plan Notes - ONE flat advisory section carrying every note kind,
        // not a section per kind: excess/reclaim, competency gaps, and the
        // Mystic-Clover-yield forge-scope caveat, in that fixed order - see
        // PlanViewModelBuilder.BuildNotesSection. Always last (Build()'s
        // section 7) since every note kind is a caveat ABOUT facts shown in
        // an earlier section. No PlanContentHeightMath case is added for
        // this type on purpose: a note wraps to as many fixed-height line
        // rows as its text needs at the current width, so rows.Count is not
        // the row count on screen. Its renderer reports the height it built
        // instead (see NotesSectionRenderer's own doc comment).
        Notes,

        // Not a member of PlanViewModel.Sections (the tree renders from
        // PlanViewModel.TreeRoot, not a row list) - used only as a
        // dictionary key so its header expansion persists like every
        // other section's.
        RecipeTree,
    }

    internal enum PlanRowType
    {
        CurrencyCost,
        UsedMaterial,
        ShoppingBuy,
        ShoppingVendor,
        ShoppingCurrency,
        ShoppingUnknown,
        CraftStep,
        DisciplineRow,
        RecipeRow,

        // Plain informational line in the Crafting Steps section
        // - a vendor-capped item whose merged demand exceeds its
        // offer's daily/weekly purchase cap. Never numbered/badged like a
        // CraftStep row; rendered via the same plain-text row pattern as
        // any other fallback text row.
        TimegatedNotice,

        // One plain informational line appended to the Summary/Total Cost
        // section for a genuine multi-item batch (2+ requested items),
        // describing the batch-level Sell value/Profit rollup - see
        // SellSideEconomics.ApplyBatchSellSideEconomics and
        // PlanViewModelBuilder.BuildSummarySection. That rollup has no
        // craft-vs-buy filter (a bought-but-tradable root with a live sell
        // price still contributes), so the Label text does not reuse gw2e's
        // "Profit numbers are the sum of all crafted recipes." banner
        // (docs/gw2e-parity-spec.md, and KNOWN-ISSUES #25's divergence
        // record). Rendered via TimegatedNotice's plain-text row pattern.
        MultiItemNote,

        // One tile of the Total Cost section's first formula band -
        // "Total Materials Value - Your Materials Used = Actual Cost to
        // Craft". Collapses to a single "Actual Cost to Craft" tile only
        // when there is no materials-used middle term AND the plan has a
        // real cost to show; a plan whose coin cost and materials-used term
        // are both zero renders all three tiles at 0
        // (PlanViewModelBuilder.BuildCostFormulaBand's collapse rule), as
        // does a zero produced by unpriceable nodes - those carry
        // PlanViewModelBuilder.UnpricedTileMarker plus a SummaryFootnote
        // row. Rendered as an equal-width stat tile by SummarySectionRenderer.
        CostFormulaTile,

        // One tile of the Total Cost section's second formula band -
        // "Sell Value - Total Materials Value = Profit/Loss if Sold" -
        // present only when the plan has a live sell price
        // (CraftingPlanResult.NetSaleValue.HasValue). Always exactly 3 rows
        // of this type when present - no collapse rule, the profit formula
        // is meaningless with fewer than 3 terms.
        ProfitFormulaTile,

        // The Total Cost section's subdued footnote rows at the bottom of
        // the section: the trading-post pricing-basis line, always
        // present, preceded by the unpriced-items line when the plan has
        // one (PlanViewModelBuilder.UnpricedFootnoteText).
        SummaryFootnote,

        // The one shared
        // row shape for every line in PlanSectionType.Notes - excess/
        // reclaim, competency, and forge-scope lines all use this single
        // member rather than one row type per note kind. Label carries the
        // full self-describing sentence; CoinValue is 0 for a plain-text
        // line (competency, forge-scope) and > 0 for an excess-item or
        // total line - NotesSectionRenderer draws a right-aligned coin cell
        // only when CoinValue > 0, mirroring CoinCurrencyRenderer's own
        // "hasCoin = copper > 0" convention. Never carries StatusTag/
        // BadgeText - the Notes section draws no pills.
        NoteLine,
    }

    internal class PlanViewModel
    {
        // The plan's heading item: the single requested item, or the FIRST
        // of a batch. A batch used to null the icon and rarity because "no
        // single target item exists", which cost the header both its art
        // and its rarity colour; naming the first item is what the title
        // already does, so these three now agree with it.
        public string TargetItemName { get; set; }

        public string TargetIconUrl { get; set; }

        // GW2 API rarity string; null/empty = unknown (neutral color/border).
        public string TargetRarity { get; set; }

        // The uncoloured remainder of a batch's heading (" + 2 others"),
        // null for a single-item plan. Its own field because only
        // TargetItemName carries the rarity colour - the count is about the
        // batch, not about that item, and colouring it would claim
        // otherwise.
        public string TargetNameSuffix { get; set; }

        // Every requested item AFTER the first, in request order, for the
        // header's stacked icon run. Null for a single-item plan; never
        // empty when non-null (a batch is 2+ items by definition).
        public List<PlanHeaderItem> AdditionalTargetItems { get; set; }

        public int TargetQuantity { get; set; }

        public List<PlanSectionViewModel> Sections { get; set; } = new List<PlanSectionViewModel>();

        public CraftingTreeNode TreeRoot { get; set; }

        // Populated INSTEAD of TreeRoot for a genuine multi-item batch (2+
        // requested items): one full CraftingTreeNode per requested item, in
        // request order, with no synthetic wrapper root - see
        // CraftingPlanResult.MultiItemRoots. Null for a single-item plan;
        // CraftingPlanView.RenderPlan branches on whichever is non-null.
        public List<CraftingTreeNode> MultiItemRoots { get; set; }

        // Passthrough of CraftingPlanResult.CurrencyMetadata (see that
        // field's doc comment) so the recipe-tree renderer can resolve a
        // node's VendorCurrencyCosts (raw CostLine ids) to display-ready
        // name/icon via CurrencyDisplayResolver at render time, the same
        // way BuildShoppingListSection already resolves it for shopping
        // rows. Null under the same conditions as the source field - the
        // resolver's own null-safe fallbacks handle that case.
        public IReadOnlyDictionary<int, CurrencyMetadata> CurrencyMetadata { get; set; }

        // Passthrough of CraftingPlanResult.ItemMetadata, on the same
        // precedent as CurrencyMetadata above: the recipe-tree renderer
        // resolves a Subdued pill's StrictDomination item-kind deltas (raw
        // item ids) to a display-ready name via
        // PlanViewModelBuilder.ResolveName at render time, keeping ids out
        // of the pure layers' output. Null under the same conditions as the
        // source field.
        public IReadOnlyDictionary<int, ItemMetadata> ItemMetadata { get; set; }

        // Passthrough of
        // CraftingPlanResult.PriceBasis so the recipe-tree renderer can word
        // a fallen-back node's unit-price tooltip caveat with the correct
        // side names ("buy-order price unavailable" vs. "instant-buy price
        // unavailable") instead of a basis-agnostic message.
        public PriceBasis PriceBasis { get; set; }

        // Plan-scope currency facts for
        // the Recipe Tree's per-leaf "HAVE {have}/{planTotal} TOTAL" pill
        // (DecisionPillPlanner.BuildPillSpecs/TreeSectionController.
        // RenderDecisionPills) - deliberately whole-PLAN totals, not any
        // per-row allocation, so the identical pill text is truthful at
        // every tree occurrence of the same currency id (see
        // DecisionPillPlanner's own doc comment on why no allocation is
        // computed). Both are plain passthroughs of already-computed plan
        // facts, keyed by currency id:
        //
        // CurrencyPlanTotals: CraftingPlanResult.Plan.CurrencyCosts (the
        // whole plan's real currency need), converted to a dictionary.
        // Null when the plan needs no currency at all.
        //
        // OwnedCurrencyAmounts: passthrough of CraftingPlanResult.
        // OwnedCurrencyAmounts (raw wallet holding, never clamped to need -
        // see that field's own doc comment). Null when no wallet snapshot
        // was available - distinct from "0 owned", and the tree renderer
        // must treat it that way (omit the pill entirely, not show HAVE 0).
        public IReadOnlyDictionary<int, long> CurrencyPlanTotals { get; set; }

        public IReadOnlyDictionary<int, int> OwnedCurrencyAmounts { get; set; }

        // Every item id the plan drew owned stock for, projected from
        // CraftingPlanResult.UsedMaterials (the reducer's own per-item
        // total). One item can sit at several places in one tree and the
        // reducer gives its stock to whichever node it reaches first, so
        // this plan-scope set is what lets the Recipe Tree draw the
        // "HAVE {used}/{total} NEEDED" badge on the later nodes too, at
        // "HAVE 0/{total}". Null when the plan drew no owned stock at all.
        public ISet<int> OwnedStockDrawnItemIds { get; set; }

        // currency-ux-package (Feature 3, KNOWN-ISSUES #21
        // resolution): passthrough of CraftingPlan.TimegatedItems
        // (informational-only vendor purchase caps - see that class's own
        // doc comment), re-indexed by ItemId so the Recipe Tree's
        // value-detail tooltip can look up a BuyFromVendor node's winning
        // offer cap in O(1) instead of scanning the list per row. Null
        // when the plan has no timegated items at all.
        public IReadOnlyDictionary<int, TimegatedItem> VendorCapsByItemId { get; set; }

        // The whole plan's NON-COIN price, beside the coin total the Total
        // Cost section's tiles show: one entry per wallet currency and per
        // barter item, display-ready, in the order that section's non-coin
        // table lists them - and projected from those very rows, so the
        // two cannot disagree. Reported BESIDE the coin figure, never
        // folded into it: the module holds no currency-to-gold rate and
        // must not invent one. Null (not empty) when the plan costs
        // nothing but coin - the common case, which must add no chrome.
        // Derivation: docs/ARCHITECTURE.md section 7.5.
        public IReadOnlyList<CurrencyAmountViewModel> NonCoinCostTotals { get; set; }
    }

    /// <summary>
    /// One item in the plan header's stacked batch run: what it takes to
    /// draw a framed icon and hover it. The same three display fields
    /// PlanViewModelBuilder resolves for every other row, plus the id the
    /// hover needs to reach this session's stat cache.
    /// </summary>
    internal class PlanHeaderItem
    {
        public int ItemId { get; set; }

        public string Name { get; set; }

        public string IconUrl { get; set; }

        // GW2 API rarity string; null/empty = unknown (neutral colour and
        // frame, never guessed).
        public string Rarity { get; set; }
    }

    /// <summary>
    /// A single non-coin amount, already resolved to display-ready
    /// name/icon (never a raw id - see CurrencyDisplayResolver). Used for
    /// BuyFromVendor rows/nodes priced wholly or partly in something other
    /// than coin - KNOWN-ISSUES #16.
    /// <para>
    /// Usually a wallet currency such as a spirit shard or karma. Not
    /// always: NonCoinCostTotals also projects barter-item rows into this
    /// type, where Amount is a count of items rather than of currency. Read
    /// the source list to know which, and never label one as the other.
    /// </para>
    /// </summary>
    internal class CurrencyAmountViewModel
    {
        public long Amount { get; set; }

        // The wallet currency this amount is of. Set by
        // CurrencyDisplayResolver alongside Name and IconUrl, so an icon
        // drawn from this view model can resolve its whole tooltip.
        public int CurrencyId { get; set; }

        public string Name { get; set; }

        public string IconUrl { get; set; }

        // Non-null only for a fractional-per-unit "Each" amount:
        // when a vendor offer's true per-unit rate does not divide
        // evenly (e.g. "2 for 3"), the renderer displays this literal
        // bundle text instead of Amount, rather than inventing a rounded
        // number. Null for every whole-number amount and for every Total
        // (non-"Each") amount - see CurrencyDisplayResolver.ResolveUnitAmounts.
        public string BundleLabel { get; set; }

        // The exact per-unit rate behind a "Each" amount, as a number
        // nothing renders: set by CurrencyDisplayResolver alongside every
        // per-unit amount (equal to Amount when the division was even,
        // the true fraction behind BundleLabel when it was not, e.g. 9.9
        // for "912 for 92"). Null for a Total (non-"Each") amount, where
        // Amount is already the exact figure. Exists because Amount is
        // deliberately 0 on a bundle-labelled row, which makes it useless
        // as a sort key - see PlanTableSorter.
        public double? UnitRate { get; set; }

        // Owned/needed split for a shopping-row currency Total amount
        // (gw2e parity - mirrors PlanRowViewModel.
        // CurrencyOwnedQuantity's doc comment): min(Amount, holding) of
        // what this row costs that the account already holds. Null (not 0)
        // when no account snapshot was available, or this is a per-unit
        // "Each" figure (ownership is a total-quantity concept - see
        // CurrencyDisplayResolver.ResolveAmounts/ResolveUnitAmounts). This
        // value is DELIBERATELY clamped so the HAVE/Amount pair the row
        // tooltip renders always reads as a coverage fraction (e.g.
        // "HAVE 500/500") rather than overshooting the total - see
        // RawOwnedQuantity below for the real, unclamped holding.
        public int? OwnedQuantity { get; set; }

        // Raw, UNCLAMPED holding backing OwnedQuantity above
        // (shoplist-have-format): unlike OwnedQuantity, this is never
        // capped at Amount, so it can exceed the row's Total when the
        // account holds more than this row needs. Null under the exact
        // same conditions as OwnedQuantity (no account snapshot /
        // per-unit "Each" figure). Tooltip-only - lets the
        // shopping row's tooltip spell out the real holding even when the
        // clamped OwnedQuantity/Amount pair alone would hide it.
        public int? RawOwnedQuantity { get; set; }
    }

    internal class PlanSectionViewModel
    {
        public PlanSectionType SectionType { get; set; }

        public string Title { get; set; }

        public List<PlanRowViewModel> Rows { get; set; } = new List<PlanRowViewModel>();

        public bool IsDefaultExpanded { get; set; }
    }

    internal class PlanRowViewModel
    {
        public PlanRowType RowType { get; set; }

        // The row's ITEM id, for the item stat tooltip only, and 0 on
        // every row whose numeric id is not an item id - which a
        // barter-item CurrencyCost row's is. PlanStep.ItemId is
        // one numeric slot shared by three id spaces (items, wallet
        // currencies, guild upgrades - see CraftingDecision), and id 24 is
        // BOTH a real item and the currency "Pristine Fractal Relics", so
        // an item-keyed stat lookup on a currency row would open the
        // tooltip with an unrelated ITEM's name, rarity and vendor value.
        // The gate lives in PlanViewModelBuilder, where the row's source
        // is known; the same collision TreeRowTooltipComposer.
        // RowIdIsAnItemId guards on the tree side.
        // Never displayed (repo invariant: ids are internal-only).
        public int ItemId { get; set; }

        public string Label { get; set; }

        public string Sublabel { get; set; }

        public string IconUrl { get; set; }

        // GW2 API rarity string; null/empty = unknown (neutral border).
        public string Rarity { get; set; }

        public int Quantity { get; set; }

        public long CoinValue { get; set; }

        // Per-unit price (CoinValue is the row's total for Quantity units).
        // Only populated for shopping rows, which show both a unit-price and
        // a total-price table column.
        public long UnitCoinValue { get; set; }

        // How many units UnitCoinValue actually buys, when it does not buy
        // one. Zero means UnitCoinValue is a true per-unit price and the
        // Each cell renders it alone.
        //
        // A vendor offer selling 2 for 5 copper has no per-unit coin price,
        // and a merged step whose occurrences paid different prices has no
        // single price either. Dividing produced a number that failed the
        // reader's own check, because Each times Amount did not reach
        // Total. The Each cell instead renders UnitCoinValue followed by
        // "for N", the same shape CurrencyAmountViewModel.BundleLabel
        // already uses for the currency half of the same cell.
        public int UnitCoinBundleQuantity { get; set; }

        public string StatusTag { get; set; }

        // What this row's hover adds in its second box, above the wiki
        // line: acquisition guidance on an unknown-source row, the crafted
        // item a recipe sheet unlocks on a RecipeRow. Deliberately separate
        // from Sublabel, which renders inline in the row itself - HintText
        // never renders inline.
        public string HintText { get; set; }

        // The wiki page this row's icon and its row-level right-click
        // both open, and the affordance line its hover ends with - one
        // value so the two can never disagree (see IconWikiTarget).
        // Currently named only for RecipeRow rows (RequiredRecipes - see
        // PlanViewModelBuilder.BuildRecipesSection); every other row type
        // leaves it at the default, which opens nothing.
        public IconWikiTarget WikiTarget { get; set; }

        // Short pill/tag label (e.g. "SALVAGE", "EXPLORE") for
        // ShoppingUnknown rows, from the same seeded hint entry as
        // HintText. Null when the hint has no badge (or no hint at all) -
        // the view falls back to "UNKNOWN".
        public string BadgeText { get; set; }

        // Non-coin currency cost(s) of this row (ShoppingVendor rows only -
        // see PlanStep.VendorCurrencyCosts), already resolved to
        // display-ready name/icon. Total-quantity amounts, mirroring
        // CoinValue. Null/empty when this row has no non-coin currency
        // cost - KNOWN-ISSUES #16.
        public List<CurrencyAmountViewModel> CurrencyCosts { get; set; }

        // Per-unit ("Each" column) counterpart of CurrencyCosts - integer-
        // divided by Quantity the same way UnitCoinValue divides CoinValue.
        // Null/empty under the same condition as CurrencyCosts.
        public List<CurrencyAmountViewModel> UnitCurrencyCosts { get; set; }

        // Owned split for a CurrencyCost row (gw2e parity): the account's
        // holding of what the row costs - a wallet balance for a currency
        // (AccountCurrencyIndex), an account-wide item count for a barter
        // item (AccountItemIndex). Null (not 0) when no account snapshot
        // was available at all, distinct from "0 owned" - only ever set on
        // CurrencyCost rows. User-mandated: this is the RAW, UNCLAMPED
        // holding (not min(Quantity, holding)) - the currency table's
        // "Have" column shows the real holding even when it exceeds what
        // the plan needs, rather than silently capping it at Quantity.
        // CurrencyNeededQuantity below is the (still-clamped-to-zero) gap
        // derived from this value.
        public int? CurrencyOwnedQuantity { get; set; }

        // Still-to-acquire gap for a CurrencyCost row in the
        // currency table's "Needed" column - max(0, Quantity -
        // CurrencyOwnedQuantity). Null (not 0) whenever CurrencyOwnedQuantity
        // is null (no account snapshot) - mirrors that field's own null
        // contract, never a fabricated gap. Only ever set on CurrencyCost
        // rows.
        public int? CurrencyNeededQuantity { get; set; }

        // True when CurrencyOwnedQuantity is present AND covers the
        // full Required amount (CurrencyOwnedQuantity >= Quantity) - drives
        // the currency table's green full-coverage marker. Always false
        // when no account snapshot exists (CurrencyOwnedQuantity null) -
        // "unknown" must never render as "covered". Set on both kinds of
        // CurrencyCost row: a barter item the account already holds in
        // full is as covered as a wallet currency it holds in full.
        public bool CurrencyFullyCovered { get; set; }

        // True on the CurrencyCost rows that are a BARTER ITEM rather than
        // a wallet currency - an untradeable vendor token whose units are
        // the price (CraftingPlan.BarterItemCosts). ItemId is then a real
        // item id and Rarity is populated, so the renderer frames and
        // hovers the row as an ITEM. Only CurrencyDescription is null on
        // one: /v2/currencies knows nothing about an item, while the
        // account snapshot's item index does, so the owned split above is
        // populated for both kinds. False on every other row.
        public bool IsBarterItemCost { get; set; }

        // The sub-currency behind a coalesced trade-up item row: the
        // holding the account has of it, and how many of THIS row's item
        // that holding buys. Both are set together, only on an
        // IsBarterItemCost row whose plan cost carried a
        // BarterItemCost.TradeUpCurrencyId, and only when the wallet
        // snapshot answered for that currency at all. TradeUpBuysQuantity
        // is capped at what the row still needs, so it never claims a
        // holding covers more than the plan asks for. Null/0 everywhere
        // else, which is what suppresses the table's Note column.
        public int TradeUpCurrencyId { get; set; }

        public string TradeUpCurrencyName { get; set; }

        public int? TradeUpCurrencyHeld { get; set; }

        public int TradeUpBuysQuantity { get; set; }

        // The wallet currency this row is about, on a CurrencyCost row
        // that is not a barter item. Zero elsewhere. The row's icon
        // resolves its whole tooltip from this id, so the table and the
        // Recipe Tree cannot show different boxes for the same currency.
        public int CurrencyId { get; set; }

        // Identity of one CurrencyCost row, stable across a re-solve: the
        // row's id with the id space it came from written into the string.
        // The id space has to be part of it, because a wallet currency id
        // and an item id are different spaces over the same numbers - id
        // 24 is both a real item and the currency "Pristine Fractal
        // Relics". Null on every other row type. Never displayed (repo
        // invariant: ids are internal-only); the scroll anchor registry
        // keys the row's control by it, so a rebuilt table's row takes
        // over from the disposed one it stands in for.
        public string NonCoinCostKey { get; set; }

        // User-mandated mouseover
        // tooltips: the exact-meaning tooltip text for a CostFormulaTile/
        // ProfitFormulaTile row's header caption. Set directly on the
        // caption Label control itself, never on the tile's containing
        // Panel (a label captures the mouse before a container
        // tooltip underneath it would ever be reached - see
        // SummarySectionRenderer.CreateFormulaBand). Null/unused for every
        // other row type.
        public string TooltipText { get; set; }

        // true (default) when the formula band's
        // "=" operator drawn immediately to this tile's left is an honest
        // statement (left tile - middle tile literally equals this tile's
        // displayed CoinValue). SummarySectionRenderer.CreateFormulaBand
        // only ever draws "=" before the LAST tile in a band, and only
        // reads this field on that tile - it being false on every other
        // tile is harmless (unread there). The one case this is ever
        // false: the profit band's loss tile, whose Label/CoinValue
        // deliberately show "Loss if Sold" / Math.Abs(profit) (the
        // pre-existing sign convention - PlanViewModelBuilder.
        // BuildProfitFormulaBand). With that convention the drawn
        // equation ("Sell Value - Total Materials Value = <abs loss>")
        // is arithmetically FALSE for a negative profit, since the true
        // right-hand side is negative. When false, the renderer
        // substitutes a neutral, non-equality punctuation mark for that
        // one boundary instead of "=".
        public bool FormulaResultIsExact { get; set; } = true;

        // Per-character discipline display (gw2efficiency parity):
        // which of the account's characters have this DisciplineRow's
        // discipline, and at what rating - e.g. "Anna (500), Bob
        // (400/450)" (the "/450" suffix marks a character below the row's
        // required MinRating - see PlanViewModelBuilder.
        // BuildCharacterAvailabilityText). "Not trained on any character"
        // when the snapshot has discipline data but no character has it.
        // Null (not empty) when the snapshot has no character-crafting
        // data at all (old snapshot / degraded fetch) - the renderer must
        // show nothing extra for that case, never a fabricated claim
        // either way. Only ever set on DisciplineRow rows.
        public string CharacterAvailabilityText { get; set; }
    }
}
