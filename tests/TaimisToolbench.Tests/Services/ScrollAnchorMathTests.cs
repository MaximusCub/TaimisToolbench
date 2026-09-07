using System;
using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The arithmetic behind "the row under my cursor must not move when a
    /// re-solve reflows the sections above it" - reported in game as
    /// toggling IGNORE jarring the view because the Total Cost section
    /// gains or loses currency rows. Each test runs the real
    /// capture-then-restore sequence the view runs, over a candidate list
    /// standing in for the content panel's laid-out children.
    /// </summary>
    public class ScrollAnchorMathTests
    {
        private const int ViewportHeight = 400;

        // Total Cost header at 0, its band, the tree section header, and
        // three tree rows. summaryHeight is what a re-solve changes.
        private static List<ScrollAnchorCandidate> Layout(int summaryHeight)
        {
            int treeTop = summaryHeight;
            return new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate("section:Summary", 0, summaryHeight),
                new ScrollAnchorCandidate("section:RecipeTree", treeTop, 40),
                new ScrollAnchorCandidate("node:1", treeTop + 40, 30),
                new ScrollAnchorCandidate("node:2", treeTop + 70, 30),
                new ScrollAnchorCandidate("node:3", treeTop + 100, 30),
            };
        }

        private static int ContentHeight(List<ScrollAnchorCandidate> layout)
        {
            int bottom = 0;
            foreach (var c in layout)
            {
                if (c.Top + c.Height > bottom)
                {
                    bottom = c.Top + c.Height;
                }
            }

            return bottom + 2000; // plenty of rows below; never clamps
        }

        // --- The Total Cost table's own anchors ---
        //
        // Section header height is this fixture's own; nothing below turns
        // on its value. Every other height is the production constant the
        // renderer builds the table from, so a change to the table's shape
        // moves these layouts with it.
        private const int SectionHeaderHeight = 40;

        private static string WalletRowKey(int currencyId)
        {
            return SummarySectionLayoutMath.NonCoinRowAnchorKey(new PlanRowViewModel
            {
                RowType = PlanRowType.CurrencyCost,
                NonCoinCostKey = SummarySectionLayoutMath.WalletCurrencyCostKey(currencyId),
            });
        }

        /// <summary>
        /// The Total Cost section followed by the Recipe Tree, with one
        /// wallet-currency row per id in walletCurrencyIds and the anchors
        /// the renderer registers for them.
        /// </summary>
        private static List<ScrollAnchorCandidate> CostTableLayout(params int[] walletCurrencyIds)
        {
            bool hasTable = walletCurrencyIds.Length > 0;
            var candidates = new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate("section:Summary", 0, SectionHeaderHeight),
            };

            int y = SectionHeaderHeight + SummarySectionLayoutMath.CostBandHeight(hasTable);
            if (hasTable)
            {
                y += SummarySectionLayoutMath.CurrencyTableTopGap
                    + PlanContentHeightMath.ColumnHeaderRowHeight;
                candidates.Add(new ScrollAnchorCandidate(
                    SummarySectionLayoutMath.NonCoinGroupAnchorKey(isInventoryGroup: false),
                    y,
                    SummarySectionLayoutMath.NonCoinGroupHeadingHeight));
                y += SummarySectionLayoutMath.NonCoinGroupHeadingHeight;

                foreach (int currencyId in walletCurrencyIds)
                {
                    candidates.Add(new ScrollAnchorCandidate(
                        WalletRowKey(currencyId), y, PlanContentHeightMath.CurrencyRowHeight));
                    y += PlanContentHeightMath.CurrencyRowHeight;
                }
            }

            candidates.Add(new ScrollAnchorCandidate("section:RecipeTree", y, SectionHeaderHeight));
            candidates.Add(new ScrollAnchorCandidate(
                "node:1", y + SectionHeaderHeight, 30));
            return candidates;
        }

        private static string BarterRowKey(int itemId)
        {
            return SummarySectionLayoutMath.NonCoinRowAnchorKey(new PlanRowViewModel
            {
                RowType = PlanRowType.CurrencyCost,
                IsBarterItemCost = true,
                NonCoinCostKey = SummarySectionLayoutMath.BarterItemCostKey(itemId),
            });
        }

        private static int TopOf(List<ScrollAnchorCandidate> layout, string key)
        {
            int? top = ScrollAnchorMath.FindTop(layout, new ScrollAnchor(key, 0));
            Assert.True(top.HasValue);
            return top.Value;
        }

        [Fact]
        public void LineInsideTheCostTable_AnchorsToTheRowNotTheSectionHeader()
        {
            var layout = CostTableLayout(3);
            string rowKey = WalletRowKey(3);
            int rowTop = TopOf(layout, rowKey);

            Assert.True(ScrollAnchorMath.TryCapture(layout, rowTop + 10, out var anchor));

            Assert.Equal(rowKey, anchor.Key);
            Assert.Equal(rowTop, anchor.CapturedTop);
        }

        [Fact]
        public void CostTableGainsARowAbove_TheTreeBelowKeepsItsScreenPosition()
        {
            // The reported case: a decision toggle re-solves, the table
            // gains a currency row, and the tree row the click landed on
            // slides. The anchor line is the viewport top here, which is
            // where it sits whenever the cursor is off the panel.
            var before = CostTableLayout(3);
            int savedOffset = TopOf(before, WalletRowKey(3)) + 6;
            int anchorLine = ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, null);
            Assert.True(ScrollAnchorMath.TryCapture(before, anchorLine, out var anchor));
            Assert.Equal(WalletRowKey(3), anchor.Key);
            int treeScreenYBefore = TopOf(before, "node:1") - savedOffset;

            var after = CostTableLayout(2, 3);
            int restored = ScrollAnchorMath.RestoredOffset(
                savedOffset, anchor, ScrollAnchorMath.FindTop(after, anchor).Value,
                ContentHeight(after), ViewportHeight);

            Assert.Equal(savedOffset + PlanContentHeightMath.CurrencyRowHeight, restored);
            Assert.Equal(treeScreenYBefore, TopOf(after, "node:1") - restored);
        }

        [Fact]
        public void CostTableAppears_TheGroupHeadingAnchorAbsorbsTheWholeJump()
        {
            // Zero non-coin rows to one is the 141px case
            // (SummarySectionLayoutMathTests pins the number). Nothing in
            // the table exists to anchor to before it appears, so the line
            // has to be below the table for the anchor to hold - the tree
            // section header is what does it, and it still does.
            var before = CostTableLayout();
            int savedOffset = TopOf(before, "section:RecipeTree") + 4;
            Assert.True(ScrollAnchorMath.TryCapture(before, savedOffset, out var anchor));
            Assert.Equal("section:RecipeTree", anchor.Key);

            var after = CostTableLayout(3);
            int restored = ScrollAnchorMath.RestoredOffset(
                savedOffset, anchor, ScrollAnchorMath.FindTop(after, anchor).Value,
                ContentHeight(after), ViewportHeight);

            Assert.Equal(savedOffset + 141, restored);
            Assert.Equal(
                TopOf(before, "node:1") - savedOffset, TopOf(after, "node:1") - restored);
        }

        [Fact]
        public void WithoutTheTablesAnchors_TheSameLineHoldsTheSectionHeaderInstead()
        {
            // What the table's rows are registered FOR: strip them and the
            // lowest candidate at or above the line is the Summary header,
            // which sits above the rows whose count changed, so holding it
            // still lets the whole tree slide by the table's growth.
            var before = Coarse(CostTableLayout(3));
            int savedOffset = TopOf(CostTableLayout(3), WalletRowKey(3)) + 6;
            Assert.True(ScrollAnchorMath.TryCapture(before, savedOffset, out var anchor));
            Assert.Equal("section:Summary", anchor.Key);

            var after = Coarse(CostTableLayout(2, 3));
            int restored = ScrollAnchorMath.RestoredOffset(
                savedOffset, anchor, ScrollAnchorMath.FindTop(after, anchor).Value,
                ContentHeight(after), ViewportHeight);

            Assert.Equal(savedOffset, restored);
            Assert.Equal(
                PlanContentHeightMath.CurrencyRowHeight,
                (TopOf(after, "node:1") - restored) - (TopOf(before, "node:1") - savedOffset));
        }

        private static List<ScrollAnchorCandidate> Coarse(List<ScrollAnchorCandidate> layout)
        {
            var kept = new List<ScrollAnchorCandidate>();
            foreach (var candidate in layout)
            {
                if (candidate.Key.StartsWith("section:") || candidate.Key.StartsWith("node:"))
                {
                    kept.Add(candidate);
                }
            }

            return kept;
        }

        [Fact]
        public void EveryKeyInOneLayout_NamesExactlyOneCandidate()
        {
            // The cost table's keys go into the same candidate list as the
            // plan view's section and tree-row keys, and FindTop matches on
            // the whole key. Two candidates answering to one name would let
            // a restore hold the wrong control.
            var layout = CostTableLayout(1, 2, 3);
            layout.Add(new ScrollAnchorCandidate(BarterRowKey(1), 900, 20));
            layout.Add(new ScrollAnchorCandidate(BarterRowKey(3), 920, 20));
            layout.Add(new ScrollAnchorCandidate(
                SummarySectionLayoutMath.NonCoinGroupAnchorKey(isInventoryGroup: true), 940, 20));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in layout)
            {
                Assert.True(seen.Add(candidate.Key), "two candidates answer to " + candidate.Key);
                Assert.Equal(candidate.Top, TopOf(layout, candidate.Key));
            }
        }

        [Fact]
        public void TheCostTablesKeys_StayOutOfTheSectionAndTreeRowNamespaces()
        {
            // "section:" and "node:" are spelled as literals because the
            // class that registers them, Views/CraftingPlanView, is
            // Blish-bound and no test may reference it. The "Total Cost
            // table registers its scroll anchors" step in
            // .github/workflows/tests.yml fails if that class stops using
            // these two prefixes.
            foreach (string key in new[]
            {
                SummarySectionLayoutMath.NonCoinGroupAnchorKey(isInventoryGroup: false),
                SummarySectionLayoutMath.NonCoinGroupAnchorKey(isInventoryGroup: true),
                WalletRowKey(0),
                WalletRowKey(int.MaxValue),
                BarterRowKey(0),
                BarterRowKey(int.MaxValue),
            })
            {
                Assert.False(key.StartsWith("section:", StringComparison.Ordinal), key);
                Assert.False(key.StartsWith("node:", StringComparison.Ordinal), key);
            }
        }

        [Fact]
        public void SectionAboveGrows_RowUnderTheCursorKeepsItsScreenPosition()
        {
            var before = Layout(summaryHeight: 200);
            // Viewport top at 250, cursor 60px down it: content y 310,
            // which sits in node:3 (300..330 with summaryHeight 200).
            int savedOffset = 250;
            int anchorLine = ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, 60);

            Assert.True(ScrollAnchorMath.TryCapture(before, anchorLine, out var anchor));
            int screenYBefore = anchor.CapturedTop - savedOffset;

            // The re-solve adds two currency rows above everything.
            var after = Layout(summaryHeight: 260);
            int? newTop = ScrollAnchorMath.FindTop(after, anchor);
            Assert.True(newTop.HasValue);

            int restored = ScrollAnchorMath.RestoredOffset(
                savedOffset, anchor, newTop.Value, ContentHeight(after), ViewportHeight);

            Assert.Equal(310, savedOffset + 60);
            Assert.Equal(screenYBefore, newTop.Value - restored);
            // The content above grew by 60, so the viewport follows it by
            // exactly 60 rather than staying put and letting the row slide.
            Assert.Equal(310, restored);
        }

        [Fact]
        public void SectionAboveShrinks_ViewportFollowsUpward()
        {
            var before = Layout(summaryHeight: 260);
            int savedOffset = 300;
            int anchorLine = ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, 40);
            Assert.True(ScrollAnchorMath.TryCapture(before, anchorLine, out var anchor));

            var after = Layout(summaryHeight: 200);
            int? newTop = ScrollAnchorMath.FindTop(after, anchor);
            int restored = ScrollAnchorMath.RestoredOffset(
                savedOffset, anchor, newTop.Value, ContentHeight(after), ViewportHeight);

            Assert.Equal(240, restored);
            Assert.Equal(anchor.CapturedTop - savedOffset, newTop.Value - restored);
        }

        [Fact]
        public void NothingMoves_RestoresTheSameOffset()
        {
            var layout = Layout(summaryHeight: 200);
            int savedOffset = 250;
            int anchorLine = ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, 60);
            Assert.True(ScrollAnchorMath.TryCapture(layout, anchorLine, out var anchor));

            int restored = ScrollAnchorMath.RestoredOffset(
                savedOffset, anchor, ScrollAnchorMath.FindTop(layout, anchor).Value,
                ContentHeight(layout), ViewportHeight);

            Assert.Equal(savedOffset, restored);
        }

        [Fact]
        public void CursorLine_PicksTheRowUnderIt_NotTheViewportTop()
        {
            var layout = Layout(summaryHeight: 200);
            int savedOffset = 250;

            Assert.True(ScrollAnchorMath.TryCapture(
                layout, ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, 60), out var withCursor));
            Assert.True(ScrollAnchorMath.TryCapture(
                layout, ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, null), out var withoutCursor));

            // 310 lands in node:3 (300..330); the viewport top, 250, is
            // still up in node:1 (240..270). The cursor is what the user
            // is reading, so it decides.
            Assert.Equal("node:3", withCursor.Key);
            Assert.Equal("node:1", withoutCursor.Key);
        }

        [Fact]
        public void CursorOutsideTheViewport_FallsBackToTheViewportTop()
        {
            int savedOffset = 250;

            Assert.Equal(savedOffset, ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, null));
            Assert.Equal(savedOffset, ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, -5));
            Assert.Equal(savedOffset, ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, ViewportHeight));
            Assert.Equal(savedOffset + 399, ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, 399));
        }

        [Fact]
        public void NestedCandidates_PickTheMostSpecificOneOnTheLine()
        {
            // A row that starts exactly where its section header does: the
            // shorter (deeper) element wins, so the anchor tracks the row
            // rather than the whole section.
            var layout = new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate("section:RecipeTree", 100, 500),
                new ScrollAnchorCandidate("node:9", 100, 30),
            };

            Assert.True(ScrollAnchorMath.TryCapture(layout, 110, out var anchor));
            Assert.Equal("node:9", anchor.Key);
        }

        [Fact]
        public void AnchoredRowDisappears_CallerLearnsToFallBack()
        {
            // Ignoring a node drops its whole subtree; the row the user was
            // on can be one of them. FindTop returns null rather than
            // guessing, which is what puts the view back on plain offset
            // preservation.
            var before = Layout(summaryHeight: 200);
            Assert.True(ScrollAnchorMath.TryCapture(before, 310, out var anchor));
            Assert.Equal("node:3", anchor.Key);

            var after = new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate("section:Summary", 0, 200),
                new ScrollAnchorCandidate("section:RecipeTree", 200, 40),
                new ScrollAnchorCandidate("node:1", 240, 30),
            };

            Assert.Null(ScrollAnchorMath.FindTop(after, anchor));
        }

        [Fact]
        public void AnchoredRowIsCollapsedAway_RestoreHoldsTheRowItIsDrawnUnder()
        {
            // A decision toggle can leave the anchored row inside a
            // collapsed subtree. The row still exists and is still
            // registered, but it is drawn as part of the row it hangs
            // under, so that row reports its position. Holding that row
            // still is what keeps the view where the user left it; the
            // alternative is no anchor at all and the raw pre-mutate
            // offset.
            var before = Layout(summaryHeight: 200);
            int savedOffset = 250;
            int anchorLine = ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, 60);
            Assert.True(ScrollAnchorMath.TryCapture(before, anchorLine, out var anchor));
            Assert.Equal("node:3", anchor.Key);
            int screenYBefore = anchor.CapturedTop - savedOffset;

            // The re-solve drops 60px of currency rows above the tree and
            // collapses node:3 into node:1.
            var after = new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate("section:Summary", 0, 140),
                new ScrollAnchorCandidate("section:RecipeTree", 140, 40),
                new ScrollAnchorCandidate("node:1", 180, 30),
                new ScrollAnchorCandidate("node:2", 180, 30, hidden: true),
                new ScrollAnchorCandidate("node:3", 180, 30, hidden: true),
            };

            int? newTop = ScrollAnchorMath.FindTop(after, anchor);
            Assert.Equal(180, newTop);

            int restored = ScrollAnchorMath.RestoredOffset(
                savedOffset, anchor, newTop.Value, ContentHeight(after), ViewportHeight);

            Assert.Equal(130, restored);
            Assert.Equal(screenYBefore, TopOf(after, "node:1") - restored);
        }

        [Fact]
        public void HiddenCandidates_AreNeverChosenAsTheAnchor()
        {
            // A collapsed row reports the top of the row it is drawn
            // under, so it ties with that row and is never taller. The
            // tie-break prefers the shortest candidate, which would hand
            // the anchor to something nobody can see.
            var layout = new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate("section:RecipeTree", 100, 500),
                new ScrollAnchorCandidate("node:1", 100, 30),
                new ScrollAnchorCandidate("node:2", 100, 10, hidden: true),
            };

            Assert.True(ScrollAnchorMath.TryCapture(layout, 110, out var anchor));
            Assert.Equal("node:1", anchor.Key);
        }

        [Fact]
        public void EveryCandidateHidden_CapturesNothing()
        {
            // Nothing on the line is drawn, so there is nothing to hold
            // still and the caller stays on plain offset preservation.
            var layout = new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate("node:1", 100, 30, hidden: true),
                new ScrollAnchorCandidate("node:2", 100, 30, hidden: true),
            };

            Assert.False(ScrollAnchorMath.TryCapture(layout, 110, out var anchor));
            Assert.False(anchor.IsValid);
        }

        [Fact]
        public void LineAboveEveryCandidate_CapturesNothing()
        {
            var layout = new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate("section:Summary", 50, 200),
            };

            Assert.False(ScrollAnchorMath.TryCapture(layout, 20, out var anchor));
            Assert.False(anchor.IsValid);
        }

        [Fact]
        public void EmptyOrNullCandidates_CaptureNothingAndFindNothing()
        {
            Assert.False(ScrollAnchorMath.TryCapture(null, 100, out _));
            Assert.False(ScrollAnchorMath.TryCapture(new List<ScrollAnchorCandidate>(), 100, out _));
            Assert.Null(ScrollAnchorMath.FindTop(null, new ScrollAnchor("node:1", 0)));
            Assert.Null(ScrollAnchorMath.FindTop(Layout(200), default(ScrollAnchor)));
        }

        [Fact]
        public void KeylessCandidatesAreIgnored()
        {
            // A control the view could not key (nothing registers one
            // today) must never become the anchor: an anchor nobody can
            // re-find after the rebuild is worse than none.
            var layout = new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate("section:Summary", 0, 100),
                new ScrollAnchorCandidate(null, 90, 10),
                new ScrollAnchorCandidate("", 95, 10),
            };

            Assert.True(ScrollAnchorMath.TryCapture(layout, 99, out var anchor));
            Assert.Equal("section:Summary", anchor.Key);
        }

        [Fact]
        public void RestoreClampsToWhatTheShrunkContentCanScrollTo()
        {
            // Everything below the anchor collapsed away, so the content
            // no longer reaches the offset the anchor asks for.
            var anchor = new ScrollAnchor("node:2", 300);
            int restored = ScrollAnchorMath.RestoredOffset(
                savedOffset: 250, anchor: anchor, newAnchorTop: 300,
                contentHeight: 500, viewportHeight: ViewportHeight);

            Assert.Equal(100, restored);
        }

        [Fact]
        public void RestoreNeverGoesNegativeOrScrollsUnscrollableContent()
        {
            var anchor = new ScrollAnchor("node:2", 300);

            Assert.Equal(0, ScrollAnchorMath.RestoredOffset(
                savedOffset: 50, anchor: anchor, newAnchorTop: 100,
                contentHeight: 5000, viewportHeight: ViewportHeight));

            // Content shorter than the viewport cannot scroll at all.
            Assert.Equal(0, ScrollAnchorMath.RestoredOffset(
                savedOffset: 250, anchor: anchor, newAnchorTop: 300,
                contentHeight: 100, viewportHeight: ViewportHeight));
        }

        // --- Scroll offset zero: the clicked row ---
        //
        // The reported case, with the numbers measured from the report.
        // The content panel was 560px tall, the plan was scrolled to the
        // very top, and re-solving grew the Total Cost table by thirteen
        // currency rows. The offset was preserved exactly and the tree
        // still left the screen, because the rows appeared above it.
        private const int ReportedViewportHeight = 560;
        private const int ReportedRowsGained = 13;

        private static int[] WalletIds(int count)
        {
            var ids = new int[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = i + 1;
            }

            return ids;
        }

        /// <summary>
        /// The restore a capture-then-rebuild produces: the surviving
        /// anchor's offset, or the saved offset clamped to the new
        /// content, which is what the view does when nothing survived.
        /// </summary>
        private static int Restore(
            List<ScrollAnchorCandidate> after, ScrollAnchor anchor, int savedOffset, int viewportHeight)
        {
            if (!ScrollAnchorMath.TryFindSurvivingAnchor(after, anchor, out var survivor, out int newTop))
            {
                return ScrollAnchorMath.ClampOffset(savedOffset, ContentHeight(after), viewportHeight);
            }

            return ScrollAnchorMath.RestoredOffset(
                savedOffset, survivor, newTop, ContentHeight(after), viewportHeight);
        }

        [Fact]
        public void AtOffsetZero_TheClickedRowHoldsWhileThirteenRowsAppearAboveIt()
        {
            var before = CostTableLayout(WalletIds(1));
            var after = CostTableLayout(WalletIds(1 + ReportedRowsGained));

            // The table grows by 546px, which is the report's own figure.
            int grew = TopOf(after, "node:1") - TopOf(before, "node:1");
            Assert.Equal(ReportedRowsGained * PlanContentHeightMath.CurrencyRowHeight, grew);
            Assert.Equal(546, grew);

            // Offset zero, and the click names the row it landed on.
            Assert.True(ScrollAnchorMath.TryCaptureFor(
                before, "node:1", scrollOffset: 0, viewportHeight: ReportedViewportHeight,
                cursorYInViewport: null, topInset: 0, out var anchor));
            Assert.Equal("node:1", anchor.Key);

            int rowScreenYBefore = TopOf(before, "node:1");
            int restored = Restore(after, anchor, 0, ReportedViewportHeight);

            Assert.Equal(grew, restored);
            Assert.Equal(rowScreenYBefore, TopOf(after, "node:1") - restored);
        }

        [Fact]
        public void AtOffsetZero_WithoutAKey_TheCarveOutStillSuppresses()
        {
            var before = CostTableLayout(WalletIds(1));
            var after = CostTableLayout(WalletIds(1 + ReportedRowsGained));

            // The cursor sits squarely on the tree row. That alone must
            // not anchor: a background rebuild would then scroll the plan
            // out from under a user who did nothing.
            int cursorY = TopOf(before, "node:1") + 10;
            Assert.False(ScrollAnchorMath.TryCaptureFor(
                before, null, scrollOffset: 0, viewportHeight: ReportedViewportHeight,
                cursorYInViewport: cursorY, topInset: 0, out var anchor));
            Assert.False(anchor.IsValid);

            // The offset stays where it was, which is what the view did
            // for every rebuild at the top before the key existed. The
            // tree row then moves down by the whole 546px and leaves a
            // 560px viewport - the reported symptom, kept here because
            // this is the case the carve-out is meant to cover.
            int restored = Restore(after, anchor, 0, ReportedViewportHeight);
            Assert.Equal(0, restored);
            Assert.Equal(
                TopOf(before, "node:1") + (ReportedRowsGained * PlanContentHeightMath.CurrencyRowHeight),
                TopOf(after, "node:1") - restored);
            Assert.True(TopOf(after, "node:1") - restored >= ReportedViewportHeight);
        }

        [Fact]
        public void AboveOffsetZero_TheCursorTierStillAnchors()
        {
            var before = CostTableLayout(WalletIds(2));
            string rowKey = WalletRowKey(2);
            int savedOffset = 30;
            int cursorY = TopOf(before, rowKey) + 6 - savedOffset;

            Assert.True(ScrollAnchorMath.TryCaptureFor(
                before, null, savedOffset, ViewportHeight, cursorY, topInset: 0, out var anchor));

            Assert.Equal(rowKey, anchor.Key);
        }

        [Fact]
        public void AnExplicitKeyThatNamesNothing_FallsBackToTheOrdinaryTiers()
        {
            var before = CostTableLayout(WalletIds(2));

            // Above zero the cursor tier picks up the slack.
            Assert.True(ScrollAnchorMath.TryCaptureFor(
                before, "node:9999", scrollOffset: 30, viewportHeight: ViewportHeight,
                cursorYInViewport: null, topInset: 0, out var anchor));
            Assert.Equal("section:Summary", anchor.Key);

            // At zero there is no slack to pick up.
            Assert.False(ScrollAnchorMath.TryCaptureFor(
                before, "node:9999", scrollOffset: 0, viewportHeight: ViewportHeight,
                cursorYInViewport: null, topInset: 0, out _));
        }

        // --- The pinned sticky band inset ---
        [Fact]
        public void PinnedBandInset_MovesTheTopEdgeLineBelowTheBand()
        {
            Assert.Equal(100, ScrollAnchorMath.AnchorLine(
                scrollOffset: 100, viewportHeight: 400, cursorYInViewport: null));

            Assert.Equal(140, ScrollAnchorMath.AnchorLine(
                scrollOffset: 100, viewportHeight: 400, cursorYInViewport: null, topInset: 40));
        }

        [Fact]
        public void ACursorUnderThePinnedBand_IsTreatedAsNoCursor()
        {
            // The band is drawn over the content there, so the row under
            // the cursor is not the row the user can see.
            Assert.Equal(140, ScrollAnchorMath.AnchorLine(
                scrollOffset: 100, viewportHeight: 400, cursorYInViewport: 12, topInset: 40));

            Assert.Equal(160, ScrollAnchorMath.AnchorLine(
                scrollOffset: 100, viewportHeight: 400, cursorYInViewport: 60, topInset: 40));
        }

        [Fact]
        public void AnInsetTallerThanTheViewport_HoldsThePlainTopEdge()
        {
            Assert.Equal(100, ScrollAnchorMath.AnchorLine(
                scrollOffset: 100, viewportHeight: 400, cursorYInViewport: null, topInset: 400));

            Assert.Equal(100, ScrollAnchorMath.AnchorLine(
                scrollOffset: 100, viewportHeight: 400, cursorYInViewport: null, topInset: -5));
        }

        [Fact]
        public void InsetLine_AnchorsBelowTheBandNotOnTheRowItCovers()
        {
            var layout = CostTableLayout(WalletIds(3));
            string coveredRow = WalletRowKey(1);
            string visibleRow = WalletRowKey(2);
            int savedOffset = TopOf(layout, coveredRow);
            int inset = PlanContentHeightMath.CurrencyRowHeight;

            int line = ScrollAnchorMath.AnchorLine(savedOffset, ViewportHeight, null, inset);
            Assert.True(ScrollAnchorMath.TryCapture(layout, line, out var anchor));

            Assert.Equal(visibleRow, anchor.Key);
            Assert.NotEqual(coveredRow, anchor.Key);
        }

        // --- On-screen fallbacks ---
        [Fact]
        public void AnchoredRowDisappears_TheNearestOnScreenSurvivorTakesOver()
        {
            var before = CostTableLayout(WalletIds(3));
            string goneRow = WalletRowKey(2);
            int savedOffset = 0;

            Assert.True(ScrollAnchorMath.TryCaptureFor(
                before, goneRow, savedOffset, ReportedViewportHeight,
                cursorYInViewport: null, topInset: 0, out var anchor));
            Assert.Equal(goneRow, anchor.Key);
            Assert.NotEmpty(anchor.Fallbacks);

            // The re-solve dropped that currency and added four others.
            var after = CostTableLayout(1, 3, 4, 5, 6, 7);
            Assert.Null(ScrollAnchorMath.FindTop(after, anchor));

            Assert.True(ScrollAnchorMath.TryFindSurvivingAnchor(
                after, anchor, out var survivor, out int newTop));

            // Row 1 sits one row above the one that vanished and row 3 one
            // row below, so the walk reaches row 1 first and holds it.
            Assert.Equal(WalletRowKey(1), survivor.Key);

            int restored = ScrollAnchorMath.RestoredOffset(
                savedOffset, survivor, newTop, ContentHeight(after), ReportedViewportHeight);
            Assert.Equal(
                TopOf(before, WalletRowKey(1)), TopOf(after, WalletRowKey(1)) - restored);
        }

        [Fact]
        public void Fallbacks_LeaveOutWhateverWasOffScreenAtCapture()
        {
            var before = CostTableLayout(WalletIds(12));

            // A viewport that stops part-way down the table.
            int viewportHeight = TopOf(before, WalletRowKey(4));
            Assert.True(ScrollAnchorMath.TryCaptureFor(
                before, WalletRowKey(1), scrollOffset: 0, viewportHeight: viewportHeight,
                cursorYInViewport: null, topInset: 0, out var anchor));

            foreach (var fallback in anchor.Fallbacks)
            {
                int top = TopOf(before, fallback.Key);
                Assert.InRange(top, 0, viewportHeight - 1);
            }

            Assert.DoesNotContain(anchor.Fallbacks, f => f.Key == WalletRowKey(11));
            Assert.DoesNotContain(anchor.Fallbacks, f => f.Key == anchor.Key);
        }

        [Fact]
        public void Fallbacks_AreOrderedNearestToTheAnchorFirst()
        {
            var before = CostTableLayout(WalletIds(6));
            Assert.True(ScrollAnchorMath.TryCaptureFor(
                before, WalletRowKey(3), scrollOffset: 0, viewportHeight: ReportedViewportHeight,
                cursorYInViewport: null, topInset: 0, out var anchor));

            int previous = -1;
            foreach (var fallback in anchor.Fallbacks)
            {
                int distance = Math.Abs(TopOf(before, fallback.Key) - anchor.CapturedTop);
                Assert.True(distance >= previous, "fallbacks must not get nearer as the walk goes on");
                previous = distance;
            }
        }

        [Fact]
        public void ADefaultAnchorHasNoFallbacksAndSurvivesNothing()
        {
            var anchor = default(ScrollAnchor);

            Assert.NotNull(anchor.Fallbacks);
            Assert.Empty(anchor.Fallbacks);
            Assert.False(ScrollAnchorMath.TryFindSurvivingAnchor(
                CostTableLayout(WalletIds(2)), anchor, out _, out _));
        }

        // --- The shrink case: a deliberate landing, not an accidental one ---
        [Fact]
        public void NothingSurvives_TheSavedOffsetIsClampedNotHandedOnRaw()
        {
            // ScrollMath.RatioForOffset saturates at 1.0, so an offset
            // past the end of shorter content would land the viewport at
            // the very bottom. The clamp names the last real position
            // instead.
            Assert.Equal(440, ScrollAnchorMath.ClampOffset(
                offset: 5000, contentHeight: 1000, viewportHeight: 560));

            Assert.Equal(0, ScrollAnchorMath.ClampOffset(
                offset: 900, contentHeight: 400, viewportHeight: 560));

            Assert.Equal(0, ScrollAnchorMath.ClampOffset(
                offset: -20, contentHeight: 1000, viewportHeight: 560));

            Assert.Equal(300, ScrollAnchorMath.ClampOffset(
                offset: 300, contentHeight: 1000, viewportHeight: 560));
        }

        // --- The trailing spacer, for the shrink case ---
        //
        // Anchoring cannot hold a row still when the plan gets shorter
        // than the scroll position: the offset the row needs is past the
        // new legal maximum, so it clamps and the row slides down the
        // screen. A spacer sized to exactly the shortfall gives that
        // offset back. Screen y is top minus offset throughout.
        private static List<ScrollAnchorCandidate> OneRowAt(string key, int top)
        {
            return new List<ScrollAnchorCandidate>
            {
                new ScrollAnchorCandidate(key, top, 30),
            };
        }

        [Fact]
        public void PlanShrinksBelowTheAnchoredRow_TheSpacerCoversExactlyTheShortfall()
        {
            // The row was 100px down the viewport and has not moved in
            // content space; the content under it went from 2000px to
            // 1000px, so the offset it needs is 360px past the end.
            var after = OneRowAt("node:7", 900);
            var anchor = new ScrollAnchor("node:7", 900);

            var plan = ScrollAnchorMath.PlanRestore(
                after, anchor, savedOffset: 800, contentHeight: 1000, viewportHeight: 560);

            Assert.Equal(360, plan.TailSpacerHeight);
            Assert.Equal(800, plan.Offset);
            Assert.Equal(100, 900 - plan.Offset);

            // The spacer is the whole difference. Without it the offset
            // clamps and the row drops 360px down the screen.
            Assert.Equal(440, ScrollAnchorMath.ClampOffset(800, 1000, 560));
            Assert.Equal(460, 900 - 440);
        }

        [Fact]
        public void PaddedContentIsExactlyLongEnough_AndNoLonger()
        {
            var after = OneRowAt("node:7", 900);
            var anchor = new ScrollAnchor("node:7", 900);

            var plan = ScrollAnchorMath.PlanRestore(
                after, anchor, savedOffset: 800, contentHeight: 1000, viewportHeight: 560);

            int paddedMaxOffset = 1000 + plan.TailSpacerHeight - 560;
            Assert.Equal(plan.Offset, paddedMaxOffset);
        }

        [Fact]
        public void NothingSurvivedTheRebuild_ThereIsNoSpacerAtAll()
        {
            // A user parked in a previous spacer's blank space has nothing
            // to hold still. Sizing a spacer from the saved offset would
            // preserve a position in emptiness and go on doing it, so this
            // case clamps back onto content instead.
            var after = OneRowAt("node:99", 100);
            var anchor = new ScrollAnchor("node:7", 900);

            var plan = ScrollAnchorMath.PlanRestore(
                after, anchor, savedOffset: 800, contentHeight: 1000, viewportHeight: 560);

            Assert.Equal(0, plan.TailSpacerHeight);
            Assert.Equal(440, plan.Offset);
        }

        [Fact]
        public void ContentStillLongEnough_NoSpacerIsAdded()
        {
            var after = OneRowAt("node:7", 900);
            var anchor = new ScrollAnchor("node:7", 900);

            var plan = ScrollAnchorMath.PlanRestore(
                after, anchor, savedOffset: 800, contentHeight: 2000, viewportHeight: 560);

            Assert.Equal(0, plan.TailSpacerHeight);
            Assert.Equal(800, plan.Offset);
        }

        [Fact]
        public void TheReportedGrowthCase_NeedsNoSpacer()
        {
            // Part one's case, run through the same planner: the plan grew,
            // so there is nothing to make up and the scrollbar thumb is
            // untouched. This is what demand-sizing buys over a constant
            // pad of one viewport.
            var before = CostTableLayout(WalletIds(1));
            var after = CostTableLayout(WalletIds(1 + ReportedRowsGained));
            Assert.True(ScrollAnchorMath.TryCaptureFor(
                before, "node:1", scrollOffset: 0, viewportHeight: ReportedViewportHeight,
                cursorYInViewport: null, topInset: 0, out var anchor));

            var plan = ScrollAnchorMath.PlanRestore(
                after, anchor, 0, ContentHeight(after), ReportedViewportHeight);

            Assert.Equal(0, plan.TailSpacerHeight);
            Assert.Equal(546, plan.Offset);
        }

        [Fact]
        public void ASpacerIsNeverSizedForAnOffsetOfZeroOrLess()
        {
            Assert.Equal(0, ScrollAnchorMath.TailSpacerHeight(
                targetOffset: 0, contentHeight: 100, viewportHeight: 560));

            Assert.Equal(0, ScrollAnchorMath.TailSpacerHeight(
                targetOffset: -300, contentHeight: 100, viewportHeight: 560));

            // A viewport with no height yet measures nothing worth padding.
            Assert.Equal(0, ScrollAnchorMath.TailSpacerHeight(
                targetOffset: 800, contentHeight: 1000, viewportHeight: 0));
        }

        [Fact]
        public void AnUpwardAnchorNeedsNoSpacer()
        {
            // The row moved up as much as the content shrank, which is the
            // ordinary shrink-above case anchoring already handles.
            var after = OneRowAt("node:7", 400);
            var anchor = new ScrollAnchor("node:7", 900);

            var plan = ScrollAnchorMath.PlanRestore(
                after, anchor, savedOffset: 800, contentHeight: 1000, viewportHeight: 560);

            Assert.Equal(0, plan.TailSpacerHeight);
            Assert.Equal(300, plan.Offset);
            Assert.Equal(100, 400 - plan.Offset);
        }
    }
}
