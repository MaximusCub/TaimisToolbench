namespace TaimisToolbench.Services
{
    /// <summary>
    /// The rectangles an OUTLINE icon frame paints: the border ring, and
    /// nothing inside it. Blish-free so the property the outline exists for -
    /// that no rectangle covers an interior pixel - is asserted without a
    /// graphics device.
    /// <para>
    /// A currency icon's art is mostly transparent (a coin, a shard, a
    /// sliver of crystal), so a filled frame plate behind it reads as a grey
    /// BACKGROUND rather than as a border. An item icon's art is a full-bleed
    /// bag-slot square and hides the same plate completely, which is why one
    /// shape cannot serve both.
    /// </para>
    /// </summary>
    internal static class IconFrameGeometry
    {
        /// <summary>One painted rectangle, in coordinates local to the frame.</summary>
        internal readonly struct Edge
        {
            internal readonly int X;
            internal readonly int Y;
            internal readonly int Width;
            internal readonly int Height;

            internal Edge(int x, int y, int width, int height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }
        }

        private static readonly Edge[] None = new Edge[0];

        /// <summary>
        /// Whether a currency icon at this tier paints its border ring. The
        /// two currency tiers split by role: a
        /// CurrencyListRow icon is a row's subject (the Settings valuation
        /// grid, the wallet rows, the plan Summary's currency table, the
        /// Ranker's shortfall list) and keeps the ring; a CurrencyBarRun
        /// icon sits after digits in the gold/silver/copper coins' role, a
        /// unit symbol rather than a subject, and the coins take no frame
        /// at all. A tier no currency is ever drawn at is a programming
        /// error, not a frame decision.
        /// </summary>
        public static bool CurrencyIsFramed(ItemIconTier tier)
        {
            switch (tier)
            {
                case ItemIconTier.CurrencyListRow: return true;

                // The Recipe Tree draws a currency row in the same column
                // as its item rows, at the item row's size. It is still a
                // row's subject, so it keeps the ring.
                case ItemIconTier.BagSidebar: return true;

                case ItemIconTier.CurrencyBarRun: return false;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(tier), tier, "Currency framing is only defined for the currency tiers.");
            }
        }

        /// <summary>
        /// A frame's border ring. Degenerate inputs answer with something
        /// drawable rather than throwing: a box with no room for an interior
        /// is all border, and a box with no size at all paints nothing.
        /// Width and height are taken separately so a frame that is not
        /// square draws its ring on its own edges rather than 2px inside
        /// one of them.
        /// </summary>
        public static Edge[] OutlineEdges(int width, int height, int thickness)
        {
            if (width <= 0 || height <= 0 || thickness <= 0)
            {
                return None;
            }

            if (2 * thickness >= width || 2 * thickness >= height)
            {
                return new[] { new Edge(0, 0, width, height) };
            }

            int inner = height - (2 * thickness);
            return new[]
            {
                new Edge(0, 0, width, thickness),
                new Edge(0, height - thickness, width, thickness),
                new Edge(0, thickness, thickness, inner),
                new Edge(width - thickness, thickness, thickness, inner),
            };
        }
    }
}
