using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// Pins a table's column-header band to the top of a scrolling region
    /// while that table's rows are being scrolled past. Blish has no sticky
    /// primitive: a control inside a scrolling Container moves with it, and
    /// a Container clips its children to its own bounds and nothing else.
    /// <para>
    /// So a pinned band is RE-PARENTED into a clip Panel that is a sibling
    /// of the scroll region, sized to the slice that should show. One band,
    /// not a copy: the hover washes, click cells and tooltips are the ones
    /// the table already built, so a pinned header sorts like the header it
    /// is - which takes both halves of <see cref="ClipZIndex"/>'s note. The
    /// clip supplies the top-edge cut Blish will not, and is never bigger
    /// than what it shows.
    /// </para>
    /// <para>
    /// Placement is <see cref="StickyHeaderLayout"/>'s; everything here is
    /// the Blish half - reading live positions, moving controls, and the
    /// per-frame tick that drives it (Blish raises no scroll event).
    /// </para>
    /// </summary>
    internal sealed class StickyHeaderHost
    {
        private static readonly Logger Logger = Logger.GetLogger<StickyHeaderHost>();

        /// <summary>
        /// Deliberately ABOVE the scrolling panel's own (the vendor default
        /// is 5): the hit test walks children by ZIndex DESCENDING and
        /// breaks on the first that answers, so at any lower value the
        /// scroll panel wins every event over a pinned band and the click
        /// lands on the scrolled row HIDDEN BEHIND it. Paint order is the
        /// opposite walk (<c>Container.PaintChildren</c> orders ascending),
        /// so this value also paints the band over the rows; the viewport's
        /// published cutoff (Views/Rendering/ClipCutoff.cs) is kept as the
        /// order-independent guarantee.
        /// <para>
        /// The wheel still reaches the scroll panel because the clip and
        /// every container in the band below it are
        /// <c>WheelTransparentClippedPanel</c> - see that type for the
        /// mechanism.
        /// </para>
        /// </summary>
        private const int ClipZIndex = 10;

        /// <summary>
        /// Where one tracked band sits inside the scrolling content, and how
        /// far its table runs. All in the HOME container's own coordinates,
        /// re-read every frame: the caller's layout owns these, and a table
        /// that is not showing says so with <see cref="Present"/> rather
        /// than by being untracked.
        /// </summary>
        internal readonly struct BandGeometry
        {
            public readonly bool Present;
            public readonly int X;
            public readonly int Y;
            public readonly int Width;
            public readonly int Height;

            /// <summary>One pixel past the table's last row.</summary>
            public readonly int TableBottom;

            public BandGeometry(bool present, int x, int y, int width, int height, int tableBottom)
            {
                Present = present;
                X = x;
                Y = y;
                Width = width;
                Height = height;
                TableBottom = tableBottom;
            }
        }

        private sealed class Entry
        {
            internal Panel Band;
            internal Container Home;
            internal Func<BandGeometry> Geometry;
            internal Panel Clip;
            internal bool Pinned;
        }

        private readonly Container _parent;
        private readonly Container _scrollRegion;
        private readonly List<Entry> _entries = new List<Entry>();

        // The absolute y of the lowest pinned band's bottom edge, from the
        // most recent frame's placement. The two runs share one scroll, and
        // at most one band can pin at a time: StickyHeaderLayout never pins
        // a band whose table's last row has passed the viewport top, and the
        // section chrome between the runs keeps the lower band below the
        // upper table's bottom. The max keeps the rule the viewport's cutoff
        // implements literal - the lowest pinned edge - so a future layout
        // that did pin two would still clip the shared content at the lower
        // band and keep both clean.
        private int? _pinnedBottom;

        /// <summary>Set by the ticker's own failure path; see there.</summary>
        private bool _stopped;

        /// <summary>
        /// <paramref name="parent"/> must NOT scroll - it is where the
        /// pinned clip lives, and a clip that scrolled would defeat the
        /// whole exercise. <paramref name="scrollRegion"/> is the scrolling
        /// panel inside it, whose ContentRegion is the viewport.
        /// </summary>
        internal StickyHeaderHost(Container parent, Container scrollRegion)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            _scrollRegion = scrollRegion ?? throw new ArgumentNullException(nameof(scrollRegion));

            // Held only by its parent, which disposes it: this host lives
            // exactly as long as the tab panel it was built for.
            new Ticker(this) { Parent = parent };
        }

        /// <summary>
        /// Tracks one table's band. <paramref name="home"/> is the container
        /// the band belongs to when it is not pinned - the same one whose
        /// live position tells this class where the table has scrolled to,
        /// which is why the band is described in ITS coordinates and not the
        /// viewport's.
        /// </summary>
        internal void Track(Panel band, Container home, Func<BandGeometry> geometry)
        {
            if (band == null || home == null || geometry == null)
            {
                return;
            }

            _entries.Add(new Entry
            {
                Band = band,
                Home = home,
                Geometry = geometry,
                // Publishes the cutoff for its own subtree: no viewport's
                // line is in force out here, and a band being pushed out
                // sits above the clip that shows it
                // (Views/Rendering/ClipCutoff.cs).
                Clip = new WheelTransparentClipAuthorityPanel()
                {
                    Size = Point.Zero,
                    Visible = false,
                    ZIndex = ClipZIndex,
                    BackgroundColor = HeaderBands.BandColor,
                    Parent = _parent,
                },
            });
        }

        /// <summary>Drops every tracked band, unpinning first so none is
        /// left orphaned in a clip that is about to go away.</summary>
        internal void Clear()
        {
            _pinnedBottom = null;
            foreach (var entry in _entries)
            {
                Unpin(entry, entry.Geometry());
                entry.Clip.Dispose();
            }

            _entries.Clear();
        }

        /// <summary>
        /// The absolute y of the lowest pinned band's bottom edge as of the
        /// most recent COMPLETED placement, or null when none is pinned. The
        /// viewport's authority reads this at paint time: the ticker's update
        /// and the paint walk are the same main thread with update first, so
        /// the value is the frame's own.
        /// <para>
        /// A frame that throws part-way publishes nothing: the value is an
        /// absolute y and the clips are parent-relative, so a half-placed
        /// frame left standing would drift from its band the moment the
        /// window was dragged. The failure path stands the whole feature
        /// down instead - see <see cref="StandDown"/>.
        /// </para>
        /// </summary>
        internal int? PinnedBandBottom => _pinnedBottom;

        /// <summary>
        /// One frame's placement for every tracked band. Reads positions and
        /// writes at most a Location and a Size per band, so the common case
        /// - nothing pinned, nothing moved - costs a handful of rectangle
        /// reads. Also folds the pinned bands' bottom edges into
        /// <see cref="PinnedBandBottom"/> for the viewport's paint-time
        /// cutoff; the early returns below leave the last computed value
        /// standing.
        /// </summary>
        internal void Update()
        {
            if (_stopped || _entries.Count == 0 || _scrollRegion.Parent == null)
            {
                return;
            }

            var viewport = _scrollRegion.ContentRegion;
            var scrollBounds = _scrollRegion.AbsoluteBounds;
            int viewportTop = scrollBounds.Y + viewport.Y;

            var parentBounds = _parent.AbsoluteBounds;
            var parentRegion = _parent.ContentRegion;
            int originX = parentBounds.X + parentRegion.X;
            int originY = parentBounds.Y + parentRegion.Y;

            int? lowest = null;
            for (int i = 0; i < _entries.Count; i++)
            {
                int? bottom = Place(_entries[i], viewportTop, viewport.Height, originX, originY);
                if (bottom.HasValue && (!lowest.HasValue || bottom.Value > lowest.Value))
                {
                    lowest = bottom;
                }
            }

            // Published only once every band has been placed, so a throw
            // part-way through cannot leave a cutoff describing a band that
            // was never moved.
            _pinnedBottom = lowest;
        }

        /// <summary>
        /// Gives every band back to its own container and withdraws the
        /// cutoff, leaving the tables scrolling normally with no sticky
        /// headers at all. The failure path's degradation: a frozen band
        /// with a frozen ABSOLUTE cutoff drifts from the parent-relative
        /// clip drawing it as soon as the window is dragged, and scrolled
        /// rows then overdraw exactly the band the cutoff was protecting.
        /// Nothing is disposed - a control must not leave the tree from
        /// inside the update walk over it.
        /// </summary>
        private void StandDown()
        {
            _pinnedBottom = null;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                try
                {
                    Unpin(entry, entry.Geometry());
                }
                catch (Exception ex)
                {
                    // The caller's geometry closure is the likeliest thing
                    // to have thrown in the first place; one bad entry must
                    // not strand the others pinned.
                    Logger.Warn(ex, "Sticky header stand-down failed for one band");
                }
            }
        }

        /// <summary>The absolute y of the pinned band's bottom edge, or null
        /// when nothing is pinned this frame.</summary>
        private static int? Place(
            Entry entry, int viewportTop, int viewportHeight, int originX, int originY)
        {
            var geometry = entry.Geometry();
            if (!geometry.Present || entry.Home.Parent == null)
            {
                Unpin(entry, geometry);
                return null;
            }

            // The home container's own absolute position already carries the
            // scroll, so the band's viewport-relative y falls out of it
            // without this class knowing how Blish applies a scroll offset.
            var homeBounds = entry.Home.AbsoluteBounds;
            var homeRegion = entry.Home.ContentRegion;
            int homeTop = homeBounds.Y + homeRegion.Y;
            int headerY = homeTop + geometry.Y - viewportTop;
            int tableBottomY = homeTop + geometry.TableBottom - viewportTop;

            var placement = StickyHeaderLayout.Compute(
                headerY, geometry.Height, tableBottomY, viewportHeight);

            if (!placement.Pinned)
            {
                Unpin(entry, geometry);
                return null;
            }

            // The viewport cuts scrolled rows at the band's bottom plus the
            // scale's slip budget, and how much of that strip a given row
            // actually loses depends on its own edge's phase - so the strip
            // has to be PAINTED or a hairline of window background flickers
            // under the band as it scrolls. The clip carries the band's own
            // fill for exactly that height. Only while the band is whole:
            // during the push-out its bottom is the table's last row, and
            // padding past that would draw band colour below the table.
            int slipStrip = placement.VisibleHeight >= geometry.Height
                ? ClipCutoffMath.SlipBudgetFor(GameService.Graphics.UIScaleMultiplier)
                : 0;

            int clipX = homeBounds.X + homeRegion.X + geometry.X;
            var clipLocation = new Point(clipX - originX, viewportTop + placement.ClipY - originY);
            var clipSize = new Point(geometry.Width, placement.VisibleHeight + slipStrip);
            if (entry.Clip.Location != clipLocation)
            {
                entry.Clip.Location = clipLocation;
            }

            if (entry.Clip.Size != clipSize)
            {
                entry.Clip.Size = clipSize;
            }

            if (!entry.Pinned)
            {
                entry.Band.Parent = entry.Clip;
                entry.Pinned = true;
            }

            var bandLocation = new Point(0, -placement.OffsetInBand);
            if (entry.Band.Location != bandLocation)
            {
                entry.Band.Location = bandLocation;
            }

            entry.Clip.Visible = true;

            // The published line rides the band's live bottom edge - the
            // clip's own bottom, which the push-out already keeps glued to
            // its table as the last row scrolls the band away.
            return viewportTop + placement.ClipY + placement.VisibleHeight;
        }

        /// <summary>Puts a band back where its own layout wants it. The
        /// caller's geometry is the authority on that, not a location
        /// remembered from before the pin - a resize during a pin would have
        /// moved it.</summary>
        private static void Unpin(Entry entry, BandGeometry geometry)
        {
            entry.Clip.Visible = false;
            if (!entry.Pinned)
            {
                return;
            }

            entry.Band.Parent = entry.Home;
            entry.Pinned = false;
            entry.Band.Location = new Point(geometry.X, geometry.Y);
        }

        /// <summary>
        /// Blish raises nothing when a panel is scrolled, so placement is
        /// re-derived per frame. Zero-sized on purpose: it exists to be
        /// updated, and a control with no area is in no hit test - the same
        /// idiom Views/ModalBackdrop uses when it stands down.
        /// </summary>
        private sealed class Ticker : Control
        {
            private readonly StickyHeaderHost _host;

            internal Ticker(StickyHeaderHost host)
            {
                _host = host;
                Size = Point.Zero;
                Location = Point.Zero;
            }

            public override void DoUpdate(GameTime gameTime)
            {
                try
                {
                    _host.Update();
                }
                catch (Exception ex)
                {
                    // One line, then stood down: this runs every frame, and
                    // a header stuck mid-scroll reads to the user as a
                    // frozen table rather than as an error.
                    _host._stopped = true;
                    Logger.Warn(ex, "Sticky header placement failed; stopping");

                    try
                    {
                        _host.StandDown();
                    }
                    catch (Exception standDownEx)
                    {
                        Logger.Warn(standDownEx, "Sticky header stand-down failed");
                    }

                    // At most one line: _stopped is what keeps this from
                    // running again. Not disposed here - a control must not
                    // leave the tree from inside the update walk over it.
                    ModuleLog.Shared.Write(
                        ModuleLogLevel.Warn, "ui",
                        "Sticky table headers stopped after a placement failure: "
                        + ex.GetType().Name + " - " + ex.Message);
                }
            }

            protected override void Paint(
                Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Rectangle bounds)
            {
            }
        }
    }
}
