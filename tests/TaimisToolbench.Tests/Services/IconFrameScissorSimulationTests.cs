using System;
using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The icon-frame edge-vanishing proof, executable. A frame's bottom
    /// edge is a band of ItemIconTiers.PaintedFrameThickness logical pixels,
    /// and this sweeps whether that band reaches the screen at every GW2 UI
    /// scale and every vertical position a window can be dragged to.
    ///
    /// The paint model is the one RowDividerScissorSimulationTests
    /// transcribes from the decompiled Blish HUD 1.3.0 binary, applied to a
    /// frame quad instead of a divider quad and run through the containers a
    /// framed icon sits in. Its sources, and the rule that a model must
    /// reproduce a measured defect before it is trusted with the shipped
    /// geometry, are docs/ARCHITECTURE.md section V.26.
    /// </summary>
    public class IconFrameScissorSimulationTests
    {
        /// <summary>The four GW2 UI Size scale factors Blish applies as its
        /// UIScaleMultiplier (Small / Normal / Large / Larger).</summary>
        private static readonly float[] UiScales = { 0.81f, 0.897f, 1.0f, 1.103f };

        /// <summary>Vertical positions swept per case - the row's absolute
        /// logical Y, which is where the player put the window. 5000,
        /// matching RowDividerScissorSimulationTests.</summary>
        private const int Phases = 5000;

        /// <summary>Enclosing containers whose own bottom edge coincides
        /// with the row's, swept from none to two. Each one costs the clip
        /// another floor/ceil round trip at that shared edge.</summary>
        private const int MaxFlushAncestors = 2;

        // --- The model (see class doc for the decompiled sources) ---

        /// <summary>RectangleExtension.ScaleBy, one axis: floor the scaled
        /// origin, ceil the scaled extent, at float32 precision.</summary>
        private static void ScaleInterval(
            int top, int height, float scale, out int scaledTop, out int scaledHeight)
        {
            float topProduct = top * scale;
            float heightProduct = height * scale;
            scaledTop = (int)Math.Floor(topProduct);
            scaledHeight = (int)Math.Ceiling(heightProduct);
        }

        /// <summary>One container's round trip: Control.Draw scales the clip
        /// into physical space, Container.Paint unscales it back for the
        /// children.</summary>
        private static void Propagate(
            int top, int height, float scale, out int outTop, out int outHeight)
        {
            ScaleInterval(top, height, scale, out int physTop, out int physHeight);
            ScaleInterval(physTop, physHeight, 1f / scale, out outTop, out outHeight);
        }

        /// <summary>
        /// Physical scanlines the frame's bottom edge paints for a row at
        /// absolute logical y <paramref name="rowY"/>, under
        /// <paramref name="flushAncestors"/> containers that stop
        /// <c>shape.TrailingClearance</c> below the row.
        /// </summary>
        private static int BottomEdgeScanlines(
            IconFrameShape shape, int thickness, float uiScale, int rowY, int flushAncestors)
        {
            // The scrolling viewport above it all, generous below the row.
            Propagate(
                rowY - 256, 256 + shape.RowHeight + 512, uiScale,
                out int clipTop, out int clipHeight);

            for (int depth = flushAncestors; depth >= 1; depth--)
            {
                if (!Descend(
                        rowY - (32 * depth), rowY + shape.RowHeight + shape.TrailingClearance,
                        uiScale, ref clipTop, ref clipHeight))
                {
                    return 0;
                }
            }

            if (!Descend(rowY, rowY + shape.RowHeight, uiScale, ref clipTop, ref clipHeight))
            {
                return 0;
            }

            int frameBottom = rowY + shape.IconY + shape.FrameSize;
            int clippedTop = Math.Max(clipTop, rowY + shape.IconY);
            int clippedBottom = Math.Min(clipTop + clipHeight, frameBottom);
            if (clippedBottom <= clippedTop)
            {
                return 0;
            }

            ScaleInterval(
                clippedTop, clippedBottom - clippedTop, uiScale,
                out int scissorTop, out int scissorHeight);
            int scissorBottom = scissorTop + scissorHeight;

            // The edge's own quad, rasterized by the centre-in rule the
            // divider proof uses, then scissor-tested.
            float quadTop = (frameBottom - thickness) * uiScale;
            float quadBottom = frameBottom * uiScale;
            int covered = 0;
            for (int scanline = (int)Math.Floor(quadTop); scanline <= (int)Math.Ceiling(quadBottom); scanline++)
            {
                float centre = scanline + 0.5f;
                if (quadTop <= centre && centre < quadBottom
                    && scissorTop <= scanline && scanline < scissorBottom)
                {
                    covered++;
                }
            }

            return covered;
        }

        /// <summary>One step down the container chain: intersect the
        /// inherited clip with this container's bounds, then propagate.
        /// False when nothing is left to paint in.</summary>
        private static bool Descend(
            int top, int bottom, float uiScale, ref int clipTop, ref int clipHeight)
        {
            int intersectTop = Math.Max(clipTop, top);
            int intersectBottom = Math.Min(clipTop + clipHeight, bottom);
            if (intersectBottom <= intersectTop)
            {
                return false;
            }

            Propagate(
                intersectTop, intersectBottom - intersectTop, uiScale, out clipTop, out clipHeight);
            return true;
        }

        private static int VanishCount(IconFrameShape shape, int thickness, float uiScale)
        {
            int count = 0;
            for (int flush = 0; flush <= MaxFlushAncestors; flush++)
            {
                for (int rowY = 0; rowY < Phases; rowY++)
                {
                    if (BottomEdgeScanlines(shape, thickness, uiScale, rowY, flush) == 0)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>One surface's framed-icon geometry: the row box, where
        /// the frame sits in it, how big the frame is, and what the row's
        /// own container keeps clear below it.</summary>
        private readonly struct IconFrameShape
        {
            internal readonly int RowHeight;
            internal readonly int IconY;
            internal readonly int FrameSize;
            internal readonly int TrailingClearance;

            internal IconFrameShape(int rowHeight, int iconY, int frameSize, int trailingClearance)
            {
                RowHeight = rowHeight;
                IconY = iconY;
                FrameSize = frameSize;
                TrailingClearance = trailingClearance;
            }
        }

        private static readonly IconFrameShape SnapshotItemCell = new IconFrameShape(
            SnapshotItemGridLayout.ItemRowHeight,
            SnapshotItemGridLayout.ItemIconY,
            ItemIconTiers.FrameSize(ItemIconTier.BagSlot),
            SnapshotResultLayout.TrailingClearance);

        private static IReadOnlyDictionary<string, IconFrameShape> ShippedShapes()
        {
            return new Dictionary<string, IconFrameShape>
            {
                { "Snapshot item cell", SnapshotItemCell },
                {
                    "Snapshot wallet cell", new IconFrameShape(
                        SnapshotItemGridLayout.WalletRowHeight,
                        SnapshotItemGridLayout.WalletIconY,
                        ItemIconTiers.FrameSize(ItemIconTier.CurrencyListRow),
                        SnapshotResultLayout.TrailingClearance)
                },
                {
                    "Plan icon-led row", new IconFrameShape(
                        PlanContentHeightMath.IconLedRowHeight,
                        PlanContentHeightMath.IconRowIconY,
                        PlanContentHeightMath.RowIconFrameSize,
                        0)
                },
                {
                    "Plan recipe-tree row", new IconFrameShape(
                        PlanContentHeightMath.TreeRowHeight,
                        PlanContentHeightMath.TreeRowIconPad,
                        PlanContentHeightMath.RowIconFrameSize,
                        0)
                },
                {
                    "Plan currency row", new IconFrameShape(
                        PlanContentHeightMath.CurrencyRowHeight,
                        PlanContentHeightMath.CurrencyRowIconPad,
                        ItemIconTiers.FrameSize(ItemIconTier.CurrencyListRow),
                        0)
                },
                {
                    "Settings currency cell", new IconFrameShape(
                        SettingsCurrencyGridLayout.CurrencyRowHeight,
                        SettingsCurrencyGridLayout.CellIconY,
                        ItemIconTiers.FrameSize(ItemIconTier.CurrencyListRow),
                        0)
                },
            };
        }

        public static IEnumerable<object[]> ShippedIconFrameNames()
        {
            foreach (string name in ShippedShapes().Keys)
            {
                yield return new object[] { name };
            }
        }

        // --- Model validation against the reported defect ---
        [Fact]
        public void ModelReproducesTheReportedFrameWithNoBottomEdge()
        {
            // A search returning one Snapshot item drew a frame open at the
            // bottom: the art was whole and the edge below it was gone. At
            // the frame's RESERVED thickness the edge misses on 10.3% of
            // vertical positions at UI Size Normal and 42.0% at Small, and
            // the screenshot was a Normal-scale window. A model that cannot
            // reproduce that has no authority over the geometry below.
            Assert.Equal(1545, VanishCount(SnapshotItemCell, ItemIconTiers.FrameBorder, 0.897f));
            Assert.Equal(6300, VanishCount(SnapshotItemCell, ItemIconTiers.FrameBorder, 0.81f));

            // At and above 1.0 a logical pixel covers a physical one, so no
            // reader at UI Size Large or Larger ever saw this.
            Assert.Equal(0, VanishCount(SnapshotItemCell, ItemIconTiers.FrameBorder, 1.0f));
            Assert.Equal(0, VanishCount(SnapshotItemCell, ItemIconTiers.FrameBorder, 1.103f));
        }

        [Fact]
        public void TheTrailingClearanceIsWhatSavesTheLastSnapshotRow()
        {
            // Why SnapshotResultLayout.TrailingClearance exists. The item
            // cell keeps one logical pixel below its frame, which is not
            // enough on its own: with the result panel's bottom edge on the
            // last row's, the painted thickness still misses at UI Size
            // Small. This is the assertion that keeps the clearance from
            // being read as slack and simplified away.
            var flushPanel = new IconFrameShape(
                SnapshotItemGridLayout.ItemRowHeight,
                SnapshotItemGridLayout.ItemIconY,
                ItemIconTiers.FrameSize(ItemIconTier.BagSlot),
                0);

            Assert.True(
                VanishCount(flushPanel, ItemIconTiers.PaintedFrameThickness, 0.81f) > 0);
        }

        // --- The proof over the shipped geometry ---
        [Theory]
        [MemberData(nameof(ShippedIconFrameNames))]
        public void EveryShippedIconFrameKeepsItsBottomEdgeAtEveryUiScale(string name)
        {
            var shape = ShippedShapes()[name];

            foreach (float scale in UiScales)
            {
                Assert.True(
                    VanishCount(shape, ItemIconTiers.PaintedFrameThickness, scale) == 0,
                    name + " loses its frame edge at UI scale " + scale.ToString());
            }
        }
    }
}
