using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class ShoppingColumnMathTests
    {
        [Fact]
        public void TypicalValues_FallBackToFixedMinimums()
        {
            // Small coin values (well under the fixed minimums) -> edges
            // fall back to the same minimums as the old fixed-width
            // geometry, so ordinary short lists render exactly as before.
            var edges = ShoppingColumnMath.ComputeEdges(totalRightEdge: 700, maxEachWidth: 40, maxTotalWidth: 60);

            Assert.False(edges.Distributed);
            Assert.Equal(700, edges.TotalRightEdge);
            Assert.Equal(700 - 150 - 20, edges.EachRightEdge);
            Assert.Equal(150, edges.TotalBandWidth);
            Assert.Equal(110, edges.EachBandWidth);
        }

        [Fact]
        public void FourDigitGold_BothColumns_ExpandBeyondMinimums()
        {
            // Reproduces the reported bug: 4-digit-gold coin strings (e.g.
            // "1234g 56s 78c") measure wider than the fixed minimums in
            // both the Each and Total columns - this is the Mystic Coin
            // row overflow ("2502x 02 26") from the user's capture.
            var edges = ShoppingColumnMath.ComputeEdges(totalRightEdge: 700, maxEachWidth: 180, maxTotalWidth: 220);

            Assert.False(edges.Distributed);
            Assert.Equal(700, edges.TotalRightEdge);
            Assert.Equal(700 - 220 - 20, edges.EachRightEdge);
            Assert.Equal(700 - 220 - 20 - 180 - 20, edges.SourceX);
        }

        [Fact]
        public void ZeroWidths_FallBackToMinimums()
        {
            // No row had a non-zero coin value in a column (e.g. an
            // all-currency shopping list) - the pre-scan yields 0 for that
            // column, and the fixed minimums keep it from collapsing to a
            // zero-width column.
            var edges = ShoppingColumnMath.ComputeEdges(totalRightEdge: 700, maxEachWidth: 0, maxTotalWidth: 0);

            Assert.Equal(700 - 150 - 20, edges.EachRightEdge);
            Assert.Equal(700 - 150 - 20 - 110 - 20, edges.SourceX);
        }

        [Fact]
        public void OnlyOneColumnWide_OtherStaysAtMinimum()
        {
            // A list where only Total has wide values (e.g. large
            // quantities of a cheap item) must not widen Each too - the two
            // columns are sized independently.
            var edges = ShoppingColumnMath.ComputeEdges(totalRightEdge: 700, maxEachWidth: 30, maxTotalWidth: 300);

            Assert.Equal(700 - 300 - 20, edges.EachRightEdge);
            Assert.Equal(700 - 300 - 20 - 110 - 20, edges.SourceX);
        }

        [Theory]
        [InlineData(792, 0, 0)]
        [InlineData(792, 180, 220)]
        [InlineData(400, 300, 300)]
        [InlineData(200, 0, 0)]
        public void OrderingInvariant_SourceLessThanEachLessThanTotal(
            int totalRightEdge, int maxEachWidth, int maxTotalWidth)
        {
            var edges = ShoppingColumnMath.ComputeEdges(totalRightEdge, maxEachWidth, maxTotalWidth);

            Assert.True(edges.SourceX < edges.EachRightEdge);
            Assert.True(edges.EachRightEdge < edges.TotalRightEdge);
        }

        // --- The Amount column reads before the item, on the row's own
        // left inset, the way Used Materials and the Account Snapshot
        // already read ---
        [Fact]
        public void TheAmountBandOpensTheRow_AndTheIconAndNameFollowIt()
        {
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 1400, maxEachWidth: 0, maxTotalWidth: 0,
                amountBandWidth: 79, sourceColumnWidth: 96);

            Assert.Equal(79, edges.AmountBandWidth);
            Assert.Equal(AmountLedRowMath.AmountX, AmountLedRowMath.AmountTextX(79, 79));
            Assert.True(edges.IconX > AmountLedRowMath.AmountX);
            Assert.True(edges.IconX < edges.NameX);
            Assert.True(edges.NameX < edges.SourceX);
        }

        [Fact]
        public void AWiderAmountBand_PushesTheItemColumnRight_AndNothingElse()
        {
            var narrow = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 60, 96);
            var wide = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 100, 96);

            Assert.Equal(40, wide.IconX - narrow.IconX);
            Assert.Equal(40, wide.NameX - narrow.NameX);
            Assert.Equal(narrow.TotalRightEdge, wide.TotalRightEdge);
            Assert.Equal(narrow.NameColumnWidth, wide.NameColumnWidth);
        }

        // --- Source column (the badge stopped trailing the name and
        // became an aligned column inside the pinned right-hand block) ---
        [Fact]
        public void SourceColumn_SitsOneGapAndOneEachBandLeftOfTheEachEdge()
        {
            var edges = ShoppingColumnMath.ComputeEdges(
                totalRightEdge: 700, maxEachWidth: 40, maxTotalWidth: 60,
                amountBandWidth: 79, sourceColumnWidth: 96);

            Assert.False(edges.Distributed);
            Assert.Equal(
                edges.EachRightEdge - 110 - ShoppingColumnMath.ColumnGap - 96,
                edges.SourceX);
        }

        [Fact]
        public void SourceColumn_LeftEdgeIsTheNameBudgetsStop_NotTheEachEdge()
        {
            // The name used to budget against the next column with its OWN
            // badge width subtracted, so no two rows' badges lined up. The
            // budget stops at one fixed x for the whole table now, and that
            // x is strictly left of the Each column.
            var edges = ShoppingColumnMath.ComputeEdges(700, 40, 60, 79, 96);

            Assert.True(edges.SourceX < edges.EachRightEdge);
        }

        [Fact]
        public void WiderBadge_MovesTheSourceColumnLeft_AndNothingElse()
        {
            // The badge column widens into the NAME's space, never into
            // Amount/Each/Total - every one of those hangs off the pinned
            // right edge and is unaffected.
            var narrow = ShoppingColumnMath.ComputeEdges(700, 40, 60, 79, 60);
            var wide = ShoppingColumnMath.ComputeEdges(700, 40, 60, 79, 100);

            Assert.Equal(narrow.SourceX - 40, wide.SourceX);
            Assert.Equal(narrow.EachRightEdge, wide.EachRightEdge);
            Assert.Equal(narrow.TotalRightEdge, wide.TotalRightEdge);
        }

        [Fact]
        public void SourceColumn_MovesRightWithThePanel_ByItsOwnTracksShare()
        {
            // Both widths distribute (the module's own plan panel is past
            // the threshold at every supported window size), so the Source
            // column takes its share of the increase rather than the whole
            // of it - and the Total column still ends on the pinned edge.
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 1252, maxEachWidth: 40, maxTotalWidth: 60,
                amountBandWidth: 79, sourceColumnWidth: 96);
            var wider = ShoppingColumnMath.ComputeEdgesForPanel(1452, 40, 60, 79, 96);

            Assert.True(edges.Distributed);
            Assert.InRange(wider.SourceX - edges.SourceX, 1, 200);
            Assert.Equal(PlanRelayoutMath.PinnedRightEdge(1252), edges.TotalRightEdge);
            Assert.Equal(PlanRelayoutMath.PinnedRightEdge(1452), wider.TotalRightEdge);
        }

        // --- The Item column's reserve (it used to be two tracks of six,
        // so it grew with the panel whatever its names measured) ---
        [Fact]
        public void EffectiveNameColumnWidth_IsTheLongestNamePlusHeadroom_NeverBelowTheFloor()
        {
            Assert.Equal(
                ShoppingColumnMath.NameMinWidth,
                ShoppingColumnMath.EffectiveNameColumnWidth(0));
            Assert.Equal(
                ShoppingColumnMath.NameMinWidth,
                ShoppingColumnMath.EffectiveNameColumnWidth(
                    ShoppingColumnMath.NameMinWidth - ShoppingColumnMath.NameHeadroom - 1));
            Assert.Equal(
                300 + ShoppingColumnMath.NameHeadroom,
                ShoppingColumnMath.EffectiveNameColumnWidth(300));
        }

        [Fact]
        public void ComputeEdgesForPanel_TheNameReserveTracksTheNames_AndTheDataTracksTakeTheRest()
        {
            // The reported defect: "4g 36s 20c" plus two currency segments
            // collides with its neighbour while the Item column sits on
            // several hundred px it does not need. A longer set of names
            // takes room from the data tracks, and a shorter set gives it
            // back - which under the old two-of-six split it could not.
            var shortNames = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 79, 96);
            var longNames = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 79, 96, 300);

            Assert.Equal(ShoppingColumnMath.NameMinWidth, shortNames.NameColumnWidth);
            Assert.Equal(300 + ShoppingColumnMath.NameHeadroom, longNames.NameColumnWidth);
            Assert.Equal(
                shortNames.TrackSpan - (longNames.NameColumnWidth - shortNames.NameColumnWidth),
                longNames.TrackSpan);
        }

        [Fact]
        public void ComputeEdgesForPanel_EveryDataColumnIsWiderThanTheFourTrackShare()
        {
            // Amount left the track grid for the row's left inset, so the
            // three columns that remain divide the same span four used to.
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 79, 96);

            Assert.True(edges.Distributed);
            Assert.Equal(347, edges.TrackSpan / ShoppingColumnMath.DataColumnCount);
            Assert.Equal(260, edges.TrackSpan / (ShoppingColumnMath.DataColumnCount + 1));
        }

        [Fact]
        public void ComputeEdgesForPanel_ALongNameGivesUpHeadroomBeforeTheDataColumnsGiveUpRoom()
        {
            // Names long enough to eat the row: the reserve is capped at
            // whatever three full data tracks leave, so the tracks land
            // exactly on their own floor (widest band plus the gap) rather
            // than below it.
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 79, 96, 2000);

            Assert.True(edges.Distributed);
            Assert.True(
                edges.NameColumnWidth < ShoppingColumnMath.EffectiveNameColumnWidth(2000),
                "the reserve is capped, not honoured outright");
            Assert.Equal(
                ShoppingColumnMath.TotalMinWidth + ShoppingColumnMath.ColumnGap,
                edges.TrackSpan / ShoppingColumnMath.DataColumnCount);
        }

        // --- Header centring (a header sits over the INK its cells cover,
        // not over the band that ink sits in) ---
        [Fact]
        public void BandWidths_AreTheReservesTheColumnsActuallyUse()
        {
            var edges = ShoppingColumnMath.ComputeEdges(
                totalRightEdge: 792, maxEachWidth: 180, maxTotalWidth: 40,
                amountBandWidth: 79, sourceColumnWidth: 96);

            // Measured where it beats the floor, floored where it does not.
            Assert.Equal(180, edges.EachBandWidth);
            Assert.Equal(150, edges.TotalBandWidth);
            Assert.Equal(79, edges.AmountBandWidth);
            Assert.Equal(96, edges.SourceBandWidth);

            // And each band ends exactly on its column's own edge, which is
            // what makes centring a header in it centre it over the cells.
            Assert.Equal(edges.EachRightEdge - 180, edges.EachBandX);
            Assert.Equal(edges.TotalRightEdge - 150, edges.TotalBandX);
        }

        [Fact]
        public void HeaderCentring_MeetsTheValuesOnTheirOwnCentreLine_NotTheBands()
        {
            // The law: a header and the ink under it centre on one axis.
            // Total's band is floored at TotalMinWidth (150), so on a list
            // of cheap items the band is half again the widest price in it
            // and the two axes are not the same line.
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 1000, maxEachWidth: 0, maxTotalWidth: 0,
                amountBandWidth: 79, sourceColumnWidth: 96);
            const int headerWidth = 44;
            const int totalInk = 100;

            var rooms = ShoppingColumnMath.HeaderRoomsFor(edges, 12, 60, 100, totalInk);
            int headerX = JustifiedColumnTracks.CenteredOverContentRightAligned(
                edges.TotalRightEdge, totalInk, headerWidth, rooms.Total);

            Assert.Equal(150, edges.TotalBandWidth);
            Assert.Equal(
                edges.TotalRightEdge - (totalInk / 2),
                headerX + (headerWidth / 2));
            Assert.Equal(
                25,
                headerX - JustifiedColumnTracks.CenteredInBand(
                    edges.TotalBandX, edges.TotalBandWidth, headerWidth));
        }

        // --- SegmentRunWidth (currency-segment width computation, KNOWN-ISSUES #16) ---
        [Fact]
        public void SegmentRunWidth_Null_ReturnsZero()
        {
            Assert.Equal(0, ShoppingColumnMath.SegmentRunWidth(null, 20, 2, 6));
        }

        [Fact]
        public void SegmentRunWidth_Empty_ReturnsZero()
        {
            Assert.Equal(0, ShoppingColumnMath.SegmentRunWidth(new List<int>(), 20, 2, 6));
        }

        [Fact]
        public void SegmentRunWidth_SingleSegment_NoTrailingGap()
        {
            // 30 (text) + 2 (label-icon gap) + 20 (icon) = 52, no trailing
            // segmentGap since there is only one segment.
            var width = ShoppingColumnMath.SegmentRunWidth(new List<int> { 30 }, 20, 2, 6);

            Assert.Equal(52, width);
        }

        [Fact]
        public void SegmentRunWidth_TwoSegments_IncludesGapBetweenNotAfter()
        {
            // Each segment is textWidth + 2 + 20; a single segmentGap (6)
            // separates them, none trails after the last one.
            var width = ShoppingColumnMath.SegmentRunWidth(new List<int> { 30, 15 }, 20, 2, 6);

            Assert.Equal((30 + 2 + 20) + 6 + (15 + 2 + 20), width);
        }

        [Fact]
        public void SegmentRunWidth_UsesCallerSuppliedConstants_NotHardcoded()
        {
            // Different icon/gap constants than CraftingPlanView's own
            // (20/2/6) must change the result - proves the arithmetic is
            // fully parameterized, not silently defaulting to a baked-in
            // set of pixel values.
            var width = ShoppingColumnMath.SegmentRunWidth(new List<int> { 10 }, iconSize: 100, labelIconGap: 5, segmentGap: 1);

            Assert.Equal(10 + 5 + 100, width);
        }

        // --- SegmentRunWidth(int[], ...) overload
        // (the per-frame resize hot path passes SegmentLayoutHandle.TextWidths,
        // a concrete int[], to a non-allocating overload rather than the
        // IReadOnlyList<int> one above; both must agree on every result) ---
        [Fact]
        public void SegmentRunWidthArrayOverload_Null_ReturnsZero()
        {
            Assert.Equal(0, ShoppingColumnMath.SegmentRunWidth((int[])null, 20, 2, 6));
        }

        [Fact]
        public void SegmentRunWidthArrayOverload_Empty_ReturnsZero()
        {
            Assert.Equal(0, ShoppingColumnMath.SegmentRunWidth(new int[0], 20, 2, 6));
        }

        [Fact]
        public void SegmentRunWidthArrayOverload_SingleSegment_NoTrailingGap()
        {
            var width = ShoppingColumnMath.SegmentRunWidth(new int[] { 30 }, 20, 2, 6);

            Assert.Equal(52, width);
        }

        [Fact]
        public void SegmentRunWidthArrayOverload_MatchesListOverload_ForSameInput()
        {
            // Both overloads implement the same formula; a resize-tick call
            // through the int[] overload must never drift from a
            // build-time call through the IReadOnlyList<int> overload for
            // the same segment widths.
            var widths = new int[] { 30, 15, 42 };

            int arrayResult = ShoppingColumnMath.SegmentRunWidth(widths, 20, 2, 6);
            int listResult = ShoppingColumnMath.SegmentRunWidth(new List<int>(widths), 20, 2, 6);

            Assert.Equal(listResult, arrayResult);
        }

        // --- ComputeEdgesForPanel (the justified-width invariant) ---
        [Fact]
        public void ComputeEdgesForPanel_AnchorsTheTotalColumnToThePinnedPanelEdge()
        {
            var fromEdge = ShoppingColumnMath.ComputeEdges(
                PlanRelayoutMath.PinnedRightEdge(1000), maxEachWidth: 0, maxTotalWidth: 0);
            var fromPanel = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 1000, maxEachWidth: 0, maxTotalWidth: 0);

            Assert.Equal(fromEdge.TotalRightEdge, fromPanel.TotalRightEdge);
            Assert.Equal(fromEdge.EachRightEdge, fromPanel.EachRightEdge);
            Assert.Equal(fromEdge.SourceX, fromPanel.SourceX);
        }

        [Fact]
        public void ComputeEdgesForPanel_Packed_WiderPanel_MovesEveryColumnByTheFullIncrease()
        {
            // The packed stack hangs entirely off the pinned right edge, so
            // every column in it moves with that edge and the name column
            // absorbs the whole increase. Both widths are below the
            // distribution threshold.
            var narrow = ShoppingColumnMath.ComputeEdgesForPanel(700, 0, 0, 79, 96);
            var wide = ShoppingColumnMath.ComputeEdgesForPanel(800, 0, 0, 79, 96);

            Assert.False(narrow.Distributed);
            Assert.False(wide.Distributed);
            Assert.Equal(100, wide.TotalRightEdge - narrow.TotalRightEdge);
            Assert.Equal(100, wide.EachRightEdge - narrow.EachRightEdge);
            Assert.Equal(100, wide.SourceX - narrow.SourceX);
        }

        [Fact]
        public void ComputeEdgesForPanel_Distributed_SharesTheIncreaseAcrossTheTracks()
        {
            // Distributed, only Total still tracks the panel edge by the
            // whole increase - it is the pinned column. The Item column
            // takes no share at all: its reserve is its own longest name,
            // so all 400px of panel goes to the three data tracks. A band
            // centred on track i takes i whole tracks plus half of its own
            // track's growth.
            var narrow = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 79, 96);
            var wide = ShoppingColumnMath.ComputeEdgesForPanel(1800, 0, 0, 79, 96);

            Assert.True(narrow.Distributed);
            Assert.True(wide.Distributed);
            Assert.Equal(400, wide.TotalRightEdge - narrow.TotalRightEdge);
            Assert.Equal(200, wide.EachRightEdge - narrow.EachRightEdge);
            Assert.Equal(67, wide.SourceX - narrow.SourceX);
        }

        [Fact]
        public void ComputeEdgesForPanel_Distributed_PutsTheDataColumnsOneTrackApart()
        {
            // The law: the Item column takes its own reserve off the name
            // rule, then three equal tracks carry Source, Each and Total to
            // the pinned right edge - each of the first two bands CENTRED
            // on the track it owns. Doubled rather than halved so a
            // half-track stays an integer.
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 79, 96);
            int span = edges.TrackSpan;

            Assert.True(edges.Distributed);
            Assert.Equal(edges.NameX + edges.NameColumnWidth, edges.DataStartX);
            Assert.Equal(edges.TotalRightEdge - edges.DataStartX, span);

            int[] bandCentres = new[]
            {
                edges.SourceX + (edges.SourceBandWidth / 2),
                edges.EachRightEdge - (edges.EachBandWidth / 2),
            };
            for (int i = 0; i < bandCentres.Length; i++)
            {
                Assert.InRange(
                    (2 * (bandCentres[i] - edges.DataStartX))
                        - (((2 * i) + 1) * span / ShoppingColumnMath.DataColumnCount),
                    -2, 2);
            }

            // Total is the exception the law names: it pins to the panel
            // edge, which is where the track grid ends anyway, so the table
            // still reaches its own right margin.
            Assert.Equal(PlanRelayoutMath.PinnedRightEdge(1400), edges.TotalRightEdge);
        }

        [Fact]
        public void ComputeEdgesForPanel_Distributed_MovesTheSourceColumnWellRightOfThePackedStack()
        {
            // The defect: "the item name is stranded far left with dead
            // space before the first data column". Distribution is only a
            // fix if the first data column actually moves left-to-right
            // into that space, so this compares the two regimes at one
            // width rather than trusting the arithmetic.
            const int panelWidth = 1400;
            var distributed = ShoppingColumnMath.ComputeEdgesForPanel(panelWidth, 0, 0, 79, 96);
            var packed = ShoppingColumnMath.ComputeEdges(
                PlanRelayoutMath.PinnedRightEdge(panelWidth), 0, 0, 79, 900);

            Assert.True(distributed.Distributed);
            Assert.False(packed.Distributed);
            Assert.True(
                distributed.SourceX < packed.EachRightEdge - 300,
                "the Source column leaves the right-hand block entirely");
        }

        [Fact]
        public void ComputeEdgesForPanel_NarrowPanel_FallsBackToThePackedStack()
        {
            // Below the width a track can hold the widest band plus its gap
            // there is nothing to distribute, and spreading anyway would
            // overlap the columns. On a narrow panel a legible cramped
            // table beats an evenly spaced illegible one.
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(800, 0, 0, 79, 96);

            Assert.False(edges.Distributed);
            Assert.Equal(0, edges.TrackSpan);
            Assert.Equal(0, edges.DataStartX);
            Assert.Equal(edges.TotalRightEdge - 150 - ShoppingColumnMath.ColumnGap, edges.EachRightEdge);
            Assert.Equal(
                edges.EachRightEdge - 110 - ShoppingColumnMath.ColumnGap - 96, edges.SourceX);
        }

        [Fact]
        public void ComputeEdges_AWideBandDropsTheTableBackToThePackedStack()
        {
            // The reserve decides the regime: a Total band wide enough that
            // the equal tracks can no longer each hold one packs the table
            // even at a width that would otherwise distribute.
            var distributed = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 79, 96);
            var packed = ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 900, 79, 96);

            Assert.True(distributed.Distributed);
            Assert.False(packed.Distributed);
            Assert.Equal(900, packed.TotalBandWidth);
        }

        [Fact]
        public void ComputeEdgesForPanel_NameBudgetStopsAtTheSourceColumnAndGrowsWithThePanel()
        {
            // The Item column flexes up to the Source column's left edge,
            // measured exactly as CreateShoppingRow budgets it
            // (NameToQtyGap 12, no trailing band of its own). It takes only
            // half of the Source track's share of a wider panel now,
            // because its RESERVE is its own longest name and the rest goes
            // to the data columns. That is the trade the change makes.
            int narrow = PlanRelayoutMath.NameMaxWidthBeforeColumn(
                ShoppingColumnMath.ComputeEdgesForPanel(1400, 0, 0, 79, 96).SourceX,
                0, 12, AmountLedRowMath.NameX(79));
            int wide = PlanRelayoutMath.NameMaxWidthBeforeColumn(
                ShoppingColumnMath.ComputeEdgesForPanel(1800, 0, 0, 79, 96).SourceX,
                0, 12, AmountLedRowMath.NameX(79));

            Assert.Equal(67, wide - narrow);
            Assert.True(narrow > 200, $"name budget {narrow} at the module's own widths");
        }

        // Which column a click in the band sorts by. The failure pinned:
        // a boundary between the two WORDS puts the Source cell over the
        // right-hand end of the item NAMES.
        [Fact]
        public void HeaderCellBoundaries_Packed_SplitTheGapsBetweenTheColumns()
        {
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 800, maxEachWidth: 0, maxTotalWidth: 0,
                amountBandWidth: 79, sourceColumnWidth: 90);
            Assert.False(edges.Distributed);

            var boundaries = new int[4];
            ShoppingColumnMath.HeaderCellBoundaries(edges, 12, boundaries);

            // Amount ends where the icon column opens, a fixed x...
            Assert.Equal(AmountLedRowMath.HeaderSplitX(79), boundaries[0]);

            // ...Item ends just before the source badges begin...
            Assert.Equal(edges.SourceX - 6, boundaries[1]);

            // ...and every other is the middle of the columns' own gap.
            Assert.Equal(edges.SourceX + 90 + 10, boundaries[2]);
            Assert.Equal(edges.EachRightEdge + 10, boundaries[3]);

            for (int i = 1; i < boundaries.Length; i++)
            {
                Assert.True(boundaries[i] > boundaries[i - 1], "boundaries run left to right");
            }
        }

        [Fact]
        public void HeaderCellBoundaries_Distributed_AreTheTracksThemselves()
        {
            // Distributed there is no gap to split: each cell is its
            // column's whole track, so the partition is the track grid and
            // the Item cell reaches the Source column's own track.
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 1400, maxEachWidth: 0, maxTotalWidth: 0,
                amountBandWidth: 79, sourceColumnWidth: 90);
            Assert.True(edges.Distributed);

            var boundaries = new int[4];
            ShoppingColumnMath.HeaderCellBoundaries(edges, 12, boundaries);

            // Amount heads a fixed band, so its cell ends at a fixed x.
            Assert.Equal(AmountLedRowMath.HeaderSplitX(79), boundaries[0]);

            for (int i = 1; i < boundaries.Length; i++)
            {
                Assert.Equal(
                    edges.DataStartX
                        + (edges.TrackSpan * (i - 1) / ShoppingColumnMath.DataColumnCount),
                    boundaries[i]);
            }

            // The Item cell is everything between Amount's end and the
            // first track, which is exactly its own reserve.
            Assert.Equal(edges.DataStartX, boundaries[1]);

            // The Item cell still covers the whole name column: the name's
            // own budget stops before the boundary, not past it.
            int nameRightEdge = edges.NameX
                + PlanRelayoutMath.NameMaxWidthBeforeColumn(
                    edges.SourceX, 0, 12, edges.NameX);

            Assert.True(boundaries[1] < nameRightEdge);
            Assert.True(boundaries[1] < edges.SourceX);

            for (int i = 1; i < boundaries.Length; i++)
            {
                Assert.True(boundaries[i] > boundaries[i - 1], "boundaries run left to right");
            }
        }

        [Fact]
        public void HeaderCellBoundaries_IgnoreABufferItCannotFill()
        {
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(1000, 0, 0);

            ShoppingColumnMath.HeaderCellBoundaries(edges, 12, null);
            var tooShort = new int[2];
            ShoppingColumnMath.HeaderCellBoundaries(edges, 12, tooShort);

            Assert.Equal(new[] { 0, 0 }, tooShort);
        }

        [Fact]
        public void ComputeEdgesForPanel_VeryNarrowPanel_StillEndsOneMarginInFromTheEdge()
        {
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 500, maxEachWidth: 0, maxTotalWidth: 0);

            Assert.Equal(500 - PlanRelayoutMath.TableRightMargin, edges.TotalRightEdge);
        }

        [Fact]
        public void HeaderRooms_LeaveEveryDataHeaderFreeOfItsOwnBand()
        {
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 1000, maxEachWidth: 0, maxTotalWidth: 0,
                amountBandWidth: 79, sourceColumnWidth: 96);
            var rooms = ShoppingColumnMath.HeaderRoomsFor(edges, 12, 60, 40, 100);

            Assert.True(rooms.Source.Width > edges.SourceBandWidth);
            Assert.True(rooms.Each.Width > 40);

            // Adjacent rooms are a gutter apart and never overlap.
            Assert.Equal(
                JustifiedColumnTracks.HeaderGutter, rooms.Each.Left - rooms.Source.Right);
            Assert.Equal(
                JustifiedColumnTracks.HeaderGutter, rooms.Total.Left - rooms.Each.Right);

            // Total closes the table, so its own bound is the pinned edge.
            Assert.Equal(edges.TotalRightEdge, rooms.Total.Right);
        }

        [Fact]
        public void AmountHeader_CentresOnItsOwnBand_WhateverTheQuantitiesMeasure()
        {
            // A list every row of which is "1x": 12px of ink under a 60px
            // "Amount". The band is floored at the header block, so the
            // word takes the band's own centre line and the quantities
            // under it take the same one.
            const int band = 60;

            Assert.Equal(
                AmountLedRowMath.AmountX,
                AmountLedRowMath.AmountTextX(band, band));
            Assert.Equal(
                AmountLedRowMath.AmountTextX(band, band) + (band / 2),
                AmountLedRowMath.AmountTextX(band, 12) + (12 / 2));
        }

        // --- Each and Total headers take their columns' right edge ---
        [Fact]
        public void EachAndTotalHeaders_EndWhereTheirValuesEnd()
        {
            // The module's minimum window (1378px) leaves this panel. The
            // header block widths are the word plus the sort indicator's
            // slot, measured at the ColumnHeader tier, so they are given
            // here rather than measured - BitmapFont is Blish-bound.
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 1252, maxEachWidth: 40, maxTotalWidth: 200,
                amountBandWidth: 79, sourceColumnWidth: 96, maxNameWidth: 300);
            var rooms = ShoppingColumnMath.HeaderRoomsFor(edges, 12, 96, 40, 200);

            int each = JustifiedColumnTracks.RightAlignedOverContent(
                edges.EachRightEdge, 68, rooms.Each);
            int total = JustifiedColumnTracks.RightAlignedOverContent(
                edges.TotalRightEdge, 75, rooms.Total);

            Assert.Equal(edges.EachRightEdge, each + 68);
            Assert.Equal(edges.TotalRightEdge, total + 75);
        }

        [Fact]
        public void EachAndTotalHeaders_MoveRightOffTheirOldCentredSeats()
        {
            // What the change is worth at that width: the Each header hung
            // 14px past its own values, and the Total header stopped 63px
            // short of the edge its coin runs rule against.
            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                panelWidth: 1252, maxEachWidth: 40, maxTotalWidth: 200,
                amountBandWidth: 79, sourceColumnWidth: 96, maxNameWidth: 300);
            var rooms = ShoppingColumnMath.HeaderRoomsFor(edges, 12, 96, 40, 200);

            Assert.Equal(
                -14,
                JustifiedColumnTracks.RightAlignedOverContent(edges.EachRightEdge, 68, rooms.Each)
                    - JustifiedColumnTracks.CenteredOverContentRightAligned(
                        edges.EachRightEdge, 40, 68, rooms.Each));
            Assert.Equal(
                63,
                JustifiedColumnTracks.RightAlignedOverContent(edges.TotalRightEdge, 75, rooms.Total)
                    - JustifiedColumnTracks.CenteredOverContentRightAligned(
                        edges.TotalRightEdge, 200, 75, rooms.Total));
        }
    }
}
