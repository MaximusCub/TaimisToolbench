using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Exercises the real TreeCostColumnMath used by the recipe tree's cost
    /// column. The two Blish-bound measurements are supplied as delegates
    /// exactly as TreeSectionController supplies them (a text measurement
    /// and a currency-run measurement); everything under test - which nodes
    /// widen which sub-column, and where the sub-columns land - is the
    /// production code path itself.
    /// </summary>
    public class TreeCostColumnMathTests
    {
        // Stand-in for a proportional font: one pixel per character. Real
        // widths come from BitmapFont.MeasureString in the view; what
        // matters here is that the WIDEST string per denomination wins.
        private static int MeasureByLength(string text)
        {
            return text.Length;
        }

        private static CraftingTreeNode Node(
            int nodeId, long? subtreeCost = null,
            IReadOnlyList<CraftingTreeNode> children = null,
            IReadOnlyList<CostLine> vendorCurrencyCosts = null,
            bool isCostComponent = false)
        {
            return new CraftingTreeNode
            {
                NodeId = nodeId,
                SubtreeCost = subtreeCost,
                Children = children,
                VendorCurrencyCosts = vendorCurrencyCosts,
                IsCostComponent = isCostComponent,
            };
        }

        private static IReadOnlyList<CostLine> OneCurrencyLine()
        {
            return new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 1275 } };
        }

        private static TreeCostColumnMath.CostColumnWidths Scan(
            IReadOnlyList<CraftingTreeNode> roots, int currencyRunWidth = 0)
        {
            return TreeCostColumnMath.Scan(roots, MeasureByLength, _ => currencyRunWidth);
        }

        // --- Scan ---
        [Fact]
        public void Scan_NullOrEmptyRoots_IsAllZero()
        {
            var fromNull = Scan(null);
            Assert.Equal(0, fromNull.GoldTextWidth);
            Assert.Equal(0, fromNull.SilverTextWidth);
            Assert.Equal(0, fromNull.CopperTextWidth);
            Assert.Equal(0, fromNull.CurrencyRunWidth);

            Assert.Equal(0, Scan(new List<CraftingTreeNode>()).GoldTextWidth);
        }

        [Fact]
        public void Scan_TakesTheWidestValuePerDenominationAcrossTheWholeTree()
        {
            // 41g26s80c -> "41"/"26"/"80"; 1234g07s05c -> "1234"/"07"/"05".
            // Gold's widest comes from the deep child, silver/copper are
            // always two padded characters once gold is present.
            var deep = Node(3, subtreeCost: 12340705L);
            var mid = Node(2, subtreeCost: 412680L, children: new List<CraftingTreeNode> { deep });
            var root = Node(1, subtreeCost: 412680L, children: new List<CraftingTreeNode> { mid });

            var widths = Scan(new List<CraftingTreeNode> { root });

            Assert.Equal(4, widths.GoldTextWidth);
            Assert.Equal(2, widths.SilverTextWidth);
            Assert.Equal(2, widths.CopperTextWidth);
        }

        [Fact]
        public void Scan_SubGoldAmount_LeavesTheGoldSubColumnUnreserved()
        {
            // 45s39c has no gold unit at all, so nothing may reserve a
            // gold band - the column has to collapse, not sit empty.
            var root = Node(1, subtreeCost: 4539L);

            var widths = Scan(new List<CraftingTreeNode> { root });

            Assert.Equal(0, widths.GoldTextWidth);
            Assert.Equal(2, widths.SilverTextWidth);
            Assert.Equal(2, widths.CopperTextWidth);
        }

        [Fact]
        public void Scan_NodeWithoutASubtreeCost_WidensNothing()
        {
            // A HAVE/UNKNOWN node keeps the cost column blank; it must not
            // reserve width for a value it never draws.
            var root = Node(1, children: new List<CraftingTreeNode> { Node(2) });

            var widths = Scan(new List<CraftingTreeNode> { root });

            Assert.Equal(0, widths.GoldTextWidth);
            Assert.Equal(0, widths.SilverTextWidth);
            Assert.Equal(0, widths.CopperTextWidth);
        }

        [Fact]
        public void Scan_ZeroAndUncostedNode_ReservesNoCopperBand()
        {
            // A genuinely zero cost renders the unpriceable dash, not a
            // "0" copper segment.
            var root = Node(1, subtreeCost: 0L);

            var widths = Scan(new List<CraftingTreeNode> { root });

            Assert.Equal(0, widths.CopperTextWidth);
        }

        [Fact]
        public void Scan_CurrencyBearingNode_ReservesTheCurrencyBand()
        {
            var root = Node(1, subtreeCost: 0L, vendorCurrencyCosts: OneCurrencyLine());

            Assert.Equal(88, Scan(new List<CraftingTreeNode> { root }, currencyRunWidth: 88).CurrencyRunWidth);
        }

        [Fact]
        public void Scan_CostComponentChildren_ReserveNoCurrencyBand()
        {
            // Such a node shows only its compact coin total - the currency
            // breakdown lives one expand-click away as real child rows -
            // so reserving a currency band for it would be dead space.
            var root = Node(
                1, subtreeCost: 1000L,
                children: new List<CraftingTreeNode> { Node(2, isCostComponent: true) },
                vendorCurrencyCosts: OneCurrencyLine());

            Assert.Equal(0, Scan(new List<CraftingTreeNode> { root }, currencyRunWidth: 88).CurrencyRunWidth);
        }

        [Fact]
        public void Scan_MultipleRoots_AreAllVisited()
        {
            var roots = new List<CraftingTreeNode>
            {
                Node(1, subtreeCost: 4539L),      // silver/copper only
                Node(2, subtreeCost: 12340705L), // 4-character gold,
            };

            Assert.Equal(4, Scan(roots).GoldTextWidth);
        }

        [Fact]
        public void ShowsCurrencySegments_MatchesWhatTheRowActuallyDraws()
        {
            Assert.False(TreeCostColumnMath.ShowsCurrencySegments(null));
            Assert.False(TreeCostColumnMath.ShowsCurrencySegments(Node(1)));
            Assert.True(TreeCostColumnMath.ShowsCurrencySegments(
                Node(1, vendorCurrencyCosts: OneCurrencyLine())));
            Assert.False(TreeCostColumnMath.ShowsCurrencySegments(
                Node(1,
                    children: new List<CraftingTreeNode> { Node(2, isCostComponent: true) },
                    vendorCurrencyCosts: OneCurrencyLine())));
        }

        // --- ComputeEdges / TotalWidth ---
        //
        // Pinned as absolute pixel offsets from the column's right edge
        // rather than recomputed from the same constants the formula
        // reads. Segment width is text + CoinLabelIconGap(2) +
        // CoinIconSize(18, = CurrencyIconTiers.WalletBarIconSize);
        // sub-columns are separated by CoinSegmentGap(6). A deliberate
        // geometry change re-baselines the literals here - as the coin
        // icon's re-measurement against the game's own bar just did,
        // 16 -> 18.
        [Fact]
        public void ComputeEdges_AllThreeDenominations_StackRightToLeft()
        {
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 0);
            var edges = TreeCostColumnMath.ComputeEdges(1000, widths);

            Assert.Equal(1000, edges.CurrencyRightEdge);
            Assert.Equal(1000, edges.CopperRightEdge);          // no currency band
            Assert.Equal(1000 - 40 - 6, edges.SilverRightEdge); // copper: 20+2+18
            Assert.Equal(1000 - 40 - 6 - 40 - 6, edges.GoldRightEdge);
        }

        [Fact]
        public void ComputeEdges_CurrencyBand_PushesEveryCoinSubColumnLeft()
        {
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 88);
            var edges = TreeCostColumnMath.ComputeEdges(1000, widths);

            Assert.Equal(1000, edges.CurrencyRightEdge);
            Assert.Equal(1000 - 88 - 6, edges.CopperRightEdge);
        }

        [Fact]
        public void ComputeEdges_AbsentDenomination_CostsNoReservedWidth()
        {
            // No row in the tree renders gold. Its edge still exists (it
            // is simply where a gold segment WOULD end), but nothing is
            // reserved for it, so the column as a whole is exactly the
            // silver and copper bands - a plan whose costs never reach a
            // gold does not get a gold-sized hole in its layout.
            var widths = new TreeCostColumnMath.CostColumnWidths(0, 20, 20, 0);
            var edges = TreeCostColumnMath.ComputeEdges(1000, widths);

            Assert.Equal(1000 - 40 - 6, edges.SilverRightEdge);
            Assert.Equal(40 + 6 + 40, edges.TotalWidth);
        }

        [Fact]
        public void ComputeEdges_NoCoinRowsAtAll_CollapsesToTheCurrencyBandAlone()
        {
            var widths = new TreeCostColumnMath.CostColumnWidths(0, 0, 0, 88);
            var edges = TreeCostColumnMath.ComputeEdges(1000, widths);

            Assert.Equal(1000, edges.CurrencyRightEdge);
            Assert.Equal(1000 - 88 - 6, edges.CopperRightEdge);
            Assert.Equal(88, edges.TotalWidth);
        }

        [Fact]
        public void ComputeEdges_IconsLandOnTheSameXForEveryRow()
        {
            // The whole point of the sub-columns: a segment right-aligned
            // to its own sub-column edge puts its fixed-width icon at the
            // same x whatever the number's own width is.
            var widths = new TreeCostColumnMath.CostColumnWidths(40, 20, 20, 0);
            var edges = TreeCostColumnMath.ComputeEdges(1000, widths);

            int narrowIconLeft = edges.GoldRightEdge - TreeCostColumnMath.SegmentWidth(10)
                + 10 + CoinSegmentMath.CoinLabelIconGap;
            int wideIconLeft = edges.GoldRightEdge - TreeCostColumnMath.SegmentWidth(40)
                + 40 + CoinSegmentMath.CoinLabelIconGap;

            Assert.Equal(wideIconLeft, narrowIconLeft);
            Assert.Equal(edges.GoldRightEdge - CoinSegmentMath.CoinIconSize, narrowIconLeft);
        }

        [Fact]
        public void ComputeRowEdges_CoinOnlyRow_EndsOnTheColumnRightEdge()
        {
            // Bug 4, reported in game: the "Cost" header right-aligns on the
            // column's own right edge, so a row that never fills the shared
            // currency band must not stop short of it - a gold figure
            // sitting a whole currency band left of the header is what the
            // user saw.
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 88);

            var coinOnly = TreeCostColumnMath.ComputeRowEdges(1000, widths, rowDrawsCurrency: false);

            Assert.Equal(1000, coinOnly.CopperRightEdge);
            Assert.Equal(1000, coinOnly.CurrencyRightEdge);
        }

        [Fact]
        public void ComputeRowEdges_CurrencyRow_KeepsTheSharedBand()
        {
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 88);

            var withCurrency = TreeCostColumnMath.ComputeRowEdges(1000, widths, rowDrawsCurrency: true);

            Assert.Equal(1000, withCurrency.CurrencyRightEdge);
            Assert.Equal(1000 - 88 - 6, withCurrency.CopperRightEdge);
            Assert.Equal(
                TreeCostColumnMath.ComputeEdges(1000, widths).GoldRightEdge,
                withCurrency.GoldRightEdge);
        }

        [Fact]
        public void ComputeRowEdges_EveryRowShape_EndsOnTheSameRightEdge()
        {
            // The header is right-aligned to costRightEdge, so every row's
            // rightmost segment has to end there whatever it renders:
            // coin-only, currency-only, mixed, and the unpriceable dash
            // (which is placed on the copper edge).
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 88);

            var coinOnly = TreeCostColumnMath.ComputeRowEdges(1000, widths, rowDrawsCurrency: false);
            var mixed = TreeCostColumnMath.ComputeRowEdges(1000, widths, rowDrawsCurrency: true);

            Assert.Equal(1000, coinOnly.CopperRightEdge);
            Assert.Equal(1000, mixed.CurrencyRightEdge);
        }

        [Fact]
        public void ComputeRowEdges_NoCurrencyInTheWholeTree_MatchesComputeEdges()
        {
            // Nothing reserved a currency band in the first place, so both
            // row shapes are the pre-existing layout, unchanged.
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 0);
            var shared = TreeCostColumnMath.ComputeEdges(1000, widths);

            foreach (bool drawsCurrency in new[] { false, true })
            {
                var row = TreeCostColumnMath.ComputeRowEdges(1000, widths, drawsCurrency);

                Assert.Equal(shared.GoldRightEdge, row.GoldRightEdge);
                Assert.Equal(shared.SilverRightEdge, row.SilverRightEdge);
                Assert.Equal(shared.CopperRightEdge, row.CopperRightEdge);
                Assert.Equal(shared.CurrencyRightEdge, row.CurrencyRightEdge);
            }
        }

        [Fact]
        public void ComputeRowEdges_ReportsTheWholeColumnsReservedWidth()
        {
            // TotalWidth is the budget the tree reserves so a wide row
            // cannot run back into the decision pills - a property of the
            // tree, not of the row being placed, so collapsing a row's
            // currency band must not shrink it.
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 88);

            Assert.Equal(
                TreeCostColumnMath.TotalWidth(widths),
                TreeCostColumnMath.ComputeRowEdges(1000, widths, rowDrawsCurrency: false).TotalWidth);
        }

        [Fact]
        public void ComputeRowEdges_CoinOnlyRow_StaysInsideTheReservedColumn()
        {
            // Pulling a coin-only run right must not push its leftmost
            // segment past the column's own left boundary and into the
            // pills.
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 88);
            var row = TreeCostColumnMath.ComputeRowEdges(1000, widths, rowDrawsCurrency: false);

            int leftmostX = row.GoldRightEdge - TreeCostColumnMath.SegmentWidth(widths.GoldTextWidth);

            Assert.True(leftmostX >= 1000 - TreeCostColumnMath.TotalWidth(widths));
        }

        [Fact]
        public void TotalWidth_IsEveryPopulatedBandPlusItsGaps()
        {
            Assert.Equal(0, TreeCostColumnMath.TotalWidth(TreeCostColumnMath.CostColumnWidths.Empty));

            // copper only: 20 + 2 + 18
            Assert.Equal(40, TreeCostColumnMath.TotalWidth(
                new TreeCostColumnMath.CostColumnWidths(0, 0, 20, 0)));

            // gold(50) + gap + silver(40) + gap + copper(40) + gap + currency(88)
            Assert.Equal(50 + 6 + 40 + 6 + 40 + 6 + 88, TreeCostColumnMath.TotalWidth(
                new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 88)));
        }

        [Fact]
        public void TotalWidth_AgreesWithTheSpanComputeEdgesActuallyLaysOut()
        {
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 88);
            var edges = TreeCostColumnMath.ComputeEdges(1000, widths);

            int leftmostX = edges.GoldRightEdge - TreeCostColumnMath.SegmentWidth(widths.GoldTextWidth);
            Assert.Equal(1000 - leftmostX, edges.TotalWidth);
            Assert.Equal(TreeCostColumnMath.TotalWidth(widths), edges.TotalWidth);
        }

        // --- ScanColumns name extent (dead gutters) ---
        private static CraftingTreeNode NamedNode(
            int nodeId, string name, int quantity = 0, IReadOnlyList<CraftingTreeNode> children = null)
        {
            return new CraftingTreeNode
            {
                NodeId = nodeId,
                Name = name,
                Quantity = quantity,
                Children = children,
            };
        }

        // The view's own nameX arithmetic (indent 24 per depth, caret 18,
        // icon frame 34, gap 6) against the one-pixel-per-character font.
        private static TreeCostColumnMath.TreeColumnScan ScanTree(IReadOnlyList<CraftingTreeNode> roots)
        {
            return TreeCostColumnMath.ScanColumns(roots, MeasureByLength, _ => 0);
        }

        // --- ScanColumns node count (the Recipe Tree
        // section header's parenthesised count) ---
        [Fact]
        public void ScanColumns_NodeCount_CountsEveryNodeAtEveryDepth()
        {
            var roots = new[]
            {
                NamedNode(1, "Root", children: new[]
                {
                    NamedNode(2, "Child", children: new[] { NamedNode(3, "Grandchild") }),
                    NamedNode(4, "Sibling"),
                }),
            };

            Assert.Equal(4, ScanTree(roots).NodeCount);
        }

        // Rows are built lazily, so a count taken from what is on screen
        // would change under the reader on every caret click. The count is
        // the whole tree - what Expand All reveals.
        [Fact]
        public void ScanColumns_NodeCount_IsIndependentOfAnyExpansionState()
        {
            var deep = new[]
            {
                NamedNode(1, "Root", children: new[]
                {
                    NamedNode(2, "Child", children: new[] { NamedNode(3, "Grandchild") }),
                }),
            };
            var flat = new[] { NamedNode(1, "A"), NamedNode(2, "B"), NamedNode(3, "C") };

            Assert.Equal(3, ScanTree(deep).NodeCount);
            Assert.Equal(3, ScanTree(flat).NodeCount);
        }

        [Fact]
        public void ScanColumns_NodeCount_CountsEveryRootOfAMultiItemBatch()
        {
            var roots = new[] { NamedNode(1, "First"), NamedNode(2, "Second") };

            Assert.Equal(2, ScanTree(roots).NodeCount);
        }

        [Fact]
        public void ScanColumns_NoRoots_ReportsZeroNodes()
        {
            Assert.Equal(0, ScanTree(new CraftingTreeNode[0]).NodeCount);
            Assert.Equal(0, TreeCostColumnMath.TreeColumnScan.Empty.NodeCount);
        }

        // A null child is skipped by the walk, and must not be counted as a
        // row the section will never render.
        [Fact]
        public void ScanColumns_NullChild_IsNotCounted()
        {
            var roots = new[] { NamedNode(1, "Root", children: new CraftingTreeNode[] { null }) };

            Assert.Equal(1, ScanTree(roots).NodeCount);
        }

        [Fact]
        public void ScanColumns_CostWidths_MatchTheCostOnlyScan()
        {
            var roots = new[] { Node(1, subtreeCost: 123456), Node(2, subtreeCost: 42) };

            var costOnly = Scan(roots);
            var both = TreeCostColumnMath.ScanColumns(roots, MeasureByLength, _ => 0);

            Assert.Equal(costOnly.GoldTextWidth, both.CostWidths.GoldTextWidth);
            Assert.Equal(costOnly.SilverTextWidth, both.CostWidths.SilverTextWidth);
            Assert.Equal(costOnly.CopperTextWidth, both.CostWidths.CopperTextWidth);
        }

        // --- WidestRowRunWidth (the ink extent, which is what the header
        // centres over - see HeaderX below) ---
        [Fact]
        public void Scan_OneCoinOnlyRow_ReachesTheWholeReserve()
        {
            // 12g34s56c -> "12"/"34"/"56", one pixel per character. With a
            // single row the sub-column maxima all come from it, so its own
            // ink does fill the reserve and the two agree.
            var widths = Scan(new[] { Node(1, subtreeCost: 12345656) });

            Assert.Equal(TreeCostColumnMath.TotalWidth(widths), widths.WidestRowRunWidth);
        }

        [Fact]
        public void Scan_ACurrencyBandNoCoinRowFills_LeavesTheInkShortOfTheReserve()
        {
            // The reported shape: coin-only rows collapse the currency band
            // for themselves (ComputeRowEdges), and the currency row draws
            // no coin, so NOTHING ever reaches the reserve's left edge. The
            // reserve is still correct - it is what keeps a wide row off the
            // decision pills - it is just not what a reader sees.
            var roots = new[]
            {
                Node(1, subtreeCost: 1234567),
                Node(2, subtreeCost: 0, vendorCurrencyCosts: OneCurrencyLine()),
            };

            var widths = Scan(roots, currencyRunWidth: 88);

            // One row in each regime. The tie goes to the coin-only one -
            // "123"/"45"/"67", segments 23/22/22 and two 6px gaps, 79px of
            // ink - rather than to the currency row's 88px, and either way
            // both fall well short of the 173px reserve.
            Assert.Equal(79, widths.WidestRowRunWidth);
            Assert.Equal(173, TreeCostColumnMath.TotalWidth(widths));
        }

        /// <summary>
        /// The two regimes do not share an extent, so the header follows
        /// the one MORE rows are drawn in. Reported case: a plan whose
        /// coin rows all collapse the currency band, plus a single vendor
        /// row that does not, put the header 43px left of every coin
        /// figure it was meant to label.
        /// </summary>
        [Fact]
        public void Scan_OneMixedRowAmongCoinRows_DoesNotDefineTheInkExtent()
        {
            var coinRowsOnly = new[]
            {
                Node(1, subtreeCost: 1234567),
                Node(2, subtreeCost: 45678),
                Node(3, subtreeCost: 789),
            };
            var withOneMixedRow = new[]
            {
                Node(1, subtreeCost: 1234567),
                Node(2, subtreeCost: 45678),
                Node(3, subtreeCost: 789),
                Node(4, subtreeCost: 4242, vendorCurrencyCosts: OneCurrencyLine()),
            };

            var without = Scan(coinRowsOnly, currencyRunWidth: 88);
            var with = Scan(withOneMixedRow, currencyRunWidth: 88);

            Assert.Equal(without.WidestRowRunWidth, with.WidestRowRunWidth);
            Assert.True(TreeCostColumnMath.TotalWidth(with) > with.WidestRowRunWidth + 88);
        }

        /// <summary>
        /// And the mirror: a column that is mostly vendor rows centres on
        /// theirs, so the rule is "the regime with more rows", not "coin
        /// always wins".
        /// </summary>
        [Fact]
        public void Scan_MostlyMixedRows_TakeTheirOwnExtent()
        {
            var roots = new[]
            {
                Node(1, subtreeCost: 789),
                Node(2, subtreeCost: 4242, vendorCurrencyCosts: OneCurrencyLine()),
                Node(3, subtreeCost: 4243, vendorCurrencyCosts: OneCurrencyLine()),
            };

            var widths = Scan(roots, currencyRunWidth: 88);

            // A mixed row keeps the currency band, so its coin segments
            // start a whole band-plus-gap left of where a coin-only row's
            // would - which is exactly why it cannot be the extent for a
            // column of coin rows.
            Assert.True(widths.WidestRowRunWidth > 88);
        }

        [Fact]
        public void Scan_ARowThatLeadsWithSilver_ReachesOnlyItsOwnSubColumn()
        {
            // Under a gold sub-column reserved by a richer row, a sub-gold
            // value starts one whole band further right. Its ink extent is
            // measured from where it actually starts, not from the reserve.
            var roots = new[] { Node(1, subtreeCost: 10000000), Node(2, subtreeCost: 4567) };

            var widths = Scan(roots);

            // Row 2 is "45"/"67", starting at the silver sub-column's own
            // edge: 22 + 6 + 22 = 50. Row 1's "1000"/"00"/"00" run is
            // 24 + 6 + 22 + 6 + 22 = 80, which wins.
            Assert.Equal(80, widths.WidestRowRunWidth);
        }

        [Fact]
        public void Scan_NothingPriced_HasNoInkAtAll()
        {
            var widths = Scan(new[] { Node(1), Node(2) });

            Assert.Equal(0, widths.WidestRowRunWidth);
        }

        // --- LeftmostInkReach, and the column budget the decision-pill
        // column spends against it (Reserve / InkFloor / RightSlack /
        // WidthAfterClaim) ---

        /// <summary>The tree's fixed cost-column floor, which
        /// TreeSectionController passes as fixedFloor.</summary>
        private const int FixedFloor = 150;

        [Fact]
        public void RightSlack_MaximaFromDifferentRows_IsRoomTheOldReadingCalledNegative()
        {
            // The reported defect: the room the pill column may claim was
            // read as the fixed FLOOR less TotalWidth, and TotalWidth sums
            // per-denomination maxima no one row draws, so any tree with a
            // currency band produced a negative that clamped to zero and
            // the column could never claim rightward at all.
            var roots = new[]
            {
                Node(1, subtreeCost: 1234567),
                Node(2, subtreeCost: 0, vendorCurrencyCosts: OneCurrencyLine()),
            };

            var widths = Scan(roots, currencyRunWidth: 88);

            Assert.Equal(173, TreeCostColumnMath.TotalWidth(widths));
            Assert.True(FixedFloor - TreeCostColumnMath.TotalWidth(widths) < 0);

            Assert.Equal(88, widths.LeftmostInkReach);
            Assert.Equal(173, TreeCostColumnMath.Reserve(widths, FixedFloor));
            Assert.Equal(85, TreeCostColumnMath.RightSlack(widths, FixedFloor));
        }

        /// <summary>
        /// The ink reach is the max over BOTH regimes, where
        /// WidestRowRunWidth is whichever single one the header follows.
        /// A mixed row among coin rows does not move the header, but its
        /// coin segments start a whole currency band further left, and
        /// that is the line the pill column may not cross.
        /// </summary>
        [Fact]
        public void Scan_AMixedRowTheHeaderIgnores_StillSetsTheInkReach()
        {
            var roots = new[]
            {
                Node(1, subtreeCost: 1234567),
                Node(2, subtreeCost: 45678),
                Node(3, subtreeCost: 789),
                Node(4, subtreeCost: 4242, vendorCurrencyCosts: OneCurrencyLine()),
            };

            var widths = Scan(roots, currencyRunWidth: 88);

            Assert.Equal(79, widths.WidestRowRunWidth);
            Assert.Equal(144, widths.LeftmostInkReach);
            Assert.Equal(29, TreeCostColumnMath.RightSlack(widths, FixedFloor));
        }

        [Fact]
        public void WidthAfterClaim_NoClaimCanReachTheLeftmostInk()
        {
            var roots = new[]
            {
                Node(1, subtreeCost: 1234567),
                Node(2, subtreeCost: 45678),
                Node(3, subtreeCost: 789),
                Node(4, subtreeCost: 4242, vendorCurrencyCosts: OneCurrencyLine()),
            };

            var widths = Scan(roots, currencyRunWidth: 88);
            int slack = TreeCostColumnMath.RightSlack(widths, FixedFloor);

            Assert.Equal(
                widths.LeftmostInkReach,
                TreeCostColumnMath.WidthAfterClaim(widths, FixedFloor, slack));

            // And a claim written from outside RightSlack's answer stops
            // on the same line rather than walking through the values.
            Assert.Equal(
                widths.LeftmostInkReach,
                TreeCostColumnMath.WidthAfterClaim(widths, FixedFloor, slack + 500));
        }

        [Fact]
        public void RightSlack_NothingPriced_OffersNoneOfTheColumn()
        {
            // Every row still draws the unpriceable dash, which this scan
            // never measures, so a reach of 0 must not read as "the whole
            // column is free".
            var widths = Scan(new[] { Node(1), Node(2) });

            Assert.Equal(0, widths.LeftmostInkReach);
            Assert.Equal(FixedFloor, TreeCostColumnMath.InkFloor(widths, FixedFloor));
            Assert.Equal(0, TreeCostColumnMath.RightSlack(widths, FixedFloor));
            Assert.Equal(
                FixedFloor, TreeCostColumnMath.WidthAfterClaim(widths, FixedFloor, 90));
        }

        [Fact]
        public void RightSlack_OneCoinOnlyRow_IsTheFixedFloorsOwnSurplus()
        {
            // A single row's ink does fill the sub-column reserve - every
            // maximum is its own - so what is left to give back is the
            // fixed floor's surplus over that ink, 70px the old reading
            // also refused because it compared the floor with itself.
            var widths = Scan(new[] { Node(1, subtreeCost: 12345656) });

            Assert.Equal(80, widths.LeftmostInkReach);
            Assert.Equal(TreeCostColumnMath.TotalWidth(widths), widths.LeftmostInkReach);
            Assert.Equal(FixedFloor - 80, TreeCostColumnMath.RightSlack(widths, FixedFloor));
        }

        // --- HeaderX (the "Cost" header centres over the INK, not over
        // either reserve around it, and only the pill column on its left
        // and the table's own edge on its right may move it - see
        // JustifiedColumnTracks.HeaderRoom) ---
        private static JustifiedColumnTracks.HeaderRoom CostRoom(int costRightEdge, int costInk)
        {
            PlanRelayoutMath.ComputeTreeHeaderRooms(
                new PlanRelayoutMath.TreeColumnEdges(costRightEdge - 500, costRightEdge, 0),
                60, costInk, out _, out var cost);
            return cost;
        }

        [Fact]
        public void HeaderX_CentresTheHeaderOverTheInk_NotOverTheReserve()
        {
            // 50 + 6 + 40 + 6 + 40 = 142px of coin ink, then a 6px gap and
            // an 88px currency band no coin row ever fills: a 236px reserve
            // over 142px of ink. Centring in the reserve puts the word 47px
            // left of the numbers, which is the reported defect.
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 88, 142);

            int x = TreeCostColumnMath.HeaderX(1000, widths, 40, CostRoom(1000, 142));

            Assert.Equal(909, x);
            Assert.Equal(
                862,
                JustifiedColumnTracks.CenteredInBand(1000 - 236, 236, 40));
        }

        [Fact]
        public void HeaderX_IsIndependentOfEveryReserveTheInkDoesNotFill()
        {
            // TreeSectionController reserves max(TotalWidth, its own floor)
            // for the column, and TotalWidth itself sums per-denomination
            // maxima no one row draws together. Neither may move the header
            // while the ink under it is unchanged.
            var narrow = new TreeCostColumnMath.CostColumnWidths(20, 0, 0, 0, 38);
            var wide = new TreeCostColumnMath.CostColumnWidths(20, 20, 20, 60, 38);

            Assert.Equal(
                TreeCostColumnMath.HeaderX(1000, narrow, 40, CostRoom(1000, 38)),
                TreeCostColumnMath.HeaderX(1000, wide, 40, CostRoom(1000, 38)));
        }

        [Fact]
        public void HeaderX_HeaderWiderThanTheInk_StopsAtTheTableEdge()
        {
            // Cost is the last column, so its right-hand bound is the
            // table's own edge and there is nowhere for a header wider than
            // its ink to centre. It right-aligns on that edge rather than
            // overhanging the panel margin - the one bound the header law
            // never yields, and where a tree with nothing priced at all
            // lands too.
            Assert.Equal(
                960,
                TreeCostColumnMath.HeaderX(
                    1000, TreeCostColumnMath.CostColumnWidths.Empty, 40, CostRoom(1000, 0)));
            Assert.Equal(
                960,
                TreeCostColumnMath.HeaderX(
                    1000, new TreeCostColumnMath.CostColumnWidths(1, 0, 0, 0, 19), 40,
                    CostRoom(1000, 19)));
        }

        [Fact]
        public void HeaderX_TracksTheColumnsRightEdge()
        {
            var widths = new TreeCostColumnMath.CostColumnWidths(30, 20, 20, 0, 130);

            Assert.Equal(
                TreeCostColumnMath.HeaderX(1000, widths, 40, CostRoom(1000, 130)) + 200,
                TreeCostColumnMath.HeaderX(1200, widths, 40, CostRoom(1200, 130)));
        }

        [Fact]
        public void SegmentWidth_AbsentText_IsZero()
        {
            Assert.Equal(0, TreeCostColumnMath.SegmentWidth(0));
            Assert.Equal(
                12 + CoinSegmentMath.CoinLabelIconGap + CoinSegmentMath.CoinIconSize,
                TreeCostColumnMath.SegmentWidth(12));
        }
    }
}
