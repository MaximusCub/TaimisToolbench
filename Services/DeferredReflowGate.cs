using System;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// "A reflow is pending at width W", for a strip whose layout is a step
    /// function of its width. The plan tab's item input grid is one: it
    /// seats another column every time the window crosses a column-count
    /// boundary, so a drag that crosses several boundaries repacks the
    /// whole strip once per boundary and stretches every cell in between.
    /// The gate holds the newest width and hands it back exactly once - see
    /// <see cref="TryTake"/> for when.
    /// <para>
    /// Blish-free and clock-injected: the caller passes the time and
    /// whether a drag is still running, so the state machine is decidable
    /// without a game loop.
    /// </para>
    /// </summary>
    internal sealed class DeferredReflowGate
    {
        private readonly double _settleMs;
        private readonly double _stallMs;
        private bool _pending;
        private bool _dragSeenWhilePending;
        private int _pendingWidth;
        private DateTime _pendingSinceUtc;

        /// <param name="settleMs">Quiet interval that releases a reflow no
        /// drag drove.</param>
        /// <param name="stallMs">Ceiling on how long a drag that reads as
        /// still running may hold a reflow back - see <see cref="TryTake"/>.
        /// </param>
        public DeferredReflowGate(double settleMs, double stallMs)
        {
            _settleMs = settleMs > 0 ? settleMs : 0;
            _stallMs = stallMs > 0 ? stallMs : 0;
        }

        /// <summary>
        /// The width the strip is laid out at right now. Every layout
        /// derived from that width - the strip's own cells, and the height
        /// the content below it starts at - has to read this rather than
        /// the window's live width, or the two disagree for the length of
        /// a drag and the content moves ahead of the strip it sits under.
        /// </summary>
        public int AppliedWidth { get; private set; }

        public bool IsPending
        {
            get { return _pending; }
        }

        /// <summary>Width a take would hand back if one succeeded now.</summary>
        public int PendingWidth
        {
            get { return _pending ? _pendingWidth : AppliedWidth; }
        }

        /// <summary>
        /// Declares the strip laid out at <paramref name="width"/> and
        /// drops any deferred width, for a caller that just rebuilt the
        /// strip outright.
        /// </summary>
        public void Reset(int width)
        {
            AppliedWidth = width;
            _pendingWidth = width;
            _pending = false;
            _dragSeenWhilePending = false;
        }

        /// <summary>Abandons a deferred width without applying it - the
        /// panel it was measured against is gone.</summary>
        public void CancelPending()
        {
            _pendingWidth = AppliedWidth;
            _pending = false;
            _dragSeenWhilePending = false;
        }

        /// <summary>
        /// Records the width a resize tick reported, and returns whether a
        /// reflow is pending afterwards. A width equal to the applied one
        /// cancels the pending reflow rather than scheduling a no-op: a
        /// drag that returns to where it started has nothing to re-seat.
        /// <para>
        /// <paramref name="dragActive"/> is what makes this pending width a
        /// DRAG's rather than a resize the window performed on its own. It
        /// is recorded here as well as at the take, so a drag whose ticks
        /// all land before the first take is still recognised as one.
        /// </para>
        /// </summary>
        public bool Observe(int width, DateTime nowUtc, bool dragActive)
        {
            if (width == AppliedWidth)
            {
                CancelPending();
                return false;
            }

            _pending = true;
            _dragSeenWhilePending |= dragActive;
            _pendingWidth = width;
            _pendingSinceUtc = nowUtc;
            return true;
        }

        /// <summary>
        /// Hands back the deferred width once and only once. The gate
        /// counts it as applied from that moment, so a caller that takes a
        /// width must be able to act on it.
        /// <para>
        /// A drag releases on the frame it ends and on nothing else. A
        /// hand steady for a moment is ordinary inside a drag, and
        /// re-seating the strip on that pause is what felt clunky.
        /// </para>
        /// <para>
        /// A resize no drag drove - a resolution change, a fullscreen
        /// toggle - has no release to wait for and arrives as a burst of
        /// ticks, so <c>settleMs</c> collapses it to one reflow.
        /// </para>
        /// <para>
        /// <c>stallMs</c> bounds the drag's wait, because a drag flag can
        /// outlive the drag. It runs from the last width observed, so a
        /// live drag reaches it only by holding the grip still.
        /// </para>
        /// </summary>
        public bool TryTake(DateTime nowUtc, bool dragActive, out int width)
        {
            width = AppliedWidth;
            if (!_pending)
            {
                return false;
            }

            double sincePending = (nowUtc - _pendingSinceUtc).TotalMilliseconds;
            if (dragActive)
            {
                _dragSeenWhilePending = true;
                if (sincePending < _stallMs)
                {
                    return false;
                }
            }
            else if (!_dragSeenWhilePending && sincePending < _settleMs)
            {
                return false;
            }

            width = _pendingWidth;
            AppliedWidth = _pendingWidth;
            _pending = false;
            _dragSeenWhilePending = false;
            return true;
        }
    }
}
