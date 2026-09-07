using System;
using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The divider-vanishing immunity proof (KNOWN-ISSUES #23), executable.
    /// What is under test is the production geometry - every
    /// (rowHeight, bottomClearance) pair the module hands to
    /// LabelHelpers.CreateRowDivider, most of them derived in
    /// PlanContentHeightMath from ItemIconTiers.BagSidebarIconSize - swept
    /// through a model of the paint pipeline that decides whether a 2px
    /// divider quad reaches the screen.
    ///
    /// The model is transcribed from the decompiled Blish HUD 1.3.0 binary,
    /// and must reproduce the historically measured vulnerable and immune
    /// heights before it is trusted with the shipped geometry. Both the
    /// transcription and that validation requirement are set out in
    /// docs/ARCHITECTURE.md section V.26.
    /// </summary>
    public class RowDividerScissorSimulationTests
    {
        /// <summary>The four GW2 UI Size scale factors Blish applies as
        /// its UIScaleMultiplier (Small / Normal / Large / Larger).</summary>
        private static readonly float[] UiScales = { 0.81f, 0.897f, 1.0f, 1.103f };

        /// <summary>
        /// Scroll phases swept per (rowHeight, scale) pair - the row's
        /// absolute logical Y. 5000, matching the M36b simulation; the
        /// float32 phase pattern repeats well inside that for every scale
        /// above (e.g. 0.897 cycles per 1000 logical pixels).
        /// </summary>
        private const int ScrollPhases = 5000;

        private const int DividerHeight = PlanContentHeightMath.RowDividerHeight;

        // --- The model (see class doc for the decompiled sources) ---

        /// <summary>RectangleExtension.ScaleBy, one axis: floor the scaled
        /// origin, ceil the scaled extent. The multiply happens at float32
        /// precision before the floor/ceil, exactly as the binary does.</summary>
        private static void ScaleInterval(int top, int height, float scale, out int scaledTop, out int scaledHeight)
        {
            float topProduct = top * scale;
            float heightProduct = height * scale;
            scaledTop = (int)Math.Floor(topProduct);
            scaledHeight = (int)Math.Ceiling(heightProduct);
        }

        /// <summary>
        /// True when the divider of a row at absolute logical y
        /// <paramref name="rowY"/> rasterizes ZERO visible physical
        /// scanlines at UI scale <paramref name="uiScale"/>.
        /// </summary>
        private static bool DividerVanishes(int rowHeight, int bottomClearance, float uiScale, int rowY)
        {
            // Control.Draw for the row panel: physical scissor from the
            // row's own logical bounds (the ancestors' scissor is generous
            // for a row in mid-viewport, which is the regime the proof is
            // about - a divider clipped AT the viewport edge is scrolling,
            // not vanishing).
            ScaleInterval(rowY, rowHeight, uiScale, out int physTop, out int physHeight);

            // Container.Paint: the physical scissor unscaled back to
            // logical space for the children - the second round trip.
            float inverseScale = 1f / uiScale;
            ScaleInterval(physTop, physHeight, inverseScale, out int logicalTop, out int logicalHeight);
            int logicalBottom = logicalTop + logicalHeight;

            // The divider's logical interval: CreateRowDivider places it at
            // rowHeight - DividerHeight - bottomClearance, DividerHeight tall.
            int dividerTop = rowY + rowHeight - DividerHeight - bottomClearance;
            int dividerBottom = dividerTop + DividerHeight;

            // Control.Draw for the divider: intersect the propagated
            // logical scissor with the divider's own bounds, re-scale.
            int clippedTop = Math.Max(logicalTop, dividerTop);
            int clippedBottom = Math.Min(logicalBottom, dividerBottom);
            if (clippedBottom <= clippedTop)
            {
                return true;
            }

            ScaleInterval(clippedTop, clippedBottom - clippedTop, uiScale, out int scissorTop, out int scissorHeight);
            int scissorBottom = scissorTop + scissorHeight;

            // Rasterization: the quad's continuous physical interval under
            // the UI-scale transform covers a scanline only when the
            // scanline's center lies inside it; the scissor test then
            // drops scanlines outside the physical scissor.
            float quadTop = dividerTop * uiScale;
            float quadBottom = dividerBottom * uiScale;
            for (int scanline = (int)Math.Floor(quadTop); scanline < quadBottom; scanline++)
            {
                float center = scanline + 0.5f;
                if (quadTop <= center && center < quadBottom
                    && scissorTop <= scanline && scanline < scissorBottom)
                {
                    return false;
                }
            }

            return true;
        }

        private static int VanishCount(int rowHeight, int bottomClearance, float uiScale)
        {
            int count = 0;
            for (int rowY = 0; rowY < ScrollPhases; rowY++)
            {
                if (DividerVanishes(rowHeight, bottomClearance, uiScale, rowY))
                {
                    count++;
                }
            }

            return count;
        }

        // --- Model validation against the M36b record ---
        [Fact]
        public void ModelReproducesTheM36bVulnerableHeights()
        {
            // 44px and 32px rows at clearance 0, "Normal" (0.897): the
            // published ~10.2% vanish rate (514/5000 here; the record
            // rounded a same-order sweep). 44 was also vulnerable at
            // "Small" (0.81) - the scale of the M36b session's own live
            // pixel-scans, where the user-visible misses were captured.
            Assert.Equal(514, VanishCount(44, 0, 0.897f));
            Assert.Equal(514, VanishCount(32, 0, 0.897f));
            Assert.True(VanishCount(44, 0, 0.81f) > 0);

            // The then-30px section header band, bottom-flush: immune at
            // 0.897 but vulnerable ~16-17% at 0.81, which is why it took
            // the same 1px clearance (it measures 17.0% here).
            Assert.Equal(0, VanishCount(30, 0, 0.897f));
            Assert.Equal(850, VanishCount(30, 0, 0.81f));
        }

        [Fact]
        public void ModelReproducesTheM36bImmune36pxRows()
        {
            // The pre-tier-2 36px flush fit was immune WITHOUT clearance
            // at every scale - the fact the tier-1 deferral leaned on.
            foreach (float scale in UiScales)
            {
                Assert.Equal(0, VanishCount(36, 0, scale));
            }
        }

        [Fact]
        public void TierTwoHeightsAreVulnerableWithoutTheClearancePixel()
        {
            // Why IconRowDividerClearance exists: the tier-2 flush sum
            // WITHOUT it (icon frame + divider = 44) and the naive
            // bottom-flush variant of the shipped icon-led height both
            // vanish. This is the assertion that keeps the clearance pixel
            // from being "simplified" away as slack.
            Assert.True(VanishCount(PlanContentHeightMath.RowIconFrameSize + DividerHeight, 0, 0.897f) > 0);
            Assert.True(VanishCount(PlanContentHeightMath.IconLedRowHeight, 0, 0.81f) > 0);
            Assert.True(VanishCount(PlanContentHeightMath.IconLedRowHeight, 0, 0.897f) > 0);
        }

        [Fact]
        public void TheCurrencyGridRowIsVulnerableWithoutTheClearancePixel()
        {
            // Why SettingsCurrencyGridLayout.CellDividerClearance survived
            // the row's growth from 32 to the list-tier height: the taller
            // row is not immune by itself. Bottom-flush it vanishes on ~10%
            // of scroll phases at the 0.897 "Normal" scale, the same rate
            // and the same scale as the M36b heights above.
            Assert.True(
                VanishCount(SettingsCurrencyGridLayout.CurrencyRowHeight, 0, 0.897f) > 0);
        }

        // --- The proof over the shipped geometry ---
        public static IEnumerable<object[]> ShippedDividerGeometries()
        {
            // Every CreateRowDivider caller's (rowHeight, bottomClearance),
            // plus the section header band divider built the same way.
            // Deduplicated because xUnit collapses identical theory cases
            // anyway (the four padded tier-2 rows share one geometry).
            int iconClearance = PlanContentHeightMath.IconRowDividerClearance;
            var pairs = new List<(int RowHeight, int Clearance)>
            {
                // UsedMaterialsSectionRenderer / ShoppingListSectionRenderer
                // / RecipesSectionRenderer / CraftStepsSectionRenderer: the
                // tier-2 padded fit.
                (PlanContentHeightMath.UsedMaterialRowHeight, iconClearance),
                (PlanContentHeightMath.ShoppingRowHeight, iconClearance),
                (PlanContentHeightMath.RecipeRowHeight, iconClearance),
                (PlanContentHeightMath.CraftStepRowHeight, iconClearance),

                // DisciplinesSectionRenderer passes 1 (belt-and-braces on
                // an immune height, kept because the text-only row has
                // nothing to collide with - see its own comment).
                (PlanContentHeightMath.DisciplineRowHeight, 1),

                // CraftingPlanView.CreateSectionHeader's band divider:
                // y = SectionHeaderRowHeight - 2 - 1, same shape.
                (PlanContentHeightMath.SectionHeaderRowHeight, 1),

                // SettingsTabContent's currency grid rows, whose height is
                // the module's list-tier currency row (icon box plus
                // PlanContentHeightMath.CurrencyRowIconPad either side).
                (SettingsCurrencyGridLayout.CurrencyRowHeight,
                    SettingsCurrencyGridLayout.CellDividerClearance),
            };

            var seen = new HashSet<(int, int)>();
            foreach (var pair in pairs)
            {
                if (seen.Add(pair))
                {
                    yield return new object[] { pair.RowHeight, pair.Clearance };
                }
            }
        }

        [Theory]
        [MemberData(nameof(ShippedDividerGeometries))]
        public void EveryShippedDividerGeometryIsImmuneAtEveryUiScale(int rowHeight, int bottomClearance)
        {
            foreach (float scale in UiScales)
            {
                Assert.Equal(0, VanishCount(rowHeight, bottomClearance, scale));
            }
        }

        [Fact]
        public void IconLedRowsKeepEqualPaddingAroundTheIconFrame()
        {
            // Reported in game: the rows used to be flush, so the icon
            // frame's bottom border and the divider's top edge shared a y.
            // The gap a reader sees above the frame and the gap below it
            // must both be IconRowFramePadding - which is why the icon's y
            // is one LESS than the padding, the row above ending on its own
            // clearance pixel.
            int rowHeight = PlanContentHeightMath.UsedMaterialRowHeight;
            int iconTop = PlanContentHeightMath.IconRowIconY;
            int iconBottom = iconTop + PlanContentHeightMath.RowIconFrameSize;
            int dividerTop = rowHeight - DividerHeight
                - PlanContentHeightMath.IconRowDividerClearance;

            Assert.Equal(PlanContentHeightMath.IconRowFramePadding, dividerTop - iconBottom);

            // The next row's icon against the divider this row just drew.
            int previousDividerBottom = dividerTop + DividerHeight;
            Assert.Equal(
                PlanContentHeightMath.IconRowFramePadding,
                (rowHeight + iconTop) - previousDividerBottom);
        }
    }
}
