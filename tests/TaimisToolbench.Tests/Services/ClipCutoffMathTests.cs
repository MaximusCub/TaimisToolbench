using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The viewport's hard top cutoff, executable: that re-asserting one
    /// absolute line at every container bounds the whole subtree's reach at a
    /// constant, and that the constant is that scale's own worst round trip
    /// (<see cref="ClipCutoffMath.SlipBudgetFor"/>), never worse than
    /// <see cref="ClipCutoffMath.SlipBudget"/>.
    /// <para>
    /// The propagation model is the same transcription of the decompiled
    /// Blish HUD 1.3.0 paint pipeline that ClipTopSlipSimulationTests runs -
    /// and it is the production one, so a divergence between what ships and
    /// what is proved here is not expressible.
    /// </para>
    /// </summary>
    public class ClipCutoffMathTests
    {
        /// <summary>The four GW2 UI Size scale factors Blish applies as its
        /// UIScaleMultiplier (Small / Normal / Large / Larger).</summary>
        private static readonly float[] Scales = { 0.81f, 0.897f, 1.0f, 1.103f };

        /// <summary>
        /// Absolute logical y values the viewport's top edge is swept over.
        /// The module window is draggable, so its screen y is not one number,
        /// and a cutoff that only held at some window positions would be no
        /// cutoff at all.
        /// </summary>
        private const int PhaseSweep = 4000;

        /// <summary>
        /// Deeper than any container chain the module can build, and
        /// deliberately far past it: the point of the clamp is that this
        /// number does not appear in the guarantee.
        /// </summary>
        private const int AbsurdDepth = 64;

        [Fact]
        public void OneContainerNeverLosesMoreThanTheBudget()
        {
            foreach (float scale in Scales)
            {
                for (int top = 0; top <= PhaseSweep; top++)
                {
                    int propagated = ClipCutoffMath.PropagateClipTop(top, scale);
                    Assert.True(
                        propagated <= top,
                        $"clip top fell at y={top}, scale {scale}");
                    Assert.True(
                        top - propagated <= ClipCutoffMath.SlipBudget,
                        $"one container lost {top - propagated}px at y={top}, scale {scale}");
                }
            }
        }

        [Fact]
        public void TheBudgetIsTightAtTheTwoSubUnityUiSizes()
        {
            // Falsifiable in both directions: a budget of 1 would be too
            // small at Small and Normal, and this is what fails if a future
            // edit trims it.
            Assert.Equal(2, WorstSingleContainerSlip(0.81f));
            Assert.Equal(2, WorstSingleContainerSlip(0.897f));
            Assert.Equal(1, WorstSingleContainerSlip(1.103f));
            Assert.Equal(0, WorstSingleContainerSlip(1.0f));
        }

        [Fact]
        public void ReAssertingTheCutoffBoundsTheReachRegardlessOfDepth()
        {
            // The whole claim. Every container clamps the edge it inherited
            // back to the cutoff before drawing, so the deepest a subtree can
            // reach is one container's loss below the line - at 64 levels
            // exactly as at 1.
            foreach (float scale in Scales)
            {
                for (int viewportTop = 0; viewportTop <= PhaseSweep; viewportTop++)
                {
                    int cutoff = ClipCutoffMath.CutoffTopFor(viewportTop, scale);
                    int worst = cutoff;
                    int edge = cutoff;
                    for (int level = 0; level < AbsurdDepth; level++)
                    {
                        edge = ClipCutoffMath.PropagateClipTop(edge, scale);
                        if (edge < worst)
                        {
                            worst = edge;
                        }

                        edge = ClipCutoffMath.ClampTop(edge, cutoff);
                    }

                    Assert.True(
                        worst >= viewportTop,
                        $"a clamped chain reached {worst}, above viewport top {viewportTop}, scale {scale}");
                }
            }
        }

        [Fact]
        public void WithoutTheReAssertionTheReachKeepsGrowing()
        {
            // The counterfactual that makes the test above mean something:
            // the same model, the same scales, no clamp - and the error is
            // unbounded in depth, which is why a fixed gap sized against a
            // "deepest realistic tree" was never a guarantee.
            int shallow = WorstUnclampedSlip(4, 0.81f);
            int deep = WorstUnclampedSlip(AbsurdDepth, 0.81f);

            Assert.True(shallow > ClipCutoffMath.SlipBudget);
            Assert.True(deep > shallow * 4);
        }

        [Fact]
        public void TheLargeUiSizeIsTheFalsifiableHalf()
        {
            // An integer scale makes both round trips exact, so a report that
            // content still overdraws the strip at GW2 UI Size "Large" would
            // mean the cause is something other than this propagation.
            Assert.Equal(0, WorstUnclampedSlip(AbsurdDepth, 1.0f));
        }

        /// <summary>
        /// The production budget is the measured one, not the four-size
        /// worst case: at UI Size Large the round trip is exact, and
        /// reserving 2px there cut scrolled rows 2px below every viewport
        /// top and 2px below every pinned sticky band, with nothing
        /// obliged to paint the strip.
        /// </summary>
        [Fact]
        public void TheLiveBudgetIsTheScalesOwnWorstCase()
        {
            foreach (float scale in Scales)
            {
                Assert.Equal(
                    WorstSingleContainerSlip(scale),
                    ClipCutoffMath.SlipBudgetFor(scale));
            }

            Assert.Equal(0, ClipCutoffMath.SlipBudgetFor(1.0f));
        }

        /// <summary>
        /// The cache is one slot, so the sequence a scale change produces
        /// has to give each scale its own answer rather than the first
        /// one asked for.
        /// </summary>
        [Fact]
        public void TheBudgetCacheAnswersEachScaleForItself()
        {
            foreach (float scale in Scales)
            {
                Assert.Equal(WorstSingleContainerSlip(scale), ClipCutoffMath.SlipBudgetFor(scale));
                Assert.Equal(WorstSingleContainerSlip(scale), ClipCutoffMath.SlipBudgetFor(scale));
            }

            foreach (float scale in Scales)
            {
                Assert.Equal(WorstSingleContainerSlip(scale), ClipCutoffMath.SlipBudgetFor(scale));
            }
        }

        /// <summary>
        /// A scale nobody has measured still gets an answer that bounds its
        /// own round trip, which is the whole point of measuring rather
        /// than tabulating - and a nonsense scale falls back to the
        /// four-size worst case rather than to zero.
        /// </summary>
        [Fact]
        public void AnUnlistedScaleIsMeasuredAndANonsenseOneFallsBack()
        {
            foreach (float scale in new[] { 0.75f, 1.25f, 1.5f, 2.0f })
            {
                int budget = ClipCutoffMath.SlipBudgetFor(scale);
                for (int top = 0; top <= PhaseSweep; top++)
                {
                    Assert.True(
                        top - ClipCutoffMath.PropagateClipTop(top, scale) <= budget,
                        $"budget {budget} too small at y={top}, scale {scale}");
                }
            }

            Assert.Equal(ClipCutoffMath.SlipBudget, ClipCutoffMath.SlipBudgetFor(0f));
            Assert.Equal(ClipCutoffMath.SlipBudget, ClipCutoffMath.SlipBudgetFor(-1f));
            Assert.Equal(ClipCutoffMath.SlipBudget, ClipCutoffMath.SlipBudgetFor(float.NaN));
        }

        /// <summary>
        /// The same claim for a sticky header's clip, which is a SIBLING of
        /// the scrolling viewport and so paints with no line in force
        /// (Views/Rendering/StickyHeaderHost.cs). A band part way through
        /// being pushed out by the end of its table sits above that clip, so
        /// the clip's own top edge is the only thing between a header label
        /// and the chrome above the viewport.
        /// </summary>
        [Fact]
        public void AClipOfItsOwnHoldsAPinnedBandAtTheClipsTopEdge()
        {
            foreach (float scale in Scales)
            {
                int worstUnclamped = 0;
                for (int clipTop = 0; clipTop <= PhaseSweep; clipTop++)
                {
                    // One round trip from the clip to the band, then one
                    // more from the band to the label's own ink.
                    int inherited = ClipCutoffMath.PropagateClipTop(clipTop, scale);
                    int unclamped = ClipCutoffMath.PropagateClipTop(inherited, scale);
                    if (clipTop - unclamped > worstUnclamped)
                    {
                        worstUnclamped = clipTop - unclamped;
                    }

                    int cutoff = ClipCutoffMath.CutoffTopFor(clipTop, scale);
                    int clamped = ClipCutoffMath.ClampTop(inherited, cutoff);
                    Assert.True(
                        ClipCutoffMath.PropagateClipTop(clamped, scale) >= clipTop,
                        $"a clamped band reached above clip top {clipTop}, scale {scale}");
                }

                Assert.Equal(WorstUnclampedSlip(2, scale), worstUnclamped);
            }
        }

        /// <summary>
        /// The size of the defect the clip's own line closes, so a change
        /// that reopened it would fail here with a number rather than a
        /// judgement.
        /// </summary>
        [Fact]
        public void AnUnclampedBandReachesThreePixelsAboveItsClipAtTheTwoSmallestUiSizes()
        {
            Assert.Equal(3, WorstUnclampedSlip(2, 0.81f));
            Assert.Equal(3, WorstUnclampedSlip(2, 0.897f));
            Assert.Equal(2, WorstUnclampedSlip(2, 1.103f));
            Assert.Equal(0, WorstUnclampedSlip(2, 1.0f));
        }

        private static int WorstSingleContainerSlip(float scale)
        {
            int worst = 0;
            for (int top = 0; top <= PhaseSweep; top++)
            {
                int slip = top - ClipCutoffMath.PropagateClipTop(top, scale);
                if (slip > worst)
                {
                    worst = slip;
                }
            }

            return worst;
        }

        private static int WorstUnclampedSlip(int containerDepth, float scale)
        {
            int worst = 0;
            for (int top = 0; top <= PhaseSweep; top++)
            {
                int edge = top;
                for (int level = 0; level < containerDepth; level++)
                {
                    edge = ClipCutoffMath.PropagateClipTop(edge, scale);
                }

                int slip = top - edge;
                if (slip > worst)
                {
                    worst = slip;
                }
            }

            return worst;
        }
    }
}
