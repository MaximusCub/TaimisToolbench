using System;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// How the snapshot asks for characters: whole records, a page at a
    /// time, instead of three narrow calls per character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on a 6-character account, 10 runs a shape, in PR #319. This
    /// fetched an identical snapshot in 14 requests where the three narrow
    /// calls per character took 31. Median 6374ms against 7085ms, and worst
    /// run 8317ms against 13001ms. The unread payload the full record
    /// carries came to 19 KB per character and cost no measurable time.
    /// </para>
    /// <para>
    /// The roster still comes from /v2/characters, so the page count is
    /// known before any page is asked for, and a character the roster names
    /// but no page returned is a character this fetch failed to read.
    /// </para>
    /// </remarks>
    internal static class CharacterPagePlan
    {
        /// <summary>
        /// Characters per request. Small pages cost more requests and large
        /// ones put more characters behind one failure, so this is measured
        /// rather than reasoned: a run at 3 beat a run at 2 in 68 of 100
        /// pairings, and 2 had the worse worst case, 13903ms against
        /// 8317ms.
        /// </summary>
        public const int PageSize = 3;

        /// <summary>
        /// Pages in flight at once. Six, matching the character fan-out it
        /// replaces, which was measured at the knee of its curve: 2, 3 and 6
        /// characters in flight took 16825ms, 12236ms and 8180ms, and going
        /// past 6 bought nothing.
        /// </summary>
        public const int MaxPagesInFlight = 6;

        /// <summary>
        /// How many pages a roster of this size needs. None for an empty
        /// roster: /v2/characters already answered that question, and asking
        /// for page 0 of nothing spends a request to be told the page is out
        /// of range, then spends the retries too.
        /// </summary>
        public static int PageCount(int characterCount)
        {
            return PageCount(characterCount, PageSize);
        }

        /// <summary>
        /// The same arithmetic for a page size the caller names, which is
        /// how the fetch profiler compares one size against another without
        /// keeping a second copy of this rule.
        /// </summary>
        public static int PageCount(int characterCount, int pageSize)
        {
            if (characterCount <= 0 || pageSize <= 0)
            {
                return 0;
            }

            return ((characterCount - 1) / pageSize) + 1;
        }
    }
}
