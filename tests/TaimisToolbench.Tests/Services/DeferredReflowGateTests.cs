using System;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class DeferredReflowGateTests
    {
        // CraftingPlanView.StripReflowStallMs and ResizeDebounceMs. The
        // gate takes both as constructor arguments, so these are the values
        // under test and not copies of a rule.
        private const int StallMs = 5000;
        private const int SettleMs = 150;

        private static DeferredReflowGate NewGate()
        {
            return new DeferredReflowGate(SettleMs, StallMs);
        }

        // 28 is Views/Rendering/UiMetrics.ButtonHeight, which the strip
        // passes into the grid - see ItemInputGridLayoutTests.
        private const int ButtonSize = 28;

        private static readonly DateTime T0 = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void AWidthChangeIsNotAppliedWhileTheDragIsStillRunning()
        {
            var gate = NewGate();
            gate.Reset(1000);

            gate.Observe(1200, T0, dragActive: true);

            int width;
            Assert.False(gate.TryTake(T0.AddMilliseconds(149), dragActive: true, width: out width));
            Assert.Equal(1000, width);
            Assert.Equal(1000, gate.AppliedWidth);
            Assert.True(gate.IsPending);
        }

        [Fact]
        public void ABurstOfWidthsCollapsesToASingleTakeAtTheLastOne()
        {
            var gate = NewGate();
            gate.Reset(1000);

            for (int i = 1; i <= 10; i++)
            {
                gate.Observe(1000 + (i * 40), T0.AddMilliseconds(i * 30), dragActive: true);
                int early;
                Assert.False(gate.TryTake(T0.AddMilliseconds(i * 30), dragActive: true, width: out early));
            }

            int width;
            Assert.True(gate.TryTake(T0.AddMilliseconds(400), dragActive: false, width: out width));
            Assert.Equal(1400, width);
            Assert.Equal(1400, gate.AppliedWidth);

            int again;
            Assert.False(gate.TryTake(T0.AddMilliseconds(1000), dragActive: false, width: out again));
            Assert.False(gate.IsPending);
        }

        /// <summary>
        /// The reported defect. A hand steady for a moment is ordinary
        /// inside a drag, and re-seating the strip on that pause is what
        /// the drag felt clunky for. The gate is offered the quiet interval
        /// over and over and takes none of them.
        /// </summary>
        [Fact]
        public void APauseInTheMiddleOfADragDoesNotReleaseTheReflow()
        {
            var gate = NewGate();
            gate.Reset(1000);

            gate.Observe(1200, T0, dragActive: true);

            int width;
            for (int elapsed = SettleMs; elapsed < StallMs; elapsed += SettleMs)
            {
                Assert.False(gate.TryTake(T0.AddMilliseconds(elapsed), dragActive: true, width: out width));
                Assert.True(gate.IsPending);
                Assert.Equal(1000, gate.AppliedWidth);
            }

            // The drag resumes and then ends: one reflow, at the last width.
            gate.Observe(1300, T0.AddMilliseconds(4900), dragActive: true);
            Assert.True(gate.TryTake(T0.AddMilliseconds(4920), dragActive: false, width: out width));
            Assert.Equal(1300, width);
        }

        /// <summary>
        /// A drag flag that outlives its drag must not strand the strip at
        /// its pre-drag width for the session.
        /// </summary>
        [Fact]
        public void AStuckDragReleasesTheReflowAtTheStallCeiling()
        {
            var gate = NewGate();
            gate.Reset(1000);

            gate.Observe(1200, T0, dragActive: true);

            int width;
            Assert.False(gate.TryTake(T0.AddMilliseconds(StallMs - 1), dragActive: true, width: out width));
            Assert.True(gate.TryTake(T0.AddMilliseconds(StallMs), dragActive: true, width: out width));
            Assert.Equal(1200, width);
            Assert.False(gate.IsPending);
        }

        /// <summary>
        /// A resize no drag drove - a resolution change, a fullscreen
        /// toggle, a size restored from settings - has no release to wait
        /// for. It arrives as a burst of ticks, one per frame, and the
        /// quiet interval collapses that burst to a single reflow at the
        /// last width. Without it the strip rebuilds its rows once per
        /// frame of the burst.
        /// </summary>
        [Fact]
        public void APointerlessResizeBurstStillCollapsesOnTheQuietInterval()
        {
            const int frames = 12;
            const int frameMs = 16;

            var gate = NewGate();
            gate.Reset(1000);

            int width;
            for (int i = 1; i <= frames; i++)
            {
                var now = T0.AddMilliseconds(i * frameMs);
                gate.Observe(1000 + (i * 20), now, dragActive: false);
                Assert.False(gate.TryTake(now, dragActive: false, width: out width));
            }

            var lastTick = T0.AddMilliseconds(frames * frameMs);
            Assert.False(gate.TryTake(lastTick.AddMilliseconds(SettleMs - 1), dragActive: false, width: out width));
            Assert.True(gate.TryTake(lastTick.AddMilliseconds(SettleMs), dragActive: false, width: out width));
            Assert.Equal(1000 + (frames * 20), width);
            Assert.False(gate.IsPending);
        }

        /// <summary>
        /// The two situations the gate has to tell apart, in one run. A
        /// held drag ignores the quiet interval and releases on the frame
        /// the drag ends. A burst no drag drove, on the same gate
        /// afterwards, releases on the quiet interval and not before.
        /// </summary>
        [Fact]
        public void AHeldDragReleasesOnReleaseAndAPointerlessBurstCollapses()
        {
            var gate = NewGate();
            gate.Reset(1000);

            int width;
            for (int i = 1; i <= 20; i++)
            {
                var now = T0.AddMilliseconds(i * 200);
                gate.Observe(1000 + (i * 15), now, dragActive: true);

                // Each tick is more than the quiet interval after the last,
                // so a gate that coalesced on time alone would release here.
                Assert.False(gate.TryTake(now, dragActive: true, width: out width));
                Assert.Equal(1000, gate.AppliedWidth);
            }

            var release = T0.AddMilliseconds(20 * 200);
            Assert.True(gate.TryTake(release, dragActive: false, width: out width));
            Assert.Equal(1300, width);

            var burstStart = release.AddMilliseconds(1000);
            for (int i = 1; i <= 8; i++)
            {
                var now = burstStart.AddMilliseconds(i * 16);
                gate.Observe(1300 + (i * 30), now, dragActive: false);
                Assert.False(gate.TryTake(now, dragActive: false, width: out width));
                Assert.Equal(1300, gate.AppliedWidth);
            }

            var lastTick = burstStart.AddMilliseconds(8 * 16);
            Assert.True(gate.TryTake(lastTick.AddMilliseconds(SettleMs), dragActive: false, width: out width));
            Assert.Equal(1540, width);
        }

        [Fact]
        public void EndingTheDragAppliesTheWidthOnTheSameTake()
        {
            var gate = NewGate();
            gate.Reset(1000);
            gate.Observe(1180, T0, dragActive: true);

            int width;
            Assert.True(gate.TryTake(T0.AddMilliseconds(1), dragActive: false, width: out width));
            Assert.Equal(1180, width);
            Assert.Equal(1180, gate.AppliedWidth);
            Assert.False(gate.IsPending);
        }

        [Fact]
        public void ADragThatEndsWhereItStartedLeavesNothingToApply()
        {
            var gate = NewGate();
            gate.Reset(1000);

            gate.Observe(1400, T0, dragActive: true);
            Assert.True(gate.IsPending);

            gate.Observe(1000, T0.AddMilliseconds(40), dragActive: true);

            int width;
            Assert.False(gate.IsPending);
            Assert.False(gate.TryTake(T0.AddMilliseconds(400), dragActive: false, width: out width));
            Assert.Equal(1000, gate.AppliedWidth);
        }

        [Fact]
        public void AnIdleGateHasNothingToTake()
        {
            var gate = NewGate();
            gate.Reset(1000);

            int width;
            Assert.False(gate.TryTake(T0, dragActive: false, width: out width));
            Assert.Equal(1000, width);
        }

        [Fact]
        public void CancellingKeepsTheWidthTheStripIsAlreadyLaidOutAt()
        {
            var gate = NewGate();
            gate.Reset(1000);
            gate.Observe(1400, T0, dragActive: true);

            gate.CancelPending();

            int width;
            Assert.False(gate.IsPending);
            Assert.Equal(1000, gate.AppliedWidth);
            Assert.False(gate.TryTake(T0.AddMilliseconds(400), dragActive: false, width: out width));
        }

        [Fact]
        public void RebuildingTheStripAdoptsTheNewWidthAndDropsTheDeferredOne()
        {
            var gate = NewGate();
            gate.Reset(1000);
            gate.Observe(1400, T0, dragActive: true);

            gate.Reset(900);

            int width;
            Assert.False(gate.IsPending);
            Assert.Equal(900, gate.AppliedWidth);
            Assert.False(gate.TryTake(T0.AddMilliseconds(400), dragActive: false, width: out width));
        }

        // ---- The defect the gate exists for ----

        /// <summary>
        /// The reported symptom: dragging the window repacks the item input
        /// strip every time the width crosses a column-count boundary, and
        /// stretches every cell in between. Driving the same drag through
        /// the gate, the row count the strip is laid out for holds still
        /// for the whole drag and changes exactly once, at the end. The
        /// drag runs long enough to pass the stall ceiling, so the sweep
        /// also proves a moving grip never reaches it.
        /// </summary>
        [Fact]
        public void AWidthSweepAcrossColumnBoundariesRepacksTheStripOnceInsteadOfPerBoundary()
        {
            const int items = 5;
            const int start = 500;
            const int end = 1800;

            var gate = NewGate();
            gate.Reset(start);

            int startRows = ItemInputGridLayout.RowCount(items, start, ButtonSize);
            int liveRows = startRows;
            int gatedRows = startRows;
            int liveRepacks = 0;
            int gatedRepacks = 0;
            var last = T0;

            for (int w = start; w <= end; w += 4)
            {
                var now = T0.AddMilliseconds((w - start) / 4 * 40);
                last = now;
                gate.Observe(w, now, dragActive: true);

                int live = ItemInputGridLayout.RowCount(items, w, ButtonSize);
                if (live != liveRows)
                {
                    liveRows = live;
                    liveRepacks++;
                }

                int gated = ItemInputGridLayout.RowCount(items, gate.AppliedWidth, ButtonSize);
                if (gated != gatedRows)
                {
                    gatedRows = gated;
                    gatedRepacks++;
                }

                int midDrag;
                Assert.False(gate.TryTake(now, dragActive: true, width: out midDrag));
                Assert.Equal(startRows, gated);
            }

            Assert.True(
                (last - T0).TotalMilliseconds > StallMs,
                "the sweep has to outlast the stall ceiling to prove a moving grip never trips it");

            int settledWidth;
            Assert.True(gate.TryTake(last, dragActive: false, width: out settledWidth));
            Assert.Equal(end, settledWidth);

            if (ItemInputGridLayout.RowCount(items, settledWidth, ButtonSize) != gatedRows)
            {
                gatedRepacks++;
            }

            Assert.True(
                liveRepacks >= 2,
                $"the sweep has to cross at least two boundaries to be the reported drag; crossed {liveRepacks}");
            Assert.Equal(1, gatedRepacks);
            Assert.Equal(
                ItemInputGridLayout.RowCount(items, end, ButtonSize),
                ItemInputGridLayout.RowCount(items, gate.AppliedWidth, ButtonSize));
        }

        /// <summary>
        /// The strip's cells and the top of the scrolling viewport are both
        /// derived from the gate's applied width, so they have to move on
        /// the same take. If the viewport height moved on the live width
        /// instead, it would open a gap or an overlap against the separator
        /// for the length of the drag.
        /// </summary>
        [Fact]
        public void TheReservedTopRegionHeightMovesOnTheSameTakeAsTheRowCount()
        {
            const int items = 5;
            const int start = 500;
            const int end = 1800;

            var gate = NewGate();
            gate.Reset(start);

            int startRows = ItemInputGridLayout.RowCount(items, start, ButtonSize);
            int startHeight = TopRegionLayoutMath.Compute(startRows, false).TopRegionHeight;

            for (int w = start; w <= end; w += 25)
            {
                gate.Observe(w, T0, dragActive: true);

                int midDrag;
                Assert.False(gate.TryTake(T0, dragActive: true, width: out midDrag));

                int rows = ItemInputGridLayout.RowCount(items, gate.AppliedWidth, ButtonSize);
                Assert.Equal(startRows, rows);
                Assert.Equal(startHeight, TopRegionLayoutMath.Compute(rows, false).TopRegionHeight);
            }

            int settledWidth;
            Assert.True(gate.TryTake(T0, dragActive: false, width: out settledWidth));

            int settledRows = ItemInputGridLayout.RowCount(items, gate.AppliedWidth, ButtonSize);
            Assert.NotEqual(startRows, settledRows);
            Assert.Equal(
                TopRegionLayoutMath.Compute(settledRows, false).TopRegionHeight,
                TopRegionLayoutMath.Compute(
                    ItemInputGridLayout.RowCount(items, settledWidth, ButtonSize), false).TopRegionHeight);
        }
    }
}
