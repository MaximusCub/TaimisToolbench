using System;
using System.Collections.Generic;
using TaimisToolbench.Contracts;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure decisions that keep an input row's resolved item in step with
    /// the text its search box actually shows (Blish-free, unit-testable).
    /// A row carries both an item id and the name the user sees, but only a
    /// suggestion pick ever sets the two together - free typing afterwards
    /// leaves the id behind unless something invalidates it, which is how a
    /// box reading one item could generate a plan for another.
    /// </summary>
    internal static class ItemRowSelection
    {
        /// <summary>Shown when every row is genuinely empty.</summary>
        // Two actions in the order they happen, and the button named
        // exactly. "Select at least one item before generating" scolded
        // for a state the reader is already in, and named no button.
        public const string NoItemsStatus = "Add at least one item, then Generate Plan.";

        /// <summary>
        /// Shown when a row has text that resolved to nothing - the typed
        /// name is not a plan target, or was only partly typed.
        /// </summary>
        public const string UnmatchedTextStatus =
            "No item matches that name - pick one from the suggestion list.";

        /// <summary>
        /// Shown when the typed name belongs to more than one item. Item
        /// ids are internal-only, so a plan for the wrong same-named item
        /// would be undetectable on screen - the user has to say which one.
        /// </summary>
        // "the one you meant" IS the problem: GW2 reuses item names, so
        // the reader has a specific one in mind and the module cannot
        // tell which. "Suggestion list" is already established by
        // UnmatchedTextStatus, so this one can say "the list".
        public const string AmbiguousTextStatus =
            "Several items share that name - pick the one you meant from the list.";

        /// <summary>
        /// True when <paramref name="resolvedItemId"/> no longer describes
        /// what the search box reads and must be dropped. Case and
        /// surrounding whitespace do not count as divergence: the pick that
        /// set the id is re-matched case-insensitively at generate time, so
        /// retyping the same name in another case still means the same item.
        /// </summary>
        public static bool SelectionIsStale(int? resolvedItemId, string resolvedName, string boxText)
        {
            if (!resolvedItemId.HasValue)
            {
                return false;
            }

            return !NamesMatch(resolvedName, boxText);
        }

        /// <summary>
        /// The one comparison rule for item names in the input strip:
        /// trimmed, case-insensitive, null treated as empty.
        /// </summary>
        public static bool NamesMatch(string left, string right)
        {
            return string.Equals(
                (left ?? string.Empty).Trim(),
                (right ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Matches what was typed against <paramref name="results"/> by
        /// exact (trimmed, case-insensitive) name. Used to adopt a full
        /// name someone typed without ever opening the suggestion list.
        /// A partial name must stay unresolved rather than silently
        /// generating a plan for whichever result ranked first, and so must
        /// a name several items share: adopting the first of those would
        /// plan for an arbitrary one of them, with nothing on screen to say
        /// so - the name is identical and ids are never displayed.
        /// Results repeating one item id count as that one item.
        /// </summary>
        public static TypedNameMatch MatchTypedName(IReadOnlyList<ItemSearchResult> results, string text)
        {
            var none = new TypedNameMatch(TypedNameMatchKind.None, null);
            if (results == null)
            {
                return none;
            }

            string wanted = (text ?? string.Empty).Trim();
            if (wanted.Length == 0)
            {
                return none;
            }

            ItemSearchResult match = null;
            foreach (var result in results)
            {
                if (result == null || !NamesMatch(result.Name, wanted))
                {
                    continue;
                }

                if (match == null)
                {
                    match = result;
                    continue;
                }

                if (result.ItemId != match.ItemId)
                {
                    return new TypedNameMatch(TypedNameMatchKind.Ambiguous, null);
                }
            }

            return match == null
                ? none
                : new TypedNameMatch(TypedNameMatchKind.Unique, match);
        }

        /// <summary>
        /// What to tell the user when a Generate produced no request items:
        /// blank rows, typed-but-unmatched rows and a name shared by
        /// several items are three different mistakes needing three
        /// different fixes.
        /// </summary>
        public static string EmptyRequestStatus(bool anyRowHasText, bool anyAmbiguousName)
        {
            if (anyAmbiguousName)
            {
                return AmbiguousTextStatus;
            }

            return anyRowHasText ? UnmatchedTextStatus : NoItemsStatus;
        }

        /// <summary>
        /// What to add to the strip while a plan generates from SOME of the
        /// rows: the ones that resolved to nothing are silently absent from
        /// it otherwise, which is the same "the strip says one thing, the
        /// plan is another" mistake as planning for the wrong item.
        /// Null when every row with text resolved.
        /// <para>
        /// Four clauses share a 133-character band
        /// (StatusText.PlanStatusBudgetChars), so this one states the count
        /// and stops. Which rows they were is on screen: they are the input
        /// rows with text and no item.
        /// </para>
        /// </summary>
        public static string UnresolvedRowsNotice(int unresolvedRowCount)
        {
            if (unresolvedRowCount <= 0)
            {
                return null;
            }

            return unresolvedRowCount == 1
                ? "1 row left out"
                : unresolvedRowCount + " rows left out";
        }
    }

    /// <summary>
    /// How a typed name lined up with the search results it was looked up
    /// against.
    /// </summary>
    internal enum TypedNameMatchKind
    {
        /// <summary>Nothing carries exactly that name.</summary>
        None,

        /// <summary>One item carries that name - safe to adopt.</summary>
        Unique,

        /// <summary>
        /// Several different items carry that name. GW2 reuses item names
        /// freely (three distinct items are called "Amethyst Gold Ring"),
        /// so the name alone cannot say which one was meant.
        /// </summary>
        Ambiguous,
    }

    /// <summary>
    /// The outcome of <see cref="ItemRowSelection.MatchTypedName"/>.
    /// </summary>
    internal readonly struct TypedNameMatch
    {
        public TypedNameMatch(TypedNameMatchKind kind, ItemSearchResult result)
        {
            Kind = kind;
            Result = result;
        }

        public TypedNameMatchKind Kind { get; }

        /// <summary>
        /// The item to adopt - non-null only when <see cref="Kind"/> is
        /// <see cref="TypedNameMatchKind.Unique"/>.
        /// </summary>
        public ItemSearchResult Result { get; }
    }
}
