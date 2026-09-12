using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Shift-clicking a filter checkbox on the Snapshot tab isolates it
    /// within its own set, and inverts that set when it was already the only
    /// box ticked. These tests pin the rule; the tab applies it to the
    /// location set or the character set, never to both.
    /// </summary>
    public class FilterSetToggleTests
    {
        [Fact]
        public void ShiftClick_OnAnUncheckedBox_LeavesOnlyThatBoxChecked()
        {
            var next = FilterSetToggle.ShiftClick(new[] { true, true, false, true }, 2);

            Assert.Equal(new[] { false, false, true, false }, next.ToArray());
        }

        [Fact]
        public void ShiftClick_OnOneOfSeveralCheckedBoxes_LeavesOnlyThatBoxChecked()
        {
            var next = FilterSetToggle.ShiftClick(new[] { true, true, true }, 0);

            Assert.Equal(new[] { true, false, false }, next.ToArray());
        }

        [Fact]
        public void ShiftClick_OnTheOnlyCheckedBox_InvertsTheSet()
        {
            var next = FilterSetToggle.ShiftClick(new[] { false, true, false, false }, 1);

            Assert.Equal(new[] { true, false, true, true }, next.ToArray());
        }

        // Two shift-clicks in a row return the set to where it started, so
        // the gesture is its own undo.
        [Fact]
        public void ShiftClick_Twice_ReturnsToTheIsolatedState()
        {
            var once = FilterSetToggle.ShiftClick(new[] { true, true, true, true }, 3);
            var twice = FilterSetToggle.ShiftClick(once, 3);
            var thrice = FilterSetToggle.ShiftClick(twice, 3);

            Assert.Equal(new[] { false, false, false, true }, once.ToArray());
            Assert.Equal(new[] { true, true, true, false }, twice.ToArray());
            Assert.Equal(new[] { false, false, false, true }, thrice.ToArray());
        }

        [Fact]
        public void ShiftClick_OnEveryBoxUnchecked_LeavesOnlyThatBoxChecked()
        {
            var next = FilterSetToggle.ShiftClick(new[] { false, false, false }, 1);

            Assert.Equal(new[] { false, true, false }, next.ToArray());
        }

        [Fact]
        public void ShiftClick_OnTheOnlyBoxInASetOfOne_JustTogglesIt()
        {
            Assert.Equal(new[] { false }, FilterSetToggle.ShiftClick(new[] { true }, 0).ToArray());
            Assert.Equal(new[] { true }, FilterSetToggle.ShiftClick(new[] { false }, 0).ToArray());
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(99)]
        public void ShiftClick_OutsideTheSet_ChangesNothing(int clickedIndex)
        {
            var next = FilterSetToggle.ShiftClick(new[] { true, false, true }, clickedIndex);

            Assert.Equal(new[] { true, false, true }, next.ToArray());
        }

        [Fact]
        public void ShiftClick_OnANullOrEmptySet_ReturnsAnEmptySet()
        {
            Assert.Empty(FilterSetToggle.ShiftClick(null, 0));
            Assert.Empty(FilterSetToggle.ShiftClick(new List<bool>(), 0));
        }

        [Fact]
        public void ShiftClick_DoesNotWriteToTheStateItWasGiven()
        {
            var current = new[] { true, true, true };

            FilterSetToggle.ShiftClick(current, 1);

            Assert.Equal(new[] { true, true, true }, current);
        }
    }
}
