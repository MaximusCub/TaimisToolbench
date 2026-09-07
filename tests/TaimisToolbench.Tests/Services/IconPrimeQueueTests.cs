using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The queue behind the Snapshot tab's pre-open icon prime. The property
    /// that matters is that no picture is ever asked for twice, across
    /// refreshes as well as within one snapshot.
    /// </summary>
    public class IconPrimeQueueTests
    {
        [Fact]
        public void Enqueue_HandsOutTheItemsInNameOrderThenTheWallet()
        {
            var queue = new IconPrimeQueue();
            queue.Enqueue(Snapshot(
                new[] { Item(2, "Zephyrite Lens", "zephyr"), Item(1, "Amulet", "amulet") },
                new[] { Wallet(1, "Karma", "karma") }));

            Assert.Equal(new[] { "amulet", "zephyr", "karma" }, TakeAll(queue, 10));
        }

        [Fact]
        public void Enqueue_TakesBothAnItemsOwnIconAndItsSkinIcon()
        {
            var item = Item(1, "Berserker's Exalted Coat", "coat");
            item.SkinIconUrl = "skin";

            var queue = new IconPrimeQueue();
            queue.Enqueue(Snapshot(new[] { item }, new SnapshotWalletEntry[0]));

            Assert.Equal(new[] { "coat", "skin" }, TakeAll(queue, 10));
        }

        [Fact]
        public void Enqueue_CountsOneStackPerDistinctPicture()
        {
            // The owner's snapshot holds 1039 item entries over 910 item
            // ids: the same item in a bank slot and on a character is two
            // entries and one picture.
            var queue = new IconPrimeQueue();
            int added = queue.Enqueue(Snapshot(
                new[]
                {
                    Item(1, "Copper Ore", "ore"),
                    Item(1, "Copper Ore", "ore"),
                    Item(2, "Iron Ore", "iron"),
                },
                new SnapshotWalletEntry[0]));

            Assert.Equal(2, added);
            Assert.Equal(new[] { "ore", "iron" }, TakeAll(queue, 10));
        }

        [Fact]
        public void Enqueue_SkipsUrlsAlreadyHandedOutByAnEarlierSnapshot()
        {
            var queue = new IconPrimeQueue();
            var first = Snapshot(new[] { Item(1, "Copper Ore", "ore") }, new SnapshotWalletEntry[0]);
            queue.Enqueue(first);
            TakeAll(queue, 10);

            // A background refresh re-enqueues the whole account. Only the
            // picture it did not already hold may come back out.
            int added = queue.Enqueue(Snapshot(
                new[] { Item(1, "Copper Ore", "ore"), Item(2, "Iron Ore", "iron") },
                new SnapshotWalletEntry[0]));

            Assert.Equal(1, added);
            Assert.Equal(new[] { "iron" }, TakeAll(queue, 10));
        }

        [Fact]
        public void Enqueue_IgnoresEmptyUrlsAndAMissingSnapshot()
        {
            var queue = new IconPrimeQueue();

            Assert.Equal(0, queue.Enqueue(null));
            Assert.Equal(0, queue.Enqueue(new AccountSnapshot()));
            Assert.Equal(
                0,
                queue.Enqueue(Snapshot(new[] { Item(1, "Nameless", "") }, new SnapshotWalletEntry[0])));
            Assert.Equal(0, queue.Waiting);
            Assert.Equal(0, queue.Accounted);
        }

        [Fact]
        public void Take_SpendsAtMostItsBudgetAndResumesWhereItStopped()
        {
            var queue = new IconPrimeQueue();
            queue.Enqueue(Snapshot(
                new[] { Item(1, "A", "a"), Item(2, "B", "b"), Item(3, "C", "c") },
                new SnapshotWalletEntry[0]));

            var into = new List<string>();
            Assert.Equal(2, queue.Take(2, into));
            Assert.Equal(1, queue.Waiting);

            Assert.Equal(1, queue.Take(2, into));
            Assert.Equal(0, queue.Waiting);
            Assert.Equal(new[] { "a", "b", "c" }, into);

            // Drained: a further frame of budget costs nothing and hands
            // back nothing.
            Assert.Equal(0, queue.Take(2, into));
            Assert.Equal(3, into.Count);
        }

        [Fact]
        public void Take_RefusesANonPositiveBudgetAndANullList()
        {
            var queue = new IconPrimeQueue();
            queue.Enqueue(Snapshot(new[] { Item(1, "A", "a") }, new SnapshotWalletEntry[0]));

            Assert.Equal(0, queue.Take(0, new List<string>()));
            Assert.Equal(0, queue.Take(-1, new List<string>()));
            Assert.Equal(0, queue.Take(4, null));
            Assert.Equal(1, queue.Waiting);
        }

        [Fact]
        public void ABackgroundPrimeFinishesTheOwnersSnapshotInAKnownNumberOfFrames()
        {
            // 927 distinct pictures, four a frame: the pace the module
            // spends on a tab the player is not looking at.
            var items = new List<SnapshotItemEntry>();
            for (int i = 0; i < 927; i++)
            {
                items.Add(Item(i, "Item " + i, "url-" + i));
            }

            var queue = new IconPrimeQueue();
            queue.Enqueue(Snapshot(items.ToArray(), new SnapshotWalletEntry[0]));

            var into = new List<string>();
            int frames = 0;
            while (queue.Waiting > 0)
            {
                queue.Take(SnapshotIconWindow.BackgroundPrimePerFrame, into);
                frames++;
            }

            Assert.Equal(927, into.Count);
            Assert.Equal(
                SnapshotIconWindow.PrimeFrames(927, SnapshotIconWindow.BackgroundPrimePerFrame),
                frames);
        }

        private static List<string> TakeAll(IconPrimeQueue queue, int budget)
        {
            var into = new List<string>();
            queue.Take(budget, into);
            return into;
        }

        private static SnapshotItemEntry Item(int id, string name, string iconUrl)
        {
            return new SnapshotItemEntry { ItemId = id, Name = name, IconUrl = iconUrl };
        }

        private static SnapshotWalletEntry Wallet(int id, string name, string iconUrl)
        {
            return new SnapshotWalletEntry { CurrencyId = id, CurrencyName = name, IconUrl = iconUrl };
        }

        private static AccountSnapshot Snapshot(
            SnapshotItemEntry[] items, SnapshotWalletEntry[] wallet)
        {
            return new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>(items),
                Wallet = new List<SnapshotWalletEntry>(wallet),
            };
        }
    }
}
