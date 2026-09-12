using System;
using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The icon-frame clipping proof, executable. A container hands its
    /// children a clip that has been through a floor/ceil round trip in
    /// each direction, and that round trip can leave the clip short of the
    /// container's own bottom edge. This sweeps whether the clip a framed
    /// icon is handed still covers the whole frame, at every GW2 UI scale
    /// and every vertical position a window can be dragged to.
    /// <para>
    /// The clip model is the one RowDividerScissorSimulationTests
    /// transcribes from the decompiled Blish HUD 1.3.0 binary. Its sources
    /// are docs/ARCHITECTURE.md section V.26.
    /// </para>
    /// <para>
    /// Sub-pixel rasterization of the frame's own quad is deliberately not
    /// modelled here, and the reason is docs/ARCHITECTURE.md section S1.3.
    /// </para>
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
        /// another floor/ceil round trip at that shared edge. Two is the
        /// depth the module ships; the clearance below is not proven at
        /// three.</summary>
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
        /// Logical pixels of the icon frame that the clip reaching it does
        /// not cover, for a row at absolute logical y <paramref name="rowY"/>
        /// under <paramref name="flushAncestors"/> containers that stop
        /// <c>shape.TrailingClearance</c> below the row.
        /// </summary>
        private static int ClipShortfall(
            IconFrameShape shape, float uiScale, int rowY, int flushAncestors)
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
                    return shape.FrameSize;
                }
            }

            if (!Descend(rowY, rowY + shape.RowHeight, uiScale, ref clipTop, ref clipHeight))
            {
                return shape.FrameSize;
            }

            int frameBottom = rowY + shape.IconY + shape.FrameSize;
            return Math.Max(0, frameBottom - (clipTop + clipHeight));
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

        /// <summary>Vertical positions, of <see cref="Phases"/>, where the
        /// clip lands inside the frame.</summary>
        private static int ClippedCount(IconFrameShape shape, float uiScale, int flushAncestors)
        {
            int count = 0;
            for (int rowY = 0; rowY < Phases; rowY++)
            {
                if (ClipShortfall(shape, uiScale, rowY, flushAncestors) > 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Deepest the clip reaches into the frame, over every
        /// swept position and container depth.</summary>
        private static int WorstShortfall(IconFrameShape shape, float uiScale)
        {
            int worst = 0;
            for (int flush = 0; flush <= MaxFlushAncestors; flush++)
            {
                for (int rowY = 0; rowY < Phases; rowY++)
                {
                    worst = Math.Max(worst, ClipShortfall(shape, uiScale, rowY, flush));
                }
            }

            return worst;
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

        private static IconFrameShape WithoutClearance(IconFrameShape shape)
        {
            return new IconFrameShape(shape.RowHeight, shape.IconY, shape.FrameSize, 0);
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
        public void ModelReproducesTheClipLandingInsideTheLastRowsFrame()
        {
            // A search returning one Snapshot item drew a frame cut off at
            // the bottom. The result panel is sized to its content, so its
            // bottom edge sat on the last row's, and the clip it handed
            // down reached two logical pixels into that row's icon frame.
            // With no container ending on the row's edge the clip covers
            // the frame at every position, which is why the fault showed on
            // a short result list and nowhere else. A model that cannot
            // reproduce both halves has no authority over the geometry
            // below.
            var flushPanel = WithoutClearance(SnapshotItemCell);

            Assert.Equal(0, ClippedCount(flushPanel, 0.81f, 0));
            Assert.Equal(500, ClippedCount(flushPanel, 0.81f, 1));
            Assert.Equal(900, ClippedCount(flushPanel, 0.81f, 2));
            Assert.Equal(2, WorstShortfall(flushPanel, 0.81f));
        }

        [Fact]
        public void TheTrailingClearanceIsWhatSavesTheLastSnapshotRow()
        {
            // Why SnapshotResultLayout.TrailingClearance exists. Stated as
            // the pixel being present AND doing work, so that zeroing the
            // constant fails here rather than passing on an empty sweep.
            Assert.True(SnapshotResultLayout.TrailingClearance > 0);
            Assert.True(WorstShortfall(WithoutClearance(SnapshotItemCell), 0.81f) > 0);
        }

        // --- The proof over the shipped geometry ---
        [Theory]
        [MemberData(nameof(ShippedIconFrameNames))]
        public void EveryShippedIconFrameSitsWhollyInsideItsClip(string name)
        {
            var shape = ShippedShapes()[name];

            foreach (float scale in UiScales)
            {
                Assert.True(
                    WorstShortfall(shape, scale) == 0,
                    name + " is clipped at UI scale " + scale.ToString());
            }
        }
    }
}
