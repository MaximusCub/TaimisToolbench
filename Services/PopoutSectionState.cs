using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// One popout's list and what the player has ticked off it. Blish-free,
    /// so every rule the window obeys is decided and tested here rather than
    /// inside a control tree no test can build.
    /// <para>
    /// Session-scoped on purpose. Ticks survive closing and reopening the
    /// window, because a shopping trip is not over when the window is shut,
    /// and they do not survive a restart, because a sync clears them anyway.
    /// </para>
    /// </summary>
    internal sealed class PopoutSectionState
    {
        private readonly HashSet<string> _checked = new HashSet<string>(StringComparer.Ordinal);

        private IReadOnlyList<PlanRowViewModel> _rows = new List<PlanRowViewModel>();

        private IReadOnlyDictionary<int, int> _baseline = new Dictionary<int, int>();

        private Dictionary<PlanRowViewModel, string> _keys =
            new Dictionary<PlanRowViewModel, string>();

        /// <summary>
        /// The section this popout shows, fixed for the window's life.
        /// </summary>
        public PopoutSectionState(PlanSectionType sectionType)
        {
            SectionType = sectionType;
        }

        public PlanSectionType SectionType { get; private set; }

        /// <summary>
        /// Which column this popout's table is sorted by. Held beside the
        /// list rather than on the window, so closing and reopening a popout
        /// brings back the sort the player left it on, exactly as the ticks
        /// come back.
        /// </summary>
        public TableSortState<PlanTableColumn> Sort { get; private set; } =
            new TableSortState<PlanTableColumn>();

        /// <summary>The rows to draw, in the plan's own emission order.</summary>
        public IReadOnlyList<PlanRowViewModel> Rows
        {
            get { return _rows; }
        }

        /// <summary>How many rows carry a tick right now.</summary>
        public int CheckedCount
        {
            get { return _checked.Count; }
        }

        /// <summary>
        /// Starts this popout on a plan's section. Called for a freshly
        /// generated plan, which is a different list: ticks and the sync
        /// baseline both belong to the plan they were taken against.
        /// </summary>
        public void Adopt(
            IReadOnlyList<PlanRowViewModel> rows, IReadOnlyDictionary<int, int> baseline)
        {
            _rows = rows ?? new List<PlanRowViewModel>();
            _baseline = baseline ?? new Dictionary<int, int>();
            _keys = PopoutRowKey.Assign(_rows);
            _checked.Clear();
            Sort.Reset();
        }

        /// <summary>
        /// The identity a tick is stored under, or null for a row this
        /// state has never seen.
        /// </summary>
        public string KeyFor(PlanRowViewModel row)
        {
            string key;
            if (row != null && _keys.TryGetValue(row, out key))
            {
                return key;
            }

            return null;
        }

        public bool IsChecked(PlanRowViewModel row)
        {
            string key = KeyFor(row);
            return key != null && _checked.Contains(key);
        }

        /// <summary>
        /// Ticks or unticks one row. The row stays in the list either way:
        /// a ticked row greys out so the player can see what they have
        /// already done, and a list that deleted it would leave them no way
        /// to undo a misclick.
        /// </summary>
        public void SetChecked(PlanRowViewModel row, bool isChecked)
        {
            string key = KeyFor(row);
            if (key == null)
            {
                return;
            }

            if (isChecked)
            {
                _checked.Add(key);
            }
            else
            {
                _checked.Remove(key);
            }
        }

        /// <summary>
        /// Applies a completed account sync: rows the player has since
        /// acquired come off the list, and every tick is cleared, because a
        /// tick recorded a guess at progress and the account has now
        /// answered for real.
        /// </summary>
        public void ApplyRefresh(IReadOnlyList<SnapshotItemEntry> items)
        {
            var current = PopoutRemaining.CountItems(items);
            var result = PopoutRemaining.Apply(_rows, _baseline, current);
            _rows = result.Rows;
            _baseline = result.Baseline;
            _keys = PopoutRowKey.Assign(_rows);
            _checked.Clear();
        }

        /// <summary>
        /// What the window says when a sync did not complete. Nothing is
        /// applied: the list is exactly what it was, and the line says so
        /// rather than letting an unchanged list read as a confirmed one.
        /// </summary>
        public static string RefreshFailedStatus()
        {
            return "Account sync failed. The list below is unchanged.";
        }

        /// <summary>
        /// What the window says after a sync landed, naming the local time
        /// it was taken so a list left on screen never reads as live.
        /// </summary>
        public static string RefreshedStatus(DateTime localTime, int rowsRemaining)
        {
            string count = rowsRemaining == 1 ? "1 item left" : rowsRemaining + " items left";
            return count + ". Synced " + localTime.ToString("HH:mm") + ".";
        }
    }
}
