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
        public readonly string Key;
        public readonly int CapturedTop;

        public ScrollAnchor(string key, int capturedTop)
        {
            Key = key;
            CapturedTop = capturedTop;
        }

        public bool IsValid => !string.IsNullOrEmpty(Key);
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
        /// </summary>
        public static int AnchorLine(int scrollOffset, int viewportHeight, int? cursorYInViewport)
        {
            if (cursorYInViewport.HasValue &&
                cursorYInViewport.Value >= 0 &&
                cursorYInViewport.Value < viewportHeight)
            {
                return scrollOffset + cursorYInViewport.Value;
            }

            return scrollOffset;
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
            if (candidates == null)
            {
                return false;
            }

            bool found = false;
            ScrollAnchorCandidate best = default(ScrollAnchorCandidate);
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

            if (!found)
            {
                return false;
            }

            anchor = new ScrollAnchor(best.Key, best.Top);
            return true;
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
            int offset = savedOffset + (newAnchorTop - anchor.CapturedTop);

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
