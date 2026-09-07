using System;
using System.Collections.Generic;
using System.Globalization;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The Summary (Total Cost) section's own pure layout arithmetic
    /// (Blish-free, unit-testable) - total content height and the currency
    /// table's column edges.
    ///
    /// Views/CraftingPlanView.cs's one call site (CreateCollapsibleSection)
    /// special-cases PlanSectionType.Summary to call BodyHeight below instead
    /// of PlanContentHeightMath.SectionBodyHeight; every other section type
    /// still routes through that method unchanged.
    ///
    /// The row-height CONSTANTS are not redefined here - every formula below
    /// reads PlanContentHeightMath's existing public CostTileRowHeight/
    /// ColumnHeaderRowHeight/CurrencyRowHeight/FallbackTextRowHeight directly,
    /// so this class can never drift from the fixed-row-height convention.
    /// Only the Summary-specific COUNTING logic lives here.
    ///
    /// Why it is a separate class: docs/ARCHITECTURE.md, "Services Q-Z:
    /// relocated design narrative"; KNOWN-ISSUES #46 for the original
    /// rationale.
    /// </summary>
    internal static class SummarySectionLayoutMath
    {
        /// <summary>
        /// Total height of the Total Cost section's content FlowPanel.
        /// CraftingPlanView.CreateCollapsibleSection assigns this to
        /// contentFlow.Height synchronously right after
        /// SummarySectionRenderer.Render populates it (see
        /// PlanContentHeightMath's class doc for why that matters), so it must
        /// agree exactly with what that renderer builds:
        ///   - one CostBandHeight-tall row for the cost formula band (always
        ///     present - 1 or 3 CostFormulaTile rows both render as ONE tile
        ///     row, per the collapse rule), taller again when the plan carries
        ///     currency costs the coin figure cannot speak for;
        ///   - at most one CostTileRowHeight-tall row for the profit formula
        ///     band, present only when ProfitFormulaTile rows exist (always 3);
        ///   - one CurrencyTableTopGap spacer plus one ColumnHeaderRowHeight
        ///     header plus NonCoinTableRowsHeight for the grouped table below
        ///     it, only when at least one CurrencyCost row exists;
        ///   - one FallbackTextRowHeight row per MultiItemNote row, and one for
        ///     the SummaryFootnote row - summed rather than assumed so an
        ///     absent footnote degrades gracefully instead of desyncing.
        /// </summary>
        public static int BodyHeight(IReadOnlyList<PlanRowViewModel> rows)
        {
            rows = rows ?? Array.Empty<PlanRowViewModel>();

            bool hasCostBand = false;
            bool hasProfitBand = false;
            int noteRowCount = 0;
            int footnoteRowCount = 0;

            // The same grouping the renderer draws from, over the same
            // rows - the table's height is a property of its groups now,
            // not of a row count.
            var groups = GroupNonCoinRows(rows);

            foreach (var row in rows)
            {
                switch (row.RowType)
                {
                    case PlanRowType.CostFormulaTile:
                        hasCostBand = true;
                        break;
                    case PlanRowType.ProfitFormulaTile:
                        hasProfitBand = true;
                        break;
                    case PlanRowType.MultiItemNote:
                        noteRowCount++;
                        break;
                    case PlanRowType.SummaryFootnote:
                        footnoteRowCount++;
                        break;
                }
            }

            int height = 0;
            if (hasCostBand)
            {
                height += CostBandHeight(groups.Count > 0);
            }

            if (hasProfitBand)
            {
                height += PlanContentHeightMath.CostTileRowHeight;
            }

            if (groups.Count > 0)
            {
                height += CurrencyTableTopGap
                    + PlanContentHeightMath.ColumnHeaderRowHeight
                    + NonCoinTableRowsHeight(groups);
            }

            height += noteRowCount * PlanContentHeightMath.FallbackTextRowHeight;
            height += footnoteRowCount * PlanContentHeightMath.FallbackTextRowHeight;
            return height;
        }

        // --- Cost formula band (the plan's headline figure) ---
        //
        // The cost band's result tile is the one number a user comes to
        // this section for. It used to say so with a promoted DefaultFont32
        // amount; that was replaced with a tinted,
        // semi-transparent highlight box around the result tile, so all
        // three tiles now share ONE amount font and the band reads as one
        // formula again. The band's height is therefore no longer a
        // promoted font's leading - it is the box's own padding around a
        // caption line, an optional disclosure line and one ordinary
        // amount run, which is what the constants below spell out.
        //
        // In a currency-bearing plan that gold figure is not the whole
        // cost: PlanViewModelBuilder.BuildCostFormulaBand sources it from
        // Plan.TotalCoinCost, which by construction excludes every
        // CurrencyCost row the table below lists. The band therefore
        // carries an extra disclosure line under the result caption, and
        // the band grows by exactly one line's worth to hold it. Both the
        // renderer's row height and BodyHeight's count come from
        // CostBandHeight so they cannot disagree about that growth.

        /// <summary>Gap between the band's own edge and the highlight box.</summary>
        public const int CostBandBoxMarginY = 6;

        /// <summary>Highlight box padding above the caption / below the amount.</summary>
        public const int CostBandBoxPadY = 6;

        /// <summary>Highlight box padding left and right of its widest line.</summary>
        public const int CostBandBoxPadX = 14;

        /// <summary>
        /// Caption y inside the cost band - the box's top edge plus its own
        /// padding, so the box never starts above the band.
        /// </summary>
        public const int CostBandCaptionY = CostBandBoxMarginY + CostBandBoxPadY;

        /// <summary>
        /// Bottom pad under the cost band's amount run, symmetric with
        /// <see cref="CostBandCaptionY"/>: the box's padding plus its margin.
        /// </summary>
        public const int CostBandAmountBottomPad = CostBandBoxPadY + CostBandBoxMarginY;

        /// <summary>
        /// Height reserved for one caption line. Aliased, not duplicated:
        /// both bands reserve the same line and PlanContentHeightMath owns
        /// the number, beside the row height it is a term of.
        /// </summary>
        public const int CostBandCaptionLineHeight = PlanContentHeightMath.CostTileCaptionLineHeight;

        /// <summary>
        /// Gap between the caption line and the amount run under it -
        /// aliased from PlanContentHeightMath.CostTileLabelToValueGap, the
        /// ONE label-to-value distance both bands use, so the cost band and
        /// the profit band cannot drift apart.
        /// </summary>
        public const int CostBandCaptionToAmountGap = PlanContentHeightMath.CostTileLabelToValueGap;

        /// <summary>
        /// Gap between the result tile's amount run and the disclosure line
        /// hanging under it. Half <see cref="CostBandCaptionToAmountGap"/>,
        /// on the same 4pt scale and deliberately tighter: the disclosure
        /// is a footnote ON that amount, so it has to bind to the number
        /// above it more closely than the number binds to its own caption.
        /// </summary>
        public const int CostBandAmountToNoteGap = 4;

        /// <summary>
        /// Extra band height the disclosure line costs, now that it hangs
        /// BELOW the amount rather than sitting between the caption and it
        /// (see <see cref="CurrencyRequirementNote"/>): the gap above it
        /// plus the Caption tier's lowest ink (TypeRampMetrics.CaptionInk,
        /// 19 - one past its own 18px line box, so a descender on this line
        /// still lands inside the band).
        /// </summary>
        public const int CostBandCurrencyNoteHeight = 23;

        /// <summary>
        /// Height of the cost formula band's single tile row: the highlight
        /// box's margin+padding, a caption line, the gap, one amount run
        /// (PlanContentHeightMath.AmountRunHeight - the taller of the amount
        /// text's line box and the coin icon beside it, which at the wallet
        /// BAR tier is the text, not the icon),
        /// and the disclosure line hanging under the amount when there is
        /// one. That line used to be counted BETWEEN the caption and a
        /// bottom-anchored amount, which dropped all three tiles' coin runs
        /// by its height while only the result tile had anything in the
        /// space it left - a dead band under the other two tiles.
        /// hasCurrencyNote must be "this Summary section has at least one
        /// CurrencyCost row" - the same condition
        /// Views/Rendering/SummarySectionRenderer.Render uses to decide
        /// whether to draw the disclosure line at all.
        /// </summary>
        public static int CostBandHeight(bool hasCurrencyNote)
        {
            return CostBandCaptionY
                + CostBandCaptionLineHeight
                + CostBandCaptionToAmountGap
                + PlanContentHeightMath.AmountRunHeight
                + (hasCurrencyNote ? CostBandCurrencyNoteHeight : 0)
                + CostBandAmountBottomPad;
        }

        /// <summary>
        /// Top y of an amount run: one
        /// <see cref="CostBandCaptionToAmountGap"/> under the bottom of the
        /// caption block above it, in EVERY band. captionBlockBottom is
        /// measured from whatever font Blish loaded while the band height
        /// is a constant, so a font taller than
        /// PlanContentHeightMath.CostTileCaptionLineHeight pushes the
        /// amount out of the band (loud - the renderer's DEBUG assert
        /// catches it) rather than silently overprinting the caption.
        /// </summary>
        public static int BandAmountY(int captionBlockBottom)
        {
            return captionBlockBottom + CostBandCaptionToAmountGap;
        }

        /// <summary>
        /// Top y of the result tile's disclosure line: one
        /// <see cref="CostBandAmountToNoteGap"/> under the amount run it
        /// footnotes. Only the result tile of a highlighted band has one -
        /// every other tile's content ends at the amount, which is what
        /// keeps all three coin runs on one line.
        /// </summary>
        public static int BandNoteY(int amountY, int amountHeight)
        {
            return amountY + amountHeight + CostBandAmountToNoteGap;
        }

        /// <summary>
        /// Band-space top edge of the highlight box: one pad above the
        /// caption, which by <see cref="CostBandCaptionY"/>'s construction
        /// is exactly one margin below the band's own top edge.
        /// </summary>
        public const int CostBandBoxTop = CostBandCaptionY - CostBandBoxPadY;

        /// <summary>
        /// Height of the highlight box around a result tile whose lowest
        /// content ends at band-space contentBottom: from
        /// <see cref="CostBandBoxTop"/> down to one pad below it. The box
        /// is the band's lowest ink, so this - not the amount run - is what
        /// has to fit inside <see cref="CostBandHeight"/>. contentBottom is
        /// the DISCLOSURE line's bottom on a tile that has one
        /// (<see cref="BandNoteY"/>) and the amount's otherwise: Blish
        /// clips a container's children, so a box measured off the amount
        /// alone would crop its own footnote.
        /// </summary>
        public static int CostBandBoxHeight(int contentBottom)
        {
            return contentBottom + CostBandBoxPadY - CostBandBoxTop;
        }

        /// <summary>
        /// Width of the highlight box around its widest measured line
        /// (caption, disclosure line or coin run). Never clamped to the
        /// tile slice: Blish clips a container's children, so a box
        /// narrower than its content would cut the amount off where an
        /// unboxed tile merely overlaps its neighbour.
        /// </summary>
        public static int CostBandBoxWidth(int widestContentWidth)
        {
            return widestContentWidth + 2 * CostBandBoxPadX;
        }

        /// <summary>
        /// The disclosure line's text, or null when the plan has no
        /// non-coin costs (in which case the coin figure genuinely IS the
        /// whole cost and no line is drawn). Pure copy rather than
        /// geometry, kept beside CostBandHeight because the two are one
        /// decision - the same precedent
        /// RequiredRecipesVisibility.BuildHeaderTitle already set for
        /// honest, count-derived header copy living in Services.
        /// <para>
        /// The table below carries two row kinds (wallet currencies and
        /// barter items - see PlanRowViewModel.IsBarterItemCost), and this
        /// line names whichever kinds are actually in it rather than
        /// calling an untradeable token a currency.
        /// </para>
        /// </summary>
        public static string CurrencyRequirementNote(IReadOnlyList<PlanRowViewModel> nonCoinRows)
        {
            int currencies = 0;
            int items = 0;
            CountNonCoinRowKinds(nonCoinRows, ref currencies, ref items);
            int total = currencies + items;
            if (total <= 0)
            {
                return null;
            }

            // Deliberately short: it sits under a caption inside one tile
            // slice of a three-tile band, and the reason WHY it matters
            // lives in the hover text rather than widening this line past
            // its tile.
            if (items == 0)
            {
                return currencies == 1
                    ? "+ 1 Currency Required"
                    : $"+ {currencies} Currencies Required";
            }

            if (currencies == 0)
            {
                return items == 1
                    ? "+ 1 Item Required"
                    : $"+ {items} Items Required";
            }

            return $"+ {total} Currencies and Items Required";
        }

        /// <summary>
        /// The non-coin table's name-column header: "Currency" while every
        /// row is a wallet currency, widened only when a barter item row is
        /// actually present, so the common plan's header does not offer a
        /// kind its table does not contain.
        /// </summary>
        public static string NonCoinNameHeader(IReadOnlyList<PlanRowViewModel> nonCoinRows)
        {
            int currencies = 0;
            int items = 0;
            CountNonCoinRowKinds(nonCoinRows, ref currencies, ref items);
            if (items == 0)
            {
                return "Currency";
            }

            return currencies == 0 ? "Item" : "Currency or Item";
        }

        private static void CountNonCoinRowKinds(
            IReadOnlyList<PlanRowViewModel> nonCoinRows, ref int currencies, ref int items)
        {
            if (nonCoinRows == null)
            {
                return;
            }

            for (int i = 0; i < nonCoinRows.Count; i++)
            {
                var row = nonCoinRows[i];
                if (row == null)
                {
                    continue;
                }

                if (row.IsBarterItemCost)
                {
                    items++;
                }
                else
                {
                    currencies++;
                }
            }
        }

        /// <summary>
        /// The non-coin table's group headings. A wallet currency is
        /// checked in the wallet and a barter item in inventory, so one
        /// alphabetical list of both sends the eye to the wrong screen for
        /// half of its rows; the headings name the place, because where the
        /// reader goes to check a row is what separates the two kinds.
        /// </summary>
        public const string WalletGroupHeading = "From your wallet";

        /// <summary>Inventory twin of <see cref="WalletGroupHeading"/>.</summary>
        public const string InventoryGroupHeading = "From your inventory";

        /// <summary>
        /// The Note column's left half: how much of the sub-currency the
        /// account holds. The currency's own icon follows it, so nothing
        /// here names the currency - the icon's hover does. Null on a row
        /// with no note, which is what keeps the column absent.
        /// </summary>
        public static string TradeUpNoteHeldText(PlanRowViewModel row)
        {
            return row != null && row.TradeUpCurrencyHeld.HasValue
                ? row.TradeUpCurrencyHeld.Value.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        /// <summary>
        /// The Note column's right half: how many of the row's own item
        /// that holding buys. Capped by the builder at what the row still
        /// needs, so it never claims to cover more than the plan asks for.
        /// Null under the same condition as
        /// <see cref="TradeUpNoteHeldText"/>.
        /// </summary>
        public static string TradeUpNoteBuysText(PlanRowViewModel row)
        {
            return row != null && row.TradeUpCurrencyHeld.HasValue
                ? "Buys " + row.TradeUpBuysQuantity.ToString(CultureInfo.InvariantCulture)
                : null;
        }

        /// <summary>
        /// Width the Note column occupies: the held amount, the
        /// sub-currency's icon seated after it the way every inline
        /// currency run in the module seats one, then the "Buys N" half.
        /// <para>
        /// Both widths are the widest the TABLE measures, not one row's,
        /// because every row seats its icon at the same x
        /// (<see cref="TradeUpNoteIconX"/>). They are Blish measurements
        /// and arrive from the caller.
        /// </para>
        /// </summary>
        public static int TradeUpNoteWidth(int heldBandWidth, int buysBandWidth)
        {
            if (heldBandWidth <= 0 && buysBandWidth <= 0)
            {
                return 0;
            }

            return heldBandWidth + CoinSegmentMath.CoinLabelIconGap + CoinSegmentMath.CoinIconSize +
                CoinSegmentMath.CoinSegmentGap + buysBandWidth;
        }

        /// <summary>
        /// X the held amount draws at: right-aligned in the held band, so a
        /// three-digit holding and a four-digit one both end where the icon
        /// begins. A number wider than the band - which only happens if the
        /// caller's band is not the table's own widest - keeps the note's
        /// left rule and pushes its own icon right, so it is never
        /// truncated and only that row leaves the shared icon x.
        /// </summary>
        public static int TradeUpNoteHeldX(int noteX, int heldBandWidth, int heldTextWidth)
        {
            int slack = heldBandWidth - heldTextWidth;
            return slack > 0 ? noteX + slack : noteX;
        }

        /// <summary>X the note's icon starts at: past the held band, so it
        /// is the same x on every row of the table.</summary>
        public static int TradeUpNoteIconX(int noteX, int heldBandWidth, int heldTextWidth)
        {
            int band = heldTextWidth > heldBandWidth ? heldTextWidth : heldBandWidth;
            return noteX + band + CoinSegmentMath.CoinLabelIconGap;
        }

        /// <summary>X the note's "Buys N" half starts at.</summary>
        public static int TradeUpNoteBuysX(int noteX, int heldBandWidth, int heldTextWidth)
        {
            return TradeUpNoteIconX(noteX, heldBandWidth, heldTextWidth) +
                CoinSegmentMath.CoinIconSize + CoinSegmentMath.CoinSegmentGap;
        }

        /// <summary>True when any row in the table carries a note.</summary>
        public static bool AnyTradeUpNote(IReadOnlyList<PlanRowViewModel> nonCoinRows)
        {
            if (nonCoinRows == null)
            {
                return false;
            }

            foreach (var row in nonCoinRows)
            {
                if (row != null && row.TradeUpCurrencyHeld.HasValue)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One group of the non-coin table: its heading and its rows.
        /// </summary>
        public readonly struct NonCoinRowGroup
        {
            public readonly string Heading;
            public readonly IReadOnlyList<PlanRowViewModel> Rows;

            /// <summary>
            /// True for the barter-item group, false for the wallet one.
            /// Carried rather than re-derived from the heading string,
            /// which is display text.
            /// </summary>
            public readonly bool IsInventoryGroup;

            internal NonCoinRowGroup(
                string heading, IReadOnlyList<PlanRowViewModel> rows, bool isInventoryGroup)
            {
                Heading = heading;
                Rows = rows;
                IsInventoryGroup = isInventoryGroup;
            }
        }

        /// <summary>
        /// The non-coin cost rows split into their groups, in draw order:
        /// wallet currencies, then barter items. Relative order inside a
        /// group is the caller's, so rows handed in alphabetically stay
        /// alphabetical within their own group.
        /// <para>
        /// An empty group produces no entry, so a plan that spends only
        /// currency draws one heading and no empty second one. Anything
        /// that is not a <see cref="PlanRowType.CurrencyCost"/> row is
        /// ignored, which is what lets BodyHeight hand this a whole
        /// section's rows and the renderer hand it just the table's.
        /// </para>
        /// </summary>
        public static IReadOnlyList<NonCoinRowGroup> GroupNonCoinRows(
            IReadOnlyList<PlanRowViewModel> rows)
        {
            List<PlanRowViewModel> wallet = null;
            List<PlanRowViewModel> inventory = null;
            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || row.RowType != PlanRowType.CurrencyCost)
                {
                    continue;
                }

                if (row.IsBarterItemCost)
                {
                    if (inventory == null)
                    {
                        inventory = new List<PlanRowViewModel>();
                    }

                    inventory.Add(row);
                }
                else
                {
                    if (wallet == null)
                    {
                        wallet = new List<PlanRowViewModel>();
                    }

                    wallet.Add(row);
                }
            }

            var groups = new List<NonCoinRowGroup>(2);
            if (wallet != null)
            {
                groups.Add(new NonCoinRowGroup(WalletGroupHeading, wallet, isInventoryGroup: false));
            }

            if (inventory != null)
            {
                groups.Add(new NonCoinRowGroup(InventoryGroupHeading, inventory, isInventoryGroup: true));
            }

            return groups;
        }

        /// <summary>
        /// Identity of one wallet currency's cost row, for
        /// <see cref="PlanRowViewModel.NonCoinCostKey"/>. The "currency:"
        /// half is not decoration: a wallet currency id and an item id are
        /// different id spaces over the same numbers, so the bare number
        /// would collide with the barter row for the same number.
        /// </summary>
        public static string WalletCurrencyCostKey(int currencyId)
        {
            return "currency:" + currencyId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Identity of one barter item's cost row. Twin of
        /// <see cref="WalletCurrencyCostKey"/>; see it for why the id
        /// space is spelled into the key.
        /// </summary>
        public static string BarterItemCostKey(int itemId)
        {
            return "item:" + itemId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Scroll anchor key of one non-coin cost row, or null when the
        /// row carries no identity to anchor by. The id underneath is the
        /// currency or item id, which the solver does not renumber, so the
        /// key survives a re-solve that adds or drops rows around it -
        /// that is what lets a restore put this row back on the line it
        /// was on. Services/ScrollAnchorMath owns what a key is used for.
        /// </summary>
        public static string NonCoinRowAnchorKey(PlanRowViewModel row)
        {
            if (row == null || string.IsNullOrEmpty(row.NonCoinCostKey))
            {
                return null;
            }

            return "cost:" + row.NonCoinCostKey;
        }

        /// <summary>
        /// Scroll anchor key of one group heading row. There are exactly
        /// two headings and neither depends on the plan, so the group's
        /// kind is the whole key.
        /// </summary>
        public static string NonCoinGroupAnchorKey(bool isInventoryGroup)
        {
            return isInventoryGroup ? "costgroup:inventory" : "costgroup:wallet";
        }

        /// <summary>
        /// A group heading is one line drawn in the same face as the column
        /// headers above it, so it takes the same row height and the same
        /// label y they do - aliased rather than restated, for the reason
        /// this class's own doc comment gives. That pair is the one already
        /// derived to clear this face's descenders inside the row, so a
        /// tier-seat swap moves the heading with the band it matches.
        /// </summary>
        public const int NonCoinGroupHeadingHeight = PlanContentHeightMath.ColumnHeaderRowHeight;

        /// <summary>
        /// Line-box y of a group heading's label. See
        /// <see cref="NonCoinGroupHeadingHeight"/> for why it is the column
        /// header's own, not a centring of the line box in the row.
        /// </summary>
        public const int NonCoinGroupHeadingTextY = PlanContentHeightMath.ColumnHeaderLabelY;

        /// <summary>
        /// Height of everything the non-coin table draws BELOW its
        /// column-header band: one heading row per group plus one row per
        /// cost row. BodyHeight and the renderer both size the table from
        /// this one function over the same groups, so neither can price a
        /// heading differently from the other.
        /// </summary>
        public static int NonCoinTableRowsHeight(IReadOnlyList<NonCoinRowGroup> groups)
        {
            if (groups == null)
            {
                return 0;
            }

            int height = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                height += NonCoinGroupHeadingHeight
                    + groups[i].Rows.Count * PlanContentHeightMath.CurrencyRowHeight;
            }

            return height;
        }

        /// <summary>
        /// Hover text for the disclosure line: the names themselves -
        /// currencies and barter items alike - in the order the table below
        /// lists them. Null when there is nothing to disclose. Never shows
        /// IDs (repo invariant: IDs are internal-only).
        /// </summary>
        public static string CurrencyRequirementNoteTooltip(IReadOnlyList<PlanRowViewModel> currencyRows)
        {
            if (currencyRows == null || currencyRows.Count == 0)
            {
                return null;
            }

            var names = new List<string>(currencyRows.Count);
            foreach (var row in currencyRows)
            {
                if (!string.IsNullOrEmpty(row.Label))
                {
                    names.Add(row.Label);
                }
            }

            if (names.Count == 0)
            {
                return null;
            }

            return string.Join(", ", names)
                + "\nThese are spent on top of the coin cost shown - see the table below.";
        }

        // --- Currency table column geometry ---
        //
        // Every column reserves what its own widest cell needs, and the
        // space left over is split into EQUAL GAPS between them. The table
        // used to divide the row into four equal tracks instead, which at
        // the 1310px plan panel gave each number column a 262px track for
        // an 88px band ("Required", measured at the 20-bold header face)
        // while the Note run got 112px and 14px of clearance before
        // Status. Equal gaps spend the row on the columns that hold
        // something instead.
        //
        // Numbers right-align INSIDE their band - that is what keeps digits
        // aligned down a column. The name is the one column that flexes,
        // and it may run past its reserve into the gap after it, bounded by
        // PlanRelayoutMath.NameMaxWidthBeforeColumn.
        //
        // Required/Have/Needed columns reserve CurrencyNumberColumnWidth by
        // default, widened per-render when an actual value needs more room
        // (EffectiveCurrencyNumberColumnWidth/ComputeCurrencyColumnEdges'
        // widestNumberWidth parameter below) - mirrors ShoppingColumnMath.
        // ComputeEdges' "clamp to a fixed minimum, widen from an actual
        // per-render widest-value measurement" shape.
        //
        // The widening matters: the Have column is unclamped to the real
        // wallet holding (PlanViewModelBuilder.BuildCurrencyTableRows), and
        // Karma (Gw2Constants id 2) routinely reaches 6-7 digits in a real
        // player's wallet, which can plausibly exceed the 60px floor. Since
        // CreateRightAlignedLabel grows a label LEFTWARD from the column's
        // own right edge, an unreserved overlong value would visually
        // intrude into its left neighbor's column rather than clip. That
        // reserve is also what decides whether the row is wide enough to
        // lay the columns out with equal gaps at all - see
        // EdgesFromRightEdge.

        /// <summary>
        /// Open space between the last formula band and the currency
        /// table's header band, which is a filled dark rectangle and
        /// without this reads as part of the band above it.
        /// 12: one 4pt step BELOW the 16px CraftingPlanView.SectionSpacing
        /// puts between two whole sections, because this is a boundary
        /// INSIDE one and has to separate less than a boundary between two.
        /// </summary>
        public const int CurrencyTableTopGap = 12;

        /// <summary>Left x of the currency icon.</summary>
        public const int CurrencyIconX = 8;

        /// <summary>
        /// Icon size for the currency table's leading icon. This IS the
        /// in-game wallet list - one row per currency, name then amounts,
        /// icon as the row's subject - so it takes the list tier
        /// (<see cref="CurrencyIconTiers.WalletListIconSize"/>), not the bar
        /// tier the inline coin runs above it use. The renderer insets the
        /// art by the module's 1px frame either side, so the framed box
        /// occupies exactly the measured 32px window.
        /// </summary>
        public const int CurrencyIconSize = CurrencyIconTiers.WalletListIconSize;

        /// <summary>
        /// Left x of the currency name label - past the icon plus a gap.
        /// </summary>
        public const int CurrencyNameX = CurrencyIconX + CurrencyIconSize + 8;

        /// <summary>
        /// Baseline-box y of the currency row's name and its three numbers:
        /// their Body line box centred in the row, the same rule the row's
        /// own icon and coverage marker already centre by.
        /// <para>
        /// Was a hard-coded 4, which centred a 20px line box only in the
        /// 28px row this table drew before its icon took the wallet-LIST
        /// tier and the row became 42 (PlanContentHeightMath.
        /// CurrencyRowHeight). The literal agreed with the row by
        /// coincidence, and when the coincidence lapsed the text sat 7px
        /// high beside a centred icon. Derived, it cannot lapse again.
        /// </para>
        /// </summary>
        public static int CurrencyRowTextY =>
            (PlanContentHeightMath.CurrencyRowHeight - TypeRampMetrics.BodyInk.LineHeight) / 2;

        /// <summary>Gap reserved before/between the right-side columns.</summary>
        public const int CurrencyColumnGap = 14;

        /// <summary>Reserved band width for each of Required/Have/Needed.</summary>
        public const int CurrencyNumberColumnWidth = 60;

        /// <summary>
        /// Floor for the full-coverage marker column's band. Widened per
        /// render to whichever is larger, the marker pill or the "Status"
        /// header over it (<see cref="EffectiveCurrencyMarkerWidth"/>) -
        /// both are Blish measurements, so both arrive from the caller.
        /// </summary>
        public const int CurrencyMarkerWidth = 34;

        /// <summary>
        /// Room the name column reserves before the gaps are shared out.
        /// MEASURED: "Clot of Congealed Screams", one of the longer names
        /// these two groups draw, is 209px in the 16-regular body face. A
        /// longer name is not truncated to this: the name flexes into the
        /// gap after the column, up to
        /// PlanRelayoutMath.NameMaxWidthBeforeColumn.
        /// </summary>
        public const int CurrencyNameColumnWidth = 210;

        /// <summary>Number columns the table draws: Required, Have, Needed.</summary>
        public const int CurrencyNumberColumnCount = 3;

        public readonly struct CurrencyColumnEdges
        {
            public readonly int RequiredRightEdge;
            public readonly int HaveRightEdge;
            public readonly int NeededRightEdge;
            public readonly int MarkerX;

            /// <summary>
            /// Band the marker column reserves, from <see cref="MarkerX"/>
            /// to the table's pinned right edge. The pills inside it are
            /// LEFT-ruled on MarkerX, so it is neither the extent the
            /// "Status" header centres over nor what bounds it - see
            /// <see cref="CurrencyHeaderRoomsFor"/>.
            /// </summary>
            public readonly int MarkerWidth;

            /// <summary>
            /// The band all three number columns reserve. NOT the extent a
            /// header centres over (that is the column's own widest
            /// number) and NOT what bounds one either (that is the gap to
            /// the neighbouring column - <see cref="CurrencyHeaderRoomsFor"/>).
            /// One band for three columns is why: it is floored at the
            /// widest of the three header labels
            /// (SummarySectionRenderer.WidestCurrencyHeaderLabel) and at
            /// the widest number in ANY of them, so it routinely exceeds
            /// what a given column draws.
            /// </summary>
            public readonly int NumberColumnWidth;

            /// <summary>
            /// Left rule of the Note column, between Needed and Status. The
            /// note is a currency amount run, so it reads left to right
            /// from here. Equal to <see cref="MarkerX"/> and paired with a
            /// zero <see cref="NoteWidth"/> when no row carries a note,
            /// which is every table that has no coalesced trade-up item.
            /// <para>
            /// The column takes its own gap from the row like every other
            /// one, so the note is not pushed up against the Status badges
            /// it used to sit one gap away from.
            /// </para>
            /// </summary>
            public readonly int NoteX;

            /// <summary>
            /// Width the Note column reserves: this render's widest note,
            /// measured by the caller. Zero when the column is absent.
            /// </summary>
            public readonly int NoteWidth;

            public CurrencyColumnEdges(
                int requiredRightEdge, int haveRightEdge, int neededRightEdge, int markerX,
                int numberColumnWidth, int markerWidth, int noteX = 0, int noteWidth = 0)
            {
                RequiredRightEdge = requiredRightEdge;
                HaveRightEdge = haveRightEdge;
                NeededRightEdge = neededRightEdge;
                MarkerX = markerX;
                NumberColumnWidth = numberColumnWidth;
                MarkerWidth = markerWidth;
                NoteX = noteWidth > 0 ? noteX : markerX;
                NoteWidth = noteWidth > 0 ? noteWidth : 0;
            }

            /// <summary>Left edge of the band each column's numbers grow
            /// leftward into.</summary>
            public int RequiredBandX => RequiredRightEdge - NumberColumnWidth;

            public int HaveBandX => HaveRightEdge - NumberColumnWidth;

            public int NeededBandX => NeededRightEdge - NumberColumnWidth;
        }

        /// <summary>
        /// The reserved width actually used for each of the Required/Have/
        /// Needed columns this render: CurrencyNumberColumnWidth, widened
        /// to fit widestNumberWidth (the widest of this render's actual
        /// Required/Have/Needed strings, measured by the caller via
        /// BitmapFont.MeasureString - Blish-bound, so not done here) when
        /// that exceeds the fixed floor.
        /// </summary>
        public static int EffectiveCurrencyNumberColumnWidth(int widestNumberWidth)
        {
            return widestNumberWidth > CurrencyNumberColumnWidth ? widestNumberWidth : CurrencyNumberColumnWidth;
        }

        /// <summary>
        /// The marker column's band this render: its
        /// <see cref="CurrencyMarkerWidth"/> floor, widened to
        /// markerColumnWidth (the wider of the marker pill and its "Status"
        /// header, both measured by the caller) when that exceeds it. The
        /// column is reserved whether or not any row is covered, exactly as
        /// the number columns are - a table whose reserve moved with its
        /// data would shift every other column when one currency crossed
        /// into full coverage.
        /// </summary>
        public static int EffectiveCurrencyMarkerWidth(int markerColumnWidth)
        {
            return markerColumnWidth > CurrencyMarkerWidth ? markerColumnWidth : CurrencyMarkerWidth;
        }

        /// <summary>
        /// Column layout for the currency table's Required/Have/Needed
        /// numeric columns, the Note column and the trailing full-coverage
        /// marker, derived from panelWidth plus (optionally) this render's
        /// actual widest Required/Have/Needed value width. Both regimes
        /// anchor on the same pinned right edge and the same effective
        /// (floor-or-measured) column width ShoppingColumnMath.ComputeEdges
        /// uses, so header and data rows built from the same panelWidth/
        /// widestNumberWidth pair always agree by construction.
        /// widestNumberWidth defaults to 0 (i.e. the fixed
        /// CurrencyNumberColumnWidth floor, unchanged prior
        /// behavior) for callers - existing tests among them - that don't
        /// need to pass a data-driven width.
        /// </summary>
        public static CurrencyColumnEdges ComputeCurrencyColumnEdges(
            int panelWidth, int widestNumberWidth = 0, int markerColumnWidth = 0,
            int noteColumnWidth = 0)
        {
            return EdgesFromRightEdge(
                PlanRelayoutMath.PinnedRightEdge(panelWidth),
                EffectiveCurrencyNumberColumnWidth(widestNumberWidth),
                EffectiveCurrencyMarkerWidth(markerColumnWidth),
                noteColumnWidth > 0 ? noteColumnWidth : 0);
        }

        /// <summary>
        /// Where each of the table's four headers may sit: from the column
        /// on its left to the column on its right, gutters split - never
        /// the column's own band, which is narrower than the header it
        /// names and would right-align it against its own numbers. Ink
        /// widths are this render's measured widest cell per column
        /// (SummarySectionRenderer.CurrencyColumnScan), so the rooms move
        /// with the data exactly as the headers do.
        /// </summary>
        public readonly struct CurrencyHeaderRooms
        {
            public readonly JustifiedColumnTracks.HeaderRoom Required;
            public readonly JustifiedColumnTracks.HeaderRoom Have;
            public readonly JustifiedColumnTracks.HeaderRoom Needed;
            public readonly JustifiedColumnTracks.HeaderRoom Note;
            public readonly JustifiedColumnTracks.HeaderRoom Status;

            internal CurrencyHeaderRooms(
                JustifiedColumnTracks.HeaderRoom required,
                JustifiedColumnTracks.HeaderRoom have,
                JustifiedColumnTracks.HeaderRoom needed,
                JustifiedColumnTracks.HeaderRoom note,
                JustifiedColumnTracks.HeaderRoom status)
            {
                Required = required;
                Have = have;
                Needed = needed;
                Note = note;
                Status = status;
            }
        }

        /// <summary>
        /// <see cref="CurrencyHeaderRooms"/> for one render. The currency
        /// name flexes, so Required's left neighbour is the ellipsis budget
        /// that name is allowed to fill rather than any measured string;
        /// the Status column's own right-hand neighbour is the table edge.
        /// No marker ink is needed: the marker column is reserved from
        /// <see cref="CurrencyColumnEdges.MarkerX"/> whether or not a row
        /// is covered, so that is where Needed's neighbour begins either
        /// way.
        /// </summary>
        public static CurrencyHeaderRooms CurrencyHeaderRoomsFor(
            CurrencyColumnEdges edges, int requiredInk, int haveInk, int neededInk)
        {
            int nameBudgetRight = edges.RequiredBandX - CurrencyColumnGap;
            int requiredInkX = edges.RequiredRightEdge - requiredInk;
            int haveInkX = edges.HaveRightEdge - haveInk;
            int neededInkX = edges.NeededRightEdge - neededInk;

            // With no Note column, Needed's right bound falls back to the
            // marker rule (NoteX is the marker's own x then) and Status's
            // left neighbour is Needed again - exactly the two bounds the
            // table had before the column existed.
            int noteInkRight = edges.NoteX + edges.NoteWidth;
            int statusLeftNeighborInkRight = edges.NoteWidth > 0 ? noteInkRight : edges.NeededRightEdge;

            return new CurrencyHeaderRooms(
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(nameBudgetRight, requiredInkX),
                    JustifiedColumnTracks.RoomRightBound(edges.RequiredRightEdge, haveInkX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(edges.RequiredRightEdge, haveInkX),
                    JustifiedColumnTracks.RoomRightBound(edges.HaveRightEdge, neededInkX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(edges.HaveRightEdge, neededInkX),
                    JustifiedColumnTracks.RoomRightBound(edges.NeededRightEdge, edges.NoteX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(edges.NeededRightEdge, edges.NoteX),
                    JustifiedColumnTracks.RoomRightBound(noteInkRight, edges.MarkerX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(statusLeftNeighborInkRight, edges.MarkerX),
                    edges.MarkerX + edges.MarkerWidth));
        }

        /// <summary>
        /// The extent the "Status" header centres over: the measured pill,
        /// or the column's reserved band when no row is covered, so the
        /// header holds still whether or not a currency crosses into full
        /// coverage.
        /// </summary>
        public static int CurrencyStatusInk(CurrencyColumnEdges edges, int markerInk)
        {
            return markerInk > 0 ? markerInk : edges.MarkerWidth;
        }

        private static CurrencyColumnEdges EdgesFromRightEdge(
            int rightEdge, int numberColumnWidth, int markerWidth, int noteWidth = 0)
        {
            int markerX = rightEdge - markerWidth;

            // One gap after each column that precedes Status: the name, the
            // three numbers, and the note when the table has one.
            int gapCount = CurrencyNumberColumnCount + (noteWidth > 0 ? 2 : 1);
            int reserved = CurrencyNameColumnWidth
                + (CurrencyNumberColumnCount * numberColumnWidth) + noteWidth;
            int gap = (markerX - CurrencyNameX - reserved) / gapCount;
            if (gap >= CurrencyColumnGap)
            {
                int requiredRightEdge =
                    CurrencyNameX + CurrencyNameColumnWidth + gap + numberColumnWidth;
                int haveRightEdge = requiredRightEdge + gap + numberColumnWidth;
                int neededRightEdge = haveRightEdge + gap + numberColumnWidth;

                // Integer division leaves up to gapCount-1 px over. It
                // lands in the last gap, before Status, rather than
                // widening one gap in the middle of the row.
                return new CurrencyColumnEdges(
                    requiredRightEdge, haveRightEdge, neededRightEdge, markerX,
                    numberColumnWidth, markerWidth, neededRightEdge + gap, noteWidth);
            }

            // Below the width the columns and their minimum gaps need,
            // there is nothing to share out and spreading anyway would
            // overlap them. They pack right-to-left on the minimum gap
            // instead. On a narrow panel a cramped legible table beats an
            // evenly spaced illegible one.
            int packedNoteX = noteWidth > 0 ? markerX - CurrencyColumnGap - noteWidth : markerX;
            int packedNeededRightEdge = packedNoteX - CurrencyColumnGap;
            int packedHaveRightEdge =
                packedNeededRightEdge - numberColumnWidth - CurrencyColumnGap;
            int packedRequiredRightEdge =
                packedHaveRightEdge - numberColumnWidth - CurrencyColumnGap;
            return new CurrencyColumnEdges(
                packedRequiredRightEdge, packedHaveRightEdge, packedNeededRightEdge, markerX,
                numberColumnWidth, markerWidth, packedNoteX, noteWidth);
        }
    }
}
