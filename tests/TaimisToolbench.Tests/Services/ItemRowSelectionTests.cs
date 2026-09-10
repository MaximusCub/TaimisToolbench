using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Contracts;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The row-state decisions CraftingPlanView's search-box TextChanged
    /// handler and TriggerGenerate's typed-name resolution pass call -
    /// Blish-free, exercising the real ItemRowSelection production code.
    /// </summary>
    public class ItemRowSelectionTests
    {
        private static ItemSearchResult Result(int id, string name)
        {
            return new ItemSearchResult { ItemId = id, Name = name, IsPlanTarget = true };
        }

        [Fact]
        public void SelectionIsStale_NothingResolved_IsNeverStale()
        {
            Assert.False(ItemRowSelection.SelectionIsStale(null, null, "Mystic Clover"));
            Assert.False(ItemRowSelection.SelectionIsStale(null, null, ""));
        }

        [Fact]
        public void SelectionIsStale_TextStillMatchesResolvedName_IsNotStale()
        {
            Assert.False(ItemRowSelection.SelectionIsStale(19721, "Mystic Clover", "Mystic Clover"));
        }

        [Fact]
        public void SelectionIsStale_TextEditedAfterPick_IsStale()
        {
            // The bug this class exists for: the box reads one item while
            // the row still carries the id of the previously picked one.
            Assert.True(ItemRowSelection.SelectionIsStale(19685, "Deldrimor Steel Ingot", "Mystic Clover"));
        }

        [Fact]
        public void SelectionIsStale_SingleCharacterEditAfterPick_IsStale()
        {
            Assert.True(ItemRowSelection.SelectionIsStale(19721, "Mystic Clover", "Mystic Clove"));
            Assert.True(ItemRowSelection.SelectionIsStale(19721, "Mystic Clover", "Mystic Clovers"));
        }

        [Fact]
        public void SelectionIsStale_BoxCleared_IsStale()
        {
            Assert.True(ItemRowSelection.SelectionIsStale(19721, "Mystic Clover", ""));
            Assert.True(ItemRowSelection.SelectionIsStale(19721, "Mystic Clover", null));
        }

        [Fact]
        public void SelectionIsStale_CaseOrSurroundingWhitespaceOnly_IsNotStale()
        {
            // Retyping the same name differently cased still means the same
            // item - the generate-time match is case-insensitive too.
            Assert.False(ItemRowSelection.SelectionIsStale(19721, "Mystic Clover", "mystic clover"));
            Assert.False(ItemRowSelection.SelectionIsStale(19721, "Mystic Clover", "  Mystic Clover "));
        }

        [Fact]
        public void SelectionIsStale_ResolvedNameMissing_IsStale()
        {
            // An id with no name can never be shown to still match; drop it
            // rather than plan for an item the box does not name.
            Assert.True(ItemRowSelection.SelectionIsStale(19721, null, "Mystic Clover"));
        }

        [Fact]
        public void MatchTypedName_ExactName_ReturnsThatResult()
        {
            var results = new List<ItemSearchResult>
            {
                Result(19976, "Mystic Coin"),
                Result(19721, "Mystic Clover"),
            };

            var match = ItemRowSelection.MatchTypedName(results, "Mystic Clover");

            Assert.Equal(TypedNameMatchKind.Unique, match.Kind);
            Assert.Equal(19721, match.Result.ItemId);
        }

        [Fact]
        public void MatchTypedName_IsCaseAndWhitespaceInsensitive()
        {
            var results = new List<ItemSearchResult> { Result(19721, "Mystic Clover") };

            Assert.Equal(19721, ItemRowSelection.MatchTypedName(results, "  mystic CLOVER ").Result.ItemId);
        }

        [Fact]
        public void MatchTypedName_SeveralItemsShareTheName_IsAmbiguous()
        {
            // Real seed data: three distinct items are named "Amethyst Gold
            // Ring", and the provider sorts by name, so all of them land in
            // one result window. Adopting the first would plan for an
            // arbitrary one of them with nothing on screen to say which -
            // the names are identical and ids are never displayed.
            var results = new List<ItemSearchResult>
            {
                Result(13318, "Amethyst Gold Ring"),
                Result(13336, "Amethyst Gold Ring"),
                Result(13532, "Amethyst Gold Ring"),
            };

            var match = ItemRowSelection.MatchTypedName(results, "Amethyst Gold Ring");

            Assert.Equal(TypedNameMatchKind.Ambiguous, match.Kind);
            Assert.Null(match.Result);
        }

        [Fact]
        public void MatchTypedName_SameItemListedTwice_IsStillUnique()
        {
            // Duplicate ids are one item, not a choice to put to the user.
            var results = new List<ItemSearchResult>
            {
                Result(13318, "Amethyst Gold Ring"),
                Result(13318, "amethyst gold ring"),
            };

            var match = ItemRowSelection.MatchTypedName(results, "Amethyst Gold Ring");

            Assert.Equal(TypedNameMatchKind.Unique, match.Kind);
            Assert.Equal(13318, match.Result.ItemId);
        }

        [Fact]
        public void MatchTypedName_PartialName_MatchesNothing()
        {
            // A prefix must not silently adopt the top-ranked result.
            var results = new List<ItemSearchResult>
            {
                Result(19976, "Mystic Coin"),
                Result(19721, "Mystic Clover"),
            };

            var match = ItemRowSelection.MatchTypedName(results, "Mystic");

            Assert.Equal(TypedNameMatchKind.None, match.Kind);
            Assert.Null(match.Result);
        }

        [Fact]
        public void MatchTypedName_NoResultsOrBlankText_MatchesNothing()
        {
            var results = new List<ItemSearchResult> { Result(19721, "Mystic Clover") };

            Assert.Equal(TypedNameMatchKind.None, ItemRowSelection.MatchTypedName(null, "Mystic Clover").Kind);
            Assert.Equal(TypedNameMatchKind.None, ItemRowSelection.MatchTypedName(new List<ItemSearchResult>(), "Mystic Clover").Kind);
            Assert.Equal(TypedNameMatchKind.None, ItemRowSelection.MatchTypedName(results, "   ").Kind);
            Assert.Equal(TypedNameMatchKind.None, ItemRowSelection.MatchTypedName(results, null).Kind);
        }

        [Fact]
        public void MatchTypedName_SkipsNullEntriesAndNullNames()
        {
            var results = new List<ItemSearchResult>
            {
                null,
                new ItemSearchResult { ItemId = 1, Name = null },
                Result(19721, "Mystic Clover"),
            };

            Assert.Equal(19721, ItemRowSelection.MatchTypedName(results, "Mystic Clover").Result.ItemId);
        }

        [Fact]
        public void MatchTypedName_NullNameAgainstBlankText_StillMatchesNothing()
        {
            // Blank text short-circuits before any comparison, so a
            // null-named result can never be matched by an empty box.
            var results = new List<ItemSearchResult> { new ItemSearchResult { ItemId = 1, Name = null } };

            Assert.Equal(TypedNameMatchKind.None, ItemRowSelection.MatchTypedName(results, "").Kind);
        }

        [Fact]
        public void EmptyRequestStatus_NoRowHasText_AsksForASelection()
        {
            Assert.Equal(ItemRowSelection.NoItemsStatus, ItemRowSelection.EmptyRequestStatus(false, false));
        }

        [Fact]
        public void EmptyRequestStatus_TypedButUnresolved_PointsAtTheSuggestionList()
        {
            Assert.Equal(ItemRowSelection.UnmatchedTextStatus, ItemRowSelection.EmptyRequestStatus(true, false));
        }

        [Fact]
        public void EmptyRequestStatus_AmbiguousName_SaysTheNameIsShared()
        {
            // "No item matched what you typed" would be a lie here - several
            // did, which is exactly the problem.
            Assert.Equal(ItemRowSelection.AmbiguousTextStatus, ItemRowSelection.EmptyRequestStatus(true, true));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void EmptyRequestStatus_ReadsAsAStandaloneSentence(bool anyText, bool anyAmbiguous)
        {
            // These three now carry an acknowledgement dialog as well as the
            // status line. A dialog has none of the line's surrounding
            // context - no toolbar above it, no strip beside it - so each
            // has to say what happened and what to do about it on its own,
            // and none may be a fragment the status line was completing.
            string status = ItemRowSelection.EmptyRequestStatus(anyText, anyAmbiguous);

            Assert.False(string.IsNullOrWhiteSpace(status));
            Assert.EndsWith(".", status);
            Assert.True(status.Length > 20, $"too terse to stand alone: '{status}'");
            Assert.Equal(status.Trim(), status);
        }

        [Fact]
        public void EmptyRequestStatus_TellsTheThreeMistakesApart()
        {
            // The dialog carries whichever of these applies, so collapsing
            // any two would put "add an item" in front of a user whose box
            // is visibly full.
            var distinct = new[]
            {
                ItemRowSelection.EmptyRequestStatus(false, false),
                ItemRowSelection.EmptyRequestStatus(true, false),
                ItemRowSelection.EmptyRequestStatus(true, true),
            };

            Assert.Equal(3, distinct.Distinct().Count());
        }

        [Fact]
        public void UnresolvedRowsNotice_EverythingResolved_SaysNothing()
        {
            Assert.Null(ItemRowSelection.UnresolvedRowsNotice(0));
            Assert.Null(ItemRowSelection.UnresolvedRowsNotice(-1));
        }

        [Fact]
        public void UnresolvedRowsNotice_SomeRowsLeftOut_CountsThem()
        {
            // A plan built from only some of the rows must admit to the rest
            // rather than letting a requested item vanish silently.
            Assert.Equal("1 row left out", ItemRowSelection.UnresolvedRowsNotice(1));
            Assert.Equal("3 rows left out", ItemRowSelection.UnresolvedRowsNotice(3));
        }
    }
}
