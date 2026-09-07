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
        // The row from the name's left edge to the table's own right edge
        // is CurrencyTrackCount EQUAL tracks - name, Required, Have, Needed
        // - each number band CENTRED on its own track. Distribution, not
        // the packed right-hand stack this table used to draw: at the plan
        // panel's real width that stack left ~1000px of nothing between a
        // currency's name and its first number, with no anchor for the eye
        // between them, and the eye could not track a row across
        // it. The idiom is RankerRowLayout.GateCell's, which already
        // divides a row's full width into N equal cells for the same
        // reason.
        //
        // Numbers right-align INSIDE their band - that is what keeps digits
        // aligned down a column - and the band, header included, centres on
        // the track. See JustifiedColumnTracks for why a shared edge is not
        // enough.
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
        // intrude into its left neighbor's column rather than clip. Under
        // distribution that reserve is what decides whether the row is wide
        // enough to distribute at all - see EdgesFromRightEdge.

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
        /// Columns the row's width is divided evenly between: the currency
        /// name, then Required, Have and Needed. The trailing Status column
        /// is NOT one of them - its badges are pinned outside the table's
        /// own right edge, on a band of their own.
        /// </summary>
        public const int CurrencyTrackCount = 4;

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

            public CurrencyColumnEdges(
                int requiredRightEdge, int haveRightEdge, int neededRightEdge, int markerX,
                int numberColumnWidth, int markerWidth)
            {
                RequiredRightEdge = requiredRightEdge;
                HaveRightEdge = haveRightEdge;
                NeededRightEdge = neededRightEdge;
                MarkerX = markerX;
                NumberColumnWidth = numberColumnWidth;
                MarkerWidth = markerWidth;
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
        /// numeric columns plus the trailing full-coverage marker, derived
        /// from panelWidth plus (optionally) this render's actual widest
        /// Required/Have/Needed value width. Both regimes anchor on the
        /// same pinned right edge and the same effective (floor-or-
        /// measured) column width ShoppingColumnMath.ComputeEdges uses, so
        /// header and data rows built from the same panelWidth/
        /// widestNumberWidth pair always agree by construction.
        /// widestNumberWidth defaults to 0 (i.e. the fixed
        /// CurrencyNumberColumnWidth floor, unchanged prior
        /// behavior) for callers - existing tests among them - that don't
        /// need to pass a data-driven width.
        /// </summary>
        public static CurrencyColumnEdges ComputeCurrencyColumnEdges(
            int panelWidth, int widestNumberWidth = 0, int markerColumnWidth = 0)
        {
            return EdgesFromRightEdge(
                PlanRelayoutMath.PinnedRightEdge(panelWidth),
                EffectiveCurrencyNumberColumnWidth(widestNumberWidth),
                EffectiveCurrencyMarkerWidth(markerColumnWidth));
        }

        /// <summary>
        /// Right edge of the number band CENTRED on track
        /// <paramref name="index"/>, through the module's shared
        /// distribution law - see <see cref="JustifiedColumnTracks"/>, which
        /// Plan History's own column band computes from as well.
        /// <para>
        /// Centred, not right-aligned on the track's own edge: a header and
        /// its numbers then share the track's centre line rather than only
        /// its right edge, which is what puts "Required" over the required
        /// amounts instead of over the gap before Have. The band's centre is
        /// the track's centre whatever
        /// <paramref name="numberColumnWidth"/> is, so a wider value grows
        /// symmetrically about the column rather than dragging it sideways.
        /// </para>
        /// </summary>
        private static int TrackBandRightEdge(int trackSpan, int index, int numberColumnWidth)
        {
            return JustifiedColumnTracks.CenteredX(
                CurrencyNameX, trackSpan, CurrencyTrackCount, index, numberColumnWidth)
                + numberColumnWidth;
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
            public readonly JustifiedColumnTracks.HeaderRoom Status;

            internal CurrencyHeaderRooms(
                JustifiedColumnTracks.HeaderRoom required,
                JustifiedColumnTracks.HeaderRoom have,
                JustifiedColumnTracks.HeaderRoom needed,
                JustifiedColumnTracks.HeaderRoom status)
            {
                Required = required;
                Have = have;
                Needed = needed;
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

            return new CurrencyHeaderRooms(
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(nameBudgetRight, requiredInkX),
                    JustifiedColumnTracks.RoomRightBound(edges.RequiredRightEdge, haveInkX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(edges.RequiredRightEdge, haveInkX),
                    JustifiedColumnTracks.RoomRightBound(edges.HaveRightEdge, neededInkX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(edges.HaveRightEdge, neededInkX),
                    JustifiedColumnTracks.RoomRightBound(edges.NeededRightEdge, edges.MarkerX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(edges.NeededRightEdge, edges.MarkerX),
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
            int rightEdge, int numberColumnWidth, int markerWidth)
        {
            int markerX = rightEdge - markerWidth;

            // The table's own right edge, which the marker trails: the
            // packed stack's Needed column, and the last track's end under
            // distribution (where Needed's band centres on that track and so
            // stops short of it by half the track's slack).
            int neededRightEdge = markerX - CurrencyColumnGap;

            // A track has to hold its own reserved number band plus the gap
            // that keeps a wide value (a 7-digit Karma balance) out of the
            // column to its left; below that the row falls back to the
            // packed right-to-left stack. See JustifiedColumnTracks.
            int trackSpan = neededRightEdge - CurrencyNameX;
            if (JustifiedColumnTracks.FitsDistributed(
                    trackSpan, CurrencyTrackCount, numberColumnWidth, CurrencyColumnGap))
            {
                return new CurrencyColumnEdges(
                    TrackBandRightEdge(trackSpan, 1, numberColumnWidth),
                    TrackBandRightEdge(trackSpan, 2, numberColumnWidth),
                    TrackBandRightEdge(trackSpan, 3, numberColumnWidth),
                    markerX,
                    numberColumnWidth,
                    markerWidth);
            }

            int haveRightEdge = neededRightEdge - numberColumnWidth - CurrencyColumnGap;
            int requiredRightEdge = haveRightEdge - numberColumnWidth - CurrencyColumnGap;
            return new CurrencyColumnEdges(
                requiredRightEdge, haveRightEdge, neededRightEdge, markerX, numberColumnWidth, markerWidth);
        }
    }
}
