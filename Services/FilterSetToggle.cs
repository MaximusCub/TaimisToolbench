using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What a shift-click does to one set of filter checkboxes on the
    /// Snapshot tab. The tab has two independent sets, locations and
    /// characters, and a shift-click acts on the clicked box's own set only.
    /// Blish-free so the rule can be tested without a control.
    /// </summary>
    internal static class FilterSetToggle
    {
        /// <summary>
        /// The set's new checked states after a shift-click on
        /// <paramref name="clickedIndex"/>. Two outcomes:
        /// <list type="bullet">
        /// <item>The clicked box was the only one checked: it goes off and
        /// every other box goes on.</item>
        /// <item>Anything else: the clicked box goes on and every other box
        /// goes off.</item>
        /// </list>
        /// <para>
        /// <paramref name="current"/> is the state the user saw when they
        /// clicked, before the control toggled itself. An index outside the
        /// set, or a null set, returns the state unchanged. Returns a new
        /// list, never null, and never writes to the input.
        /// </para>
        /// </summary>
        public static List<bool> ShiftClick(IReadOnlyList<bool> current, int clickedIndex)
        {
            var result = new List<bool>();
            if (current == null)
            {
                return result;
            }

            for (int i = 0; i < current.Count; i++)
            {
                result.Add(current[i]);
            }

            if (clickedIndex < 0 || clickedIndex >= current.Count)
            {
                return result;
            }

            bool isolated = current[clickedIndex] && CheckedCount(current) == 1;

            for (int i = 0; i < result.Count; i++)
            {
                result[i] = i == clickedIndex ? !isolated : isolated;
            }

            return result;
        }

        private static int CheckedCount(IReadOnlyList<bool> current)
        {
            int count = 0;
            for (int i = 0; i < current.Count; i++)
            {
                if (current[i])
                {
                    count++;
                }
            }

            return count;
        }
    }
}
