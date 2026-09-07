using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The picture urls an account snapshot will draw, in the order the
    /// Snapshot tab lists them, handed out a few at a time so they can be
    /// fetched before that tab is first opened (Blish-free: the caller does
    /// the asking).
    /// <para>
    /// A url is handed out at most once for the life of the queue. A
    /// refresh re-enqueues the whole snapshot and only the urls the account
    /// did not already hold come back out, so the caller needs no
    /// already-asked check of its own.
    /// </para>
    /// <para>
    /// Not thread-safe. One instance, driven from the frame thread.
    /// </para>
    /// </summary>
    internal sealed class IconPrimeQueue
    {
        // Every url this queue has accounted for, queued or already handed
        // out. It is the whole dedup: a url in here is never enqueued again.
        // Bounded by the account's distinct icons - the owner's snapshot
        // holds 927 - and the strings are the snapshot's own.
        private readonly HashSet<string> _accounted = new HashSet<string>(StringComparer.Ordinal);

        private readonly List<string> _queued = new List<string>();
        private int _cursor;

        /// <summary>Urls enqueued and not yet handed out.</summary>
        public int Waiting
        {
            get { return _queued.Count - _cursor; }
        }

        /// <summary>Urls this queue has accounted for, in total.</summary>
        public int Accounted
        {
            get { return _accounted.Count; }
        }

        /// <summary>
        /// Adds every picture url in <paramref name="snapshot"/> that this
        /// queue has not seen before, in the order the Snapshot tab lists
        /// its rows: items by name, then the wallet by currency name. Both
        /// an item's own icon and the skin icon a transmuted copy shows are
        /// taken, because either can be the one a row draws.
        /// </summary>
        /// <returns>How many urls were added.</returns>
        public int Enqueue(AccountSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return 0;
            }

            int added = 0;

            var items = new List<SnapshotItemEntry>(snapshot.Items ?? new List<SnapshotItemEntry>());
            items.Sort(CompareItemsByName);
            for (int i = 0; i < items.Count; i++)
            {
                added += Add(items[i]?.IconUrl);
                added += Add(items[i]?.SkinIconUrl);
            }

            var wallet = new List<SnapshotWalletEntry>(
                snapshot.Wallet ?? new List<SnapshotWalletEntry>());
            wallet.Sort(CompareWalletByName);
            for (int i = 0; i < wallet.Count; i++)
            {
                added += Add(wallet[i]?.IconUrl);
            }

            return added;
        }

        /// <summary>
        /// Appends up to <paramref name="budget"/> waiting urls to
        /// <paramref name="into"/> and takes them off the queue. The list is
        /// the caller's, and is appended to rather than cleared, so a
        /// per-frame caller can reuse one scratch list.
        /// </summary>
        /// <returns>How many urls were appended.</returns>
        public int Take(int budget, List<string> into)
        {
            if (into == null || budget <= 0)
            {
                return 0;
            }

            int taken = 0;
            while (taken < budget && _cursor < _queued.Count)
            {
                into.Add(_queued[_cursor++]);
                taken++;
            }

            // Nothing left to hand out: drop the spent list rather than
            // hold every string in it until the next refresh replaces it.
            if (_cursor >= _queued.Count && _queued.Count > 0)
            {
                _queued.Clear();
                _cursor = 0;
            }

            return taken;
        }

        private int Add(string url)
        {
            if (string.IsNullOrEmpty(url) || !_accounted.Add(url))
            {
                return 0;
            }

            _queued.Add(url);
            return 1;
        }

        // Ordinal-insensitive by name with the item id as the tiebreak, the
        // order Services/SnapshotSearchResultBuilder puts the tab's rows in.
        private static int CompareItemsByName(SnapshotItemEntry left, SnapshotItemEntry right)
        {
            int byName = string.Compare(
                left?.Name ?? "", right?.Name ?? "", StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : (left?.ItemId ?? 0).CompareTo(right?.ItemId ?? 0);
        }

        private static int CompareWalletByName(SnapshotWalletEntry left, SnapshotWalletEntry right)
        {
            int byName = string.Compare(
                left?.CurrencyName ?? "", right?.CurrencyName ?? "",
                StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : (left?.CurrencyId ?? 0).CompareTo(right?.CurrencyId ?? 0);
        }
    }
}
