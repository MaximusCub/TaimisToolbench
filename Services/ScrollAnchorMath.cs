using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// One anchorable element's position inside the scrolled content, in
    /// content-space pixels (0 = the very top of the content, NOT the top
    /// of the viewport). Key is whatever stable identity the caller can
    /// re-find the same element by after a rebuild - a section type, a
    /// solver NodeId - compared by ordinal string equality.
    /// </summary>
    internal readonly struct ScrollAnchorCandidate
    {
        public readonly string Key;
        public readonly int Top;
        public readonly int Height;

        /// <summary>
        /// True when this element is not drawn and Top is the position of
        /// the nearest ancestor that is - a row inside a collapsed tree
        /// node is drawn as part of the row it hangs under. A hidden
        /// candidate can be re-found by <see cref="ScrollAnchorMath.FindTop"/>
        /// but is never chosen by <see cref="ScrollAnchorMath.TryCapture"/>:
        /// nothing the user can see sits at its Top.
        /// </summary>
        public readonly bool Hidden;

        public ScrollAnchorCandidate(string key, int top, int height)
            : this(key, top, height, false)
        {
        }

        public ScrollAnchorCandidate(string key, int top, int height, bool hidden)
        {
            Key = key;
            Top = top;
            Height = height;
            Hidden = hidden;
        }
    }

    /// <summary>
    /// The element a capture chose to hold still, and where it was when
    /// the capture ran.
    /// </summary>
    internal readonly struct ScrollAnchor
    {
        private static readonly ScrollAnchor[] NoFallbacks = new ScrollAnchor[0];

        public readonly string Key;
        public readonly int CapturedTop;

        private readonly ScrollAnchor[] _fallbacks;

        public ScrollAnchor(string key, int capturedTop)
            : this(key, capturedTop, null)
        {
        }

        public ScrollAnchor(string key, int capturedTop, ScrollAnchor[] fallbacks)
        {
            Key = key;
            CapturedTop = capturedTop;
            _fallbacks = fallbacks;
        }

        /// <summary>
        /// The other elements that were on screen when this anchor was
        /// captured, nearest to the anchor line first, each with its own
        /// captured top. A restore walks them in order when the anchor
        /// itself is gone from the rebuilt content, so one disappearing
        /// row costs the nearest surviving row rather than the whole
        /// anchoring. Never null, empty on a default-constructed anchor,
        /// and the entries carry no fallbacks of their own.
        /// </summary>
        public IReadOnlyList<ScrollAnchor> Fallbacks => _fallbacks ?? NoFallbacks;

        public bool IsValid => !string.IsNullOrEmpty(Key);
    }

    /// <summary>
    /// What one restore should do: the offset to scroll to, and how much
    /// trailing blank space the content needs for that offset to be legal.
    /// </summary>
    internal readonly struct ScrollRestorePlan
    {
        public readonly int Offset;

        /// <summary>
        /// Height of a spacer at the bottom of the scrolled content, zero
        /// for every case that does not need one. See
        /// <see cref="ScrollAnchorMath.TailSpacerHeight"/>.
        /// </summary>
        public readonly int TailSpacerHeight;

        public ScrollRestorePlan(int offset, int tailSpacerHeight)
        {
            Offset = offset;
            TailSpacerHeight = tailSpacerHeight;
        }
    }

    /// <summary>
    /// Pure scroll-ANCHORING arithmetic (Blish-free, unit-testable), the
    /// step beyond ScrollMath's offset preservation.
    /// <para>
    /// Preserving the offset alone keeps the viewport the same distance
    /// from the top of the content, which only holds the view still while
    /// the content ABOVE the viewport keeps its height. A re-solve
    /// routinely changes it - the Total Cost section gains or loses
    /// currency rows, an ignored subtree's rows disappear - and whatever
    /// the user was reading slides out from under their cursor. Anchoring
    /// records WHICH element the anchor line was on and restores the
    /// offset that puts that element back under the same line, absorbing
    /// every height change above it.
    /// </para>
    /// </summary>
    internal static class ScrollAnchorMath
    {
        /// <summary>
        /// The content-space y whose content should not move: the mouse
        /// cursor's line when it is over the scrolled viewport, otherwise
        /// the viewport's top edge. cursorYInViewport is the cursor's y
        /// relative to the viewport's top edge, or null when the cursor is
        /// elsewhere; a value outside the viewport is ignored, since a
        /// cursor off the panel is not what the user is reading.
        /// <para>
        /// topInset is how many pixels of the viewport's top edge another
        /// control is drawn over - a pinned sticky header band. The line
        /// moves below it, and a cursor inside it is treated as no cursor
        /// at all, because the user cannot be reading content they cannot
        /// see. This is the scroll-padding the CSS Scroll Anchoring model
        /// applies for a region "obscured by other content (such as
        /// fixed-positioned toolbars)".
        /// </para>
        /// </summary>
        public static int AnchorLine(
            int scrollOffset, int viewportHeight, int? cursorYInViewport, int topInset = 0)
        {
            int inset = topInset;
            if (inset < 0)
            {
                inset = 0;
            }

            // An inset at or past the bottom of the viewport would put the
            // anchor line on content nobody is looking at, so it gives up
            // and holds the plain top edge instead.
            if (inset >= viewportHeight)
            {
                inset = 0;
            }

            if (cursorYInViewport.HasValue &&
                cursorYInViewport.Value >= inset &&
                cursorYInViewport.Value < viewportHeight)
            {
                return scrollOffset + cursorYInViewport.Value;
            }

            return scrollOffset + inset;
        }

        /// <summary>
        /// Picks the element the anchor line falls on: the LOWEST-starting
        /// candidate at or above the line, ties broken toward the shortest.
        /// Candidates nest (a tree row lives inside a section) and the
        /// deeper element is both lower and shorter, so this resolves to
        /// the most specific element on the line without the caller having
        /// to describe the nesting.
        /// <para>
        /// Hidden candidates are skipped: they report the position of the
        /// row they are drawn under, so they tie with it and are never
        /// taller, and the tie-break would hand the anchor to something
        /// nobody can see.
        /// </para>
        /// <para>
        /// False when the line is above every drawn candidate: nothing up
        /// there can anchor anything, so the caller stays on plain offset
        /// preservation.
        /// </para>
        /// </summary>
        public static bool TryCapture(
            IReadOnlyList<ScrollAnchorCandidate> candidates, int anchorLine, out ScrollAnchor anchor)
        {
            anchor = default(ScrollAnchor);
            if (!TryPickOnLine(candidates, anchorLine, out var best))
            {
                return false;
            }

            anchor = new ScrollAnchor(best.Key, best.Top);
            return true;
        }

        /// <summary>
        /// <see cref="TryCapture(IReadOnlyList{ScrollAnchorCandidate}, int, out ScrollAnchor)"/>
        /// with the on-screen fallback list filled in, from the scroll
        /// offset and viewport height the capture ran at. Use this one
        /// wherever the caller knows both: the fallbacks cost one list
        /// build per rebuild and save the restore from giving up whenever
        /// the anchored element itself is gone.
        /// </summary>
        public static bool TryCapture(
            IReadOnlyList<ScrollAnchorCandidate> candidates,
            int anchorLine,
            int scrollOffset,
            int viewportHeight,
            out ScrollAnchor anchor)
        {
            anchor = default(ScrollAnchor);
            if (!TryPickOnLine(candidates, anchorLine, out var best))
            {
                return false;
            }

            anchor = new ScrollAnchor(
                best.Key,
                best.Top,
                BuildFallbacks(candidates, best.Key, anchorLine, scrollOffset, viewportHeight));
            return true;
        }

        /// <summary>
        /// The element a restore must hold still, in three tiers.
        /// <list type="number">
        /// <item>explicitKey, the element the user just acted on. It
        /// applies at any scroll offset, including zero.</item>
        /// <item>Otherwise the cursor's line, above offset zero only.</item>
        /// <item>Otherwise the viewport's top edge past topInset, above
        /// offset zero only.</item>
        /// </list>
        /// <para>
        /// The last two are suppressed at the top of the content. A
        /// top-edge anchor has nothing above it to absorb, and the cursor
        /// alone cannot tell a click from a background rebuild that runs
        /// while the cursor happens to rest over the content - anchoring
        /// the second would scroll the view by itself. The CSS Scroll
        /// Anchoring model suppresses at offset zero for the same reason.
        /// An explicit key is the caller naming what the user just acted
        /// on, which is what licenses overriding the suppression.
        /// </para>
        /// </summary>
        public static bool TryCaptureFor(
            IReadOnlyList<ScrollAnchorCandidate> candidates,
            string explicitKey,
            int scrollOffset,
            int viewportHeight,
            int? cursorYInViewport,
            int topInset,
            out ScrollAnchor anchor)
        {
            if (TryCaptureKey(candidates, explicitKey, scrollOffset, viewportHeight, out anchor))
            {
                return true;
            }

            if (scrollOffset <= 0)
            {
                anchor = default(ScrollAnchor);
                return false;
            }

            int anchorLine = AnchorLine(scrollOffset, viewportHeight, cursorYInViewport, topInset);
            return TryCapture(candidates, anchorLine, scrollOffset, viewportHeight, out anchor);
        }

        /// <summary>
        /// Anchors to one named element instead of to whatever the anchor
        /// line falls on. The caller names it because the user just acted
        /// on it - a decision pill click on a tree row - which is a
        /// stronger statement about what must hold still than any
        /// inference from the cursor.
        /// <para>
        /// A hidden candidate is accepted here, unlike in
        /// <see cref="TryCapture(IReadOnlyList{ScrollAnchorCandidate}, int, out ScrollAnchor)"/>:
        /// the caller named this element, and the row it is drawn under is
        /// a real position to hold still. False when the key names nothing
        /// in this layout.
        /// </para>
        /// </summary>
        public static bool TryCaptureKey(
            IReadOnlyList<ScrollAnchorCandidate> candidates,
            string key,
            int scrollOffset,
            int viewportHeight,
            out ScrollAnchor anchor)
        {
            anchor = default(ScrollAnchor);
            if (candidates == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            int? top = FindTop(candidates, new ScrollAnchor(key, 0));
            if (!top.HasValue)
            {
                return false;
            }

            anchor = new ScrollAnchor(
                key,
                top.Value,
                BuildFallbacks(candidates, key, top.Value, scrollOffset, viewportHeight));
            return true;
        }

        /// <summary>
        /// The element the anchor line falls on: the LOWEST-starting drawn
        /// candidate at or above the line, ties broken toward the
        /// shortest.
        /// </summary>
        private static bool TryPickOnLine(
            IReadOnlyList<ScrollAnchorCandidate> candidates, int anchorLine, out ScrollAnchorCandidate best)
        {
            best = default(ScrollAnchorCandidate);
            if (candidates == null)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (string.IsNullOrEmpty(candidate.Key) ||
                    candidate.Hidden ||
                    candidate.Top > anchorLine)
                {
                    continue;
                }

                if (!found ||
                    candidate.Top > best.Top ||
                    (candidate.Top == best.Top && candidate.Height < best.Height))
                {
                    best = candidate;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// The drawn candidates whose captured top fell inside the
        /// viewport, ordered by distance from the anchor line and
        /// excluding the anchor itself. Off-screen elements are left out:
        /// putting one back where it was says nothing about what the user
        /// can see. Hidden candidates are left out for the same reason
        /// <see cref="TryCapture(IReadOnlyList{ScrollAnchorCandidate}, int, out ScrollAnchor)"/>
        /// refuses them - they report the position of the row they are
        /// drawn under, which is already in the list.
        /// </summary>
        private static ScrollAnchor[] BuildFallbacks(
            IReadOnlyList<ScrollAnchorCandidate> candidates,
            string anchorKey,
            int anchorLine,
            int scrollOffset,
            int viewportHeight)
        {
            if (viewportHeight <= 0)
            {
                return null;
            }

            int viewportBottom = scrollOffset + viewportHeight;
            var onScreen = new List<ScrollAnchorCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (string.IsNullOrEmpty(candidate.Key) ||
                    candidate.Hidden ||
                    candidate.Top < scrollOffset ||
                    candidate.Top >= viewportBottom ||
                    string.Equals(candidate.Key, anchorKey, StringComparison.Ordinal))
                {
                    continue;
                }

                onScreen.Add(candidate);
            }

            if (onScreen.Count == 0)
            {
                return null;
            }

            onScreen.Sort((a, b) =>
            {
                int byDistance = System.Math.Abs(a.Top - anchorLine).CompareTo(
                    System.Math.Abs(b.Top - anchorLine));
                if (byDistance != 0)
                {
                    return byDistance;
                }

                int byTop = a.Top.CompareTo(b.Top);
                return byTop != 0 ? byTop : string.CompareOrdinal(a.Key, b.Key);
            });

            var fallbacks = new ScrollAnchor[onScreen.Count];
            for (int i = 0; i < onScreen.Count; i++)
            {
                fallbacks[i] = new ScrollAnchor(onScreen[i].Key, onScreen[i].Top);
            }

            return fallbacks;
        }

        /// <summary>
        /// The anchored element's post-rebuild top, or null when it no
        /// longer exists (the anchored row was inside the subtree the user
        /// just ignored, say) - in which case the caller falls back to
        /// plain offset preservation rather than jumping somewhere
        /// arbitrary.
        /// <para>
        /// A hidden candidate answers here, unlike in
        /// <see cref="TryCapture"/>. A rebuild can leave the anchored row
        /// inside a collapsed node, and the row it is drawn under is a
        /// real position to hold still; refusing it would drop the anchor
        /// for the whole subtree instead.
        /// </para>
        /// </summary>
        public static int? FindTop(IReadOnlyList<ScrollAnchorCandidate> candidates, ScrollAnchor anchor)
        {
            if (candidates == null || !anchor.IsValid)
            {
                return null;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i].Key, anchor.Key, StringComparison.Ordinal))
                {
                    return candidates[i].Top;
                }
            }

            return null;
        }

        /// <summary>
        /// The scroll offset that puts the anchored element back under the
        /// line it was on, clamped to what the new content can scroll to.
        /// <para>
        /// It is the old offset plus how far the element itself moved: the
        /// anchor line's distance below the viewport top is the same
        /// before and after, so it cancels and the cursor position never
        /// enters the arithmetic - it only decides WHICH element
        /// <see cref="TryCapture"/> anchors to.
        /// </para>
        /// </summary>
        public static int RestoredOffset(
            int savedOffset, ScrollAnchor anchor, int newAnchorTop, int contentHeight, int viewportHeight)
        {
            return ClampOffset(
                TargetOffset(savedOffset, anchor, newAnchorTop), contentHeight, viewportHeight);
        }

        /// <summary>
        /// The whole restore decision: which element survived, the offset
        /// that puts it back under its line, and the trailing spacer that
        /// offset needs.
        /// <para>
        /// The spacer is sized from the anchored target only. When nothing
        /// survived there is no spacer at all: a user parked in a previous
        /// spacer's blank space has nothing to hold still, and sizing one
        /// from the saved offset would preserve a position in emptiness
        /// and go on doing it. That case clamps back onto content.
        /// </para>
        /// </summary>
        public static ScrollRestorePlan PlanRestore(
            IReadOnlyList<ScrollAnchorCandidate> candidates,
            ScrollAnchor anchor,
            int savedOffset,
            int contentHeight,
            int viewportHeight)
        {
            if (!TryFindSurvivingAnchor(candidates, anchor, out var survivor, out int newTop))
            {
                return new ScrollRestorePlan(
                    ClampOffset(savedOffset, contentHeight, viewportHeight), 0);
            }

            int target = TargetOffset(savedOffset, survivor, newTop);
            int spacer = TailSpacerHeight(target, contentHeight, viewportHeight);
            return new ScrollRestorePlan(
                ClampOffset(target, contentHeight + spacer, viewportHeight), spacer);
        }

        /// <summary>
        /// Trailing blank space the content needs for targetOffset to be a
        /// legal scroll position: max(0, target - (content - viewport)).
        /// Zero whenever the content is long enough to hold the target,
        /// which is every plan that grew or held its height.
        /// <para>
        /// Sized to the shortfall and nothing more. A constant pad of one
        /// viewport, which is what Monaco ships as scrollBeyondLastLine,
        /// also works, but it shortens the scrollbar thumb on every plan
        /// and worst on the short ones that are the common case. Simulated
        /// thumb sizes: a short plan reads 77 per cent demand-sized
        /// against 43 constant, and a plan just over the threshold 93
        /// against 48.
        /// </para>
        /// </summary>
        public static int TailSpacerHeight(int targetOffset, int contentHeight, int viewportHeight)
        {
            if (targetOffset <= 0 || viewportHeight <= 0)
            {
                return 0;
            }

            int shortfall = targetOffset - (contentHeight - viewportHeight);
            return shortfall > 0 ? shortfall : 0;
        }

        /// <summary>
        /// The offset that puts the anchored element back under its line,
        /// before any clamp: the old offset plus how far the element
        /// itself moved.
        /// </summary>
        private static int TargetOffset(int savedOffset, ScrollAnchor anchor, int newAnchorTop)
        {
            return savedOffset + (newAnchorTop - anchor.CapturedTop);
        }

        /// <summary>
        /// The first of the anchor and its fallbacks that still exists in
        /// the rebuilt content, with the top it is at now. The order is
        /// the capture's: the anchor, then the elements that were on
        /// screen with it, nearest first.
        /// <para>
        /// False when none of them survived, which is the only case the
        /// caller has nothing to hold still for.
        /// </para>
        /// </summary>
        public static bool TryFindSurvivingAnchor(
            IReadOnlyList<ScrollAnchorCandidate> candidates,
            ScrollAnchor anchor,
            out ScrollAnchor survivor,
            out int newTop)
        {
            survivor = default(ScrollAnchor);
            newTop = 0;

            int? top = FindTop(candidates, anchor);
            if (top.HasValue)
            {
                survivor = anchor;
                newTop = top.Value;
                return true;
            }

            var fallbacks = anchor.Fallbacks;
            for (int i = 0; i < fallbacks.Count; i++)
            {
                top = FindTop(candidates, fallbacks[i]);
                if (top.HasValue)
                {
                    survivor = fallbacks[i];
                    newTop = top.Value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// An offset the new content can actually scroll to. Handing an
        /// out-of-range offset on to ScrollMath.RatioForOffset is not
        /// harmless: that method saturates its ratio at 1.0, so an offset
        /// past the end of shorter content lands the viewport at the
        /// bottom. Clamping here makes that landing a decision rather than
        /// a side effect of a clamp meant for something else.
        /// </summary>
        public static int ClampOffset(int offset, int contentHeight, int viewportHeight)
        {
            int maxOffset = contentHeight - viewportHeight;
            if (maxOffset < 0)
            {
                maxOffset = 0;
            }

            if (offset < 0)
            {
                return 0;
            }

            return offset > maxOffset ? maxOffset : offset;
        }
    }
}
