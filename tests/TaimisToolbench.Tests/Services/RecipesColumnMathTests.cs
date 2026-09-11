using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class RecipesColumnMathTests
    {
        private const int NameX = 50;

        // The plan panel at the module's enforced window minimum. Derived,
        // not a literal, so a change to either constant moves these cases
        // with it.
        private static readonly int PanelWidthAtWindowMinimum =
            WindowSizing.TabPanelWidthFor(WindowSizing.MinWindowWidth);

        // Measured off the shipped Menomonia faces with the XNB reader in
        // tools/dump-font-codepoints.py. Body is Regular 16, the header
        // tier Bold 20, and the renderer floors each band at its own
        // header - which is what makes Status 66 (the word) rather than
        // 61 ("Missing!").
        //
        //   "Recipe: Relic of the Sunless"  214   longest name, Body
        //   "Armorsmith 400"                123   widest discipline, Body
        //   "5x Charm of Skill"             135   widest sheet cost, Body
        //   "Status"                         66   header, Bold 20
        private const int LongestRecipeName = 214;
        private const int DisciplineBand = 123;
        private const int SheetCostBand = 135;
        private const int StatusBand = 66;

        // A merchant phrase at the cap RecipesColumnMath.SoldByMaxWidth
        // allows, less than the cap so the band is the phrase rather than
        // the clamp.
        private const int SoldByBand = 180;

        // The row's own icon gutter, so the width figures below are the
        // ones the shipped table draws (RecipesSectionRenderer.NameX).
        private const int RowNameX = 58;

        [Fact]
        public void StatusPinsToThePanelEdge_AtEveryWidth()
        {
            // The whole point of the pinned model: the rightmost column's
            // right edge is a function of panel width alone. No pull-in,
            // no dependence on how wide the names happen to be.
            foreach (int panelWidth in new[] { 400, 1252, 3000 })
            {
                var edges = RecipesColumnMath.ComputeEdges(panelWidth, 90, 140, 120, 0, 200, NameX);
                Assert.Equal(PlanRelayoutMath.PinnedRightEdge(panelWidth), edges.StatusRightEdge);
            }
        }

        [Fact]
        public void AtTheWindowMinimum_TheThreeDataColumnsShareTheRowEqually()
        {
            // The report this table was rebuilt for: the Recipe column ran
            // two thirds of the row with nothing in it while Discipline and
            // Status touched each other at the right-hand edge.
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, 0,
                LongestRecipeName, RowNameX);

            Assert.True(edges.Distributed);
            Assert.Equal(3, edges.DataColumnCount);

            // Recipe reserves its longest name plus the headroom, and
            // nothing more: the space it used to hoard goes to the columns.
            Assert.Equal(RowNameX + LongestRecipeName + RecipesColumnMath.NameHeadroom, edges.DataStartX);
            Assert.Equal(948, edges.TrackSpan);

            // 948 over three columns is a 316px track each, and each band
            // centres in its own.
            Assert.Equal(392, edges.DisciplineX);
            Assert.Equal(837, edges.SheetCostRightEdge);
            Assert.Equal(1244, edges.StatusRightEdge);

            // Every neighbouring pair is hundreds of pixels apart, which is
            // the complaint answered.
            Assert.True(
                edges.StatusRightEdge - StatusBand - edges.SheetCostRightEdge > 100,
                "Sheet cost and Status are still crammed together");
            Assert.True(
                edges.SheetCostRightEdge - SheetCostBand - (edges.DisciplineX + DisciplineBand) > 100,
                "Discipline and Sheet cost are still crammed together");
        }

        [Fact]
        public void AtTheWindowMinimum_TheLongestNameStillFitsItsBudget()
        {
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, 0,
                LongestRecipeName, RowNameX);

            Assert.Equal(226, edges.NameMaxWidth);
            Assert.True(
                edges.NameMaxWidth >= LongestRecipeName,
                $"the longest name ellipsizes inside its own reserve: {edges.NameMaxWidth}");
        }

        [Fact]
        public void ALongerNameTakesItsReserveFromTheTracks_NotFromTheColumnsThemselves()
        {
            var shortNames = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, 0, 214, RowNameX);
            var longNames = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, 0, 414, RowNameX);

            Assert.Equal(shortNames.DataStartX + 200, longNames.DataStartX);
            Assert.Equal(shortNames.NameMaxWidth + 200, longNames.NameMaxWidth);
            Assert.Equal(shortNames.TrackSpan - 200, longNames.TrackSpan);

            // Every band still fits its narrower track, so no column gives
            // up legibility for a long name.
            Assert.True(longNames.Distributed);
            Assert.True(longNames.DisciplineX + DisciplineBand < longNames.SheetCostRightEdge - SheetCostBand);
        }

        [Fact]
        public void NoSheetCostColumn_GivesItsTrackToTheOthers()
        {
            // Nothing in the plan is missing a recipe with a sheet any
            // vendor sells, so the column is not reserved at all.
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, 0, 0, LongestRecipeName, RowNameX);

            Assert.False(edges.HasSheetCost);
            Assert.True(edges.HasDiscipline);
            Assert.Equal(2, edges.DataColumnCount);
            Assert.Equal(1244, edges.StatusRightEdge);

            // Two tracks of 474 rather than three of 316: Discipline
            // centres in the first.
            Assert.Equal(296 + ((474 - DisciplineBand) / 2), edges.DisciplineX);
        }

        [Fact]
        public void NoDisciplineColumn_PutsSheetCostOnTheFirstTrack()
        {
            // A mystic-forge-only recipe list has no disciplines at all.
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, 0, SheetCostBand, 0, LongestRecipeName, RowNameX);

            Assert.False(edges.HasDiscipline);
            Assert.True(edges.HasSheetCost);
            Assert.Equal(2, edges.DataColumnCount);
            Assert.Equal(296 + ((474 - SheetCostBand) / 2) + SheetCostBand, edges.SheetCostRightEdge);
        }

        [Fact]
        public void StatusAlone_StillTakesTheWholeSpanAsOneTrack()
        {
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, 0, 0, 0, LongestRecipeName, RowNameX);

            Assert.Equal(1, edges.DataColumnCount);
            Assert.True(edges.Distributed);
            Assert.Equal(1244, edges.StatusRightEdge);
            Assert.Equal(226, edges.NameMaxWidth);
        }

        [Fact]
        public void APanelTooNarrowToDistribute_PacksRightToLeftAsItAlwaysDid()
        {
            // Below the width that can hold the Recipe column's floor plus a
            // full track per data column, a legible cramped table beats an
            // evenly spaced illegible one - the same trade
            // ShoppingColumnMath makes.
            var edges = RecipesColumnMath.ComputeEdges(
                panelWidth: 620, statusColumnWidth: StatusBand,
                disciplineColumnWidth: DisciplineBand, sheetCostColumnWidth: SheetCostBand,
                soldByColumnWidth: 0, maxNameWidth: LongestRecipeName, nameX: RowNameX);

            Assert.False(edges.Distributed);
            Assert.Equal(0, edges.DataStartX);

            int pinned = PlanRelayoutMath.PinnedRightEdge(620);
            Assert.Equal(pinned, edges.StatusRightEdge);
            Assert.Equal(pinned - StatusBand - RecipesColumnMath.ColumnGap, edges.SheetCostRightEdge);
            Assert.Equal(
                edges.SheetCostRightEdge - SheetCostBand - RecipesColumnMath.ColumnGap - DisciplineBand,
                edges.DisciplineX);
            Assert.Equal(
                edges.DisciplineX - RecipesColumnMath.NameToDisciplineGap - RowNameX,
                edges.NameMaxWidth);
        }

        [Fact]
        public void PackedWithNoDisciplineColumn_StartsTheNameBudgetAtTheSheetCostBand()
        {
            // Narrower than the three-column case above: two full tracks
            // leave the Recipe column its floor at a width three do not.
            var edges = RecipesColumnMath.ComputeEdges(
                panelWidth: 500, statusColumnWidth: StatusBand, disciplineColumnWidth: 0,
                sheetCostColumnWidth: SheetCostBand, soldByColumnWidth: 0,
                maxNameWidth: LongestRecipeName, nameX: RowNameX);

            Assert.False(edges.Distributed);
            Assert.Equal(
                edges.SheetCostRightEdge - SheetCostBand
                    - RecipesColumnMath.NameToDisciplineGap - RowNameX,
                edges.NameMaxWidth);
        }

        [Fact]
        public void NarrowPanel_NameBudgetHoldsTheSharedFloor()
        {
            // PlanRelayoutMath.NameMaxWidthBeforeColumn's 20px floor is the
            // one thing standing between a very narrow panel and a
            // zero-or-negative ellipsis width.
            var edges = RecipesColumnMath.ComputeEdges(
                panelWidth: 200, statusColumnWidth: 90, disciplineColumnWidth: 140,
                sheetCostColumnWidth: 120, soldByColumnWidth: 0, maxNameWidth: 300,
                nameX: NameX);

            Assert.Equal(20, edges.NameMaxWidth);
        }

        [Fact]
        public void HeaderRooms_DisciplineHeaderOverrunsItsOwnBandWithoutReachingTheNames()
        {
            // "Discipline" at the header tier out-measures a "Chef 400", so
            // the band is the header's own width and centring in it pins
            // the word to the column's left rule. The room lets it overhang
            // as far as the recipe names' ellipsis budget allows.
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, 70, SheetCostBand, 0,
                LongestRecipeName, RowNameX);
            RecipesColumnMath.HeaderRooms(
                edges, 40, 100, 0, 61, out var discipline, out _, out _, out _);

            int x = JustifiedColumnTracks.CenteredOverContent(
                edges.DisciplineX, 40, 70, discipline);

            Assert.True(x < edges.DisciplineX, $"header at {x}, rule at {edges.DisciplineX}");
            Assert.True(
                x >= edges.DisciplineX - RecipesColumnMath.NameToDisciplineGap,
                $"header at {x} reached the recipe names");
        }

        [Fact]
        public void HeaderRooms_SheetCostSitsBetweenTheColumnsEitherSideOfIt()
        {
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, 0,
                LongestRecipeName, RowNameX);
            RecipesColumnMath.HeaderRooms(
                edges, DisciplineBand, SheetCostBand, 0, 61, out _, out var sheetCost, out _,
                out _);

            Assert.True(
                sheetCost.Left > edges.DisciplineX + DisciplineBand,
                "the Sheet cost header may reach back over the disciplines");
            Assert.True(
                sheetCost.Right < edges.StatusRightEdge - 61,
                "the Sheet cost header may reach forward over the status tags");
        }

        [Fact]
        public void HeaderRooms_StatusIsBoundedByTheTableEdge()
        {
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, 90, 70, SheetCostBand, 0, LongestRecipeName, RowNameX);
            RecipesColumnMath.HeaderRooms(
                edges, 40, SheetCostBand, 0, 60, out _, out _, out _, out var status);

            Assert.Equal(edges.StatusRightEdge, status.Right);

            // A status header wider than every tag under it has nowhere to
            // centre and right-aligns on that edge.
            Assert.Equal(
                edges.StatusRightEdge - 90,
                JustifiedColumnTracks.CenteredOverContentRightAligned(
                    edges.StatusRightEdge, 60, 90, status));
        }

        [Fact]
        public void HeaderRooms_WithNoDisciplineColumn_StatusStillClearsSheetCost()
        {
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, 0, SheetCostBand, 0,
                LongestRecipeName, RowNameX);
            RecipesColumnMath.HeaderRooms(
                edges, 0, SheetCostBand, 0, 61, out _, out _, out _, out var status);

            Assert.True(
                status.Left > edges.SheetCostRightEdge,
                "the Status header may reach back over the sheet costs");
        }

        /// <summary>
        /// The Sold By column is the fourth data column, and adding it
        /// re-tracks the other three rather than squeezing them: four equal
        /// tracks over the same span the three had.
        /// </summary>
        [Fact]
        public void AtTheWindowMinimum_TheFourDataColumnsShareTheRowEqually()
        {
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, SoldByBand,
                LongestRecipeName, RowNameX);

            Assert.True(edges.Distributed);
            Assert.Equal(4, edges.DataColumnCount);

            // The Recipe column keeps the same reserve it had with three
            // data columns - its longest name plus the headroom - so the
            // new column comes out of the tracks, not out of the names.
            Assert.Equal(RowNameX + LongestRecipeName + RecipesColumnMath.NameHeadroom, edges.DataStartX);
            Assert.Equal(948, edges.TrackSpan);
            Assert.Equal(226, edges.NameMaxWidth);

            // 948 over four columns is a 237px track each, and each band
            // centres in its own.
            Assert.Equal(353, edges.DisciplineX);
            Assert.Equal(719, edges.SheetCostRightEdge);
            Assert.Equal(798, edges.SoldByX);
            Assert.Equal(1244, edges.StatusRightEdge);
        }

        /// <summary>
        /// Every neighbouring pair still clears the one beside it once the
        /// fourth column is in the row, which is the property the equal
        /// tracks exist for.
        /// </summary>
        [Fact]
        public void AtTheWindowMinimum_NoTwoOfTheFourColumnsTouch()
        {
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, SoldByBand,
                LongestRecipeName, RowNameX);

            Assert.True(edges.DisciplineX + DisciplineBand < edges.SheetCostRightEdge - SheetCostBand);
            Assert.True(edges.SheetCostRightEdge < edges.SoldByX);
            Assert.True(edges.SoldByX + SoldByBand < edges.StatusRightEdge - StatusBand);
        }

        [Fact]
        public void NoSoldByColumn_GivesItsTrackBackToTheOtherThree()
        {
            var withSoldBy = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, SoldByBand,
                LongestRecipeName, RowNameX);
            var without = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, 0,
                LongestRecipeName, RowNameX);

            Assert.False(without.HasSoldBy);
            Assert.Equal(3, without.DataColumnCount);
            Assert.Equal(withSoldBy.TrackSpan, without.TrackSpan);
            Assert.True(without.SheetCostRightEdge > withSoldBy.SheetCostRightEdge);
        }

        [Fact]
        public void PackedFallback_StacksSoldByBetweenCostAndStatus()
        {
            var edges = RecipesColumnMath.ComputeEdges(
                panelWidth: 620, statusColumnWidth: StatusBand,
                disciplineColumnWidth: DisciplineBand, sheetCostColumnWidth: SheetCostBand,
                soldByColumnWidth: SoldByBand, maxNameWidth: LongestRecipeName, nameX: RowNameX);

            Assert.False(edges.Distributed);

            int pinned = PlanRelayoutMath.PinnedRightEdge(620);
            Assert.Equal(pinned, edges.StatusRightEdge);
            Assert.Equal(
                pinned - StatusBand - RecipesColumnMath.ColumnGap - SoldByBand, edges.SoldByX);
            Assert.Equal(edges.SoldByX - RecipesColumnMath.ColumnGap, edges.SheetCostRightEdge);
            Assert.Equal(
                edges.SheetCostRightEdge - SheetCostBand - RecipesColumnMath.ColumnGap - DisciplineBand,
                edges.DisciplineX);
        }

        /// <summary>
        /// The cap the renderer ellipsizes a merchant phrase to still
        /// leaves the table distributing at the module's narrowest panel.
        /// An uncapped phrase is what would drop it into the packed
        /// fallback and crush the Recipe column.
        /// </summary>
        [Fact]
        public void AtTheCap_TheTableStillDistributes()
        {
            var capped = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand,
                RecipesColumnMath.SoldByMaxWidth, LongestRecipeName, RowNameX);

            Assert.True(capped.Distributed);
            Assert.True(capped.SoldByX + RecipesColumnMath.SoldByMaxWidth <= capped.StatusRightEdge);
        }

        [Fact]
        public void HeaderRooms_SoldBySitsBetweenCostAndStatus()
        {
            var edges = RecipesColumnMath.ComputeEdges(
                PanelWidthAtWindowMinimum, StatusBand, DisciplineBand, SheetCostBand, SoldByBand,
                LongestRecipeName, RowNameX);
            RecipesColumnMath.HeaderRooms(
                edges, DisciplineBand, SheetCostBand, SoldByBand, 61,
                out _, out var sheetCost, out var soldBy, out _);

            Assert.True(
                soldBy.Left > edges.SheetCostRightEdge,
                "the Sold By header may reach back over the costs");
            Assert.True(
                soldBy.Right < edges.StatusRightEdge - 61,
                "the Sold By header may reach forward over the status tags");
            Assert.True(
                sheetCost.Right <= soldBy.Left,
                "the Cost and Sold By headers may share a pixel");
        }
    }
}
