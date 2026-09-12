using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// What a rune, sigil or infusion sitting in a piece of gear is worth to
    /// the account: a row of its own, searchable, counted, and consumable by
    /// a plan, wherever the gear itself is. Drives the shipped row builder
    /// and then the shipped search, hold-line and reducer over what it
    /// produced.
    /// </summary>
    public class SocketedItemRowsTests : IDisposable
    {
        private const int Helm = 101544;
        private const int Coat = 101521;
        private const int Dusk = 30684;
        private const int Spear = 96978;
        private const int Rune = 24836;
        private const int Sigil = 24615;
        private const int Infusion = 49431;
        private const int LegendaryRune = 100147;
        private const int Striders = 48084;

        private const string Bank = AccountItemIndex.SourceBank;
        private const string Shared = AccountItemIndex.SourceSharedInventory;

        private readonly string _tempDir;

        public SocketedItemRowsTests()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(), "TaimisToolbench_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
            }
        }

        // ---- One stack's sockets ----
        [Fact]
        public void AddFor_GivesEachSocketedItemItsOwnRowUnderTheHostsSource()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(
                rows, Helm, Worn("Divineaxe"), 1, new[] { Rune }, new[] { Infusion });

            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { Rune, Infusion }, rows.Select(r => r.ItemId).ToArray());
            Assert.All(rows, r => Assert.Equal(1, r.Count));
            Assert.All(
                rows, r => Assert.Equal("Socketed:101544:Equipped:Divineaxe", r.Source));
        }

        [Fact]
        public void AddFor_KeysAStoredStackByThePlaceTheStackItselfIsIn()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(rows, Dusk, Bank, 1, new[] { Sigil }, null);
            SocketedItemRows.AddFor(rows, Dusk, Shared, 1, new[] { Sigil }, null);
            SocketedItemRows.AddFor(rows, Dusk, Bags("Divineaxe"), 1, new[] { Sigil }, null);

            Assert.Equal(
                new[]
                {
                    "Socketed:30684:Bank",
                    "Socketed:30684:SharedInventory",
                    "Socketed:30684:Character:Divineaxe",
                },
                rows.Select(r => r.Source).ToArray());
        }

        [Fact]
        public void ACharacterNamedBankCannotCollideWithTheBankItself()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(rows, Dusk, Bank, 1, new[] { Sigil }, null);
            SocketedItemRows.AddFor(rows, Dusk, Bags("Bank"), 1, new[] { Sigil }, null);
            SocketedItemRows.AddFor(rows, Dusk, Worn("Bank"), 1, new[] { Sigil }, null);

            Assert.Equal(3, rows.Select(r => r.Source).Distinct().Count());
            Assert.Equal(3, new AccountItemIndex(rows).GetSources(Sigil).Count);
        }

        [Fact]
        public void AddFor_AddsNothingWhenTheStackHasNoSockets()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(rows, Helm, Worn("Divineaxe"), 1, null, null);
            SocketedItemRows.AddFor(rows, Dusk, Bank, 1, null, null);
            SocketedItemRows.AddFor(rows, Dusk, Bank, 1, new int[0], new int[0]);

            // A stack whose sockets could not be read must make no claim at
            // all, rather than a claim that it is empty.
            Assert.Empty(rows);
        }

        [Fact]
        public void AddFor_ReadsTwoOfTheSameInfusionAsOneRowOfTwo()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(
                rows, Helm, Worn("Divineaxe"), 1, null, new[] { Infusion, Infusion });

            Assert.Single(rows);
            Assert.Equal(2, rows[0].Count);
        }

        [Fact]
        public void AddFor_SkipsAnIdTheCaptureCouldNotRead()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(rows, Dusk, Bank, 1, new[] { 0, Rune, -1 }, null);

            Assert.Single(rows);
            Assert.Equal(Rune, rows[0].ItemId);
        }

        [Fact]
        public void AddFor_LeavesTheHostRowAloneWhenItIsAlreadyInTheList()
        {
            var rows = new List<SnapshotItemEntry>
            {
                Row(Helm, 1, Worn("Divineaxe")),
            };

            SocketedItemRows.AddFor(rows, Helm, Worn("Divineaxe"), 1, new[] { Helm }, null);

            // The scan that merges repeated sockets must not reach back into
            // rows this call did not add, or a socket would be counted as
            // another copy of the gear.
            Assert.Equal(2, rows.Count);
            Assert.Equal(1, rows[0].Count);
            Assert.Equal(1, rows[1].Count);
            Assert.True(AccountItemIndex.IsSocketedSource(rows[1].Source));
        }

        // ---- What the account then holds ----
        [Fact]
        public void SixArmourSlotsCarryingOneRuneAreSixRunes()
        {
            var rows = SixPieceSet("Divineaxe");
            var index = new AccountItemIndex(rows);

            Assert.Equal(6, Total(index, Rune));
        }

        [Fact]
        public void FourCharactersInThatSetAreTwentyFourRunes()
        {
            var rows = new List<SnapshotItemEntry>();
            foreach (string name in new[] { "Divineaxe", "Apoyu", "Zoe", "Taimi" })
            {
                rows.AddRange(SixPieceSet(name));
            }

            var index = new AccountItemIndex(rows);

            Assert.Equal(24, Total(index, Rune));
        }

        [Fact]
        public void TheSameRuneInFourPlacesIsFourRunes()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Coat, Bags("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Helm, Bank, 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Helm, Shared, 1, new[] { Rune }, null);

            var index = new AccountItemIndex(items);

            Assert.Equal(4, index.GetSources(Rune).Count);
            Assert.Equal(4, Total(index, Rune));
        }

        // ---- Where the Snapshot tab says they are ----
        [Fact]
        public void AWornPieceLeadsWithTheSlotAndBracketsTheGear()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Helm, "Obsidian Heavy Helmet", 1, Worn("Divineaxe")),
            };
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var row = SearchOneRow(items, "Scholar");

            Assert.Equal(Rune, row.ItemId);
            Assert.Equal(1, row.TotalCount);
            Assert.Equal(
                "Equipped: Divineaxe (in Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void ABankedPieceLeadsWithTheBank()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Dusk, "Dusk", 1, Bank),
            };
            SocketedItemRows.AddFor(items, Dusk, Bank, 1, new[] { Sigil }, null);
            Name(items, Sigil, "Superior Sigil of Rage");

            var row = SearchOneRow(items, "Rage");

            Assert.Equal(1, row.TotalCount);
            Assert.Equal("Bank (in Dusk)", SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void APieceInACharactersBagsLeadsWithThatCharactersBags()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Spear, "Suun's Impaler", 1, Bags("Divineaxe")),
            };
            SocketedItemRows.AddFor(items, Spear, Bags("Divineaxe"), 1, null, new[] { Infusion });
            Name(items, Infusion, "+7 Agony Infusion");

            var row = SearchOneRow(items, "Agony");

            Assert.Equal(1, row.TotalCount);
            Assert.Equal(
                "Bags: Divineaxe (in Suun's Impaler)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void APieceInSharedInventoryLeadsWithSharedInventory()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Dusk, "Dusk", 1, Shared),
            };
            SocketedItemRows.AddFor(items, Dusk, Shared, 1, new[] { Sigil }, null);
            Name(items, Sigil, "Superior Sigil of Rage");

            var row = SearchOneRow(items, "Rage");

            Assert.Equal("Shared Inventory (in Dusk)", SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void EveryPlaceIsCountedAndReadOutSeparately()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Dusk, "Dusk", 1, Bank),
                Named(Spear, "Suun's Impaler", 1, Worn("Divineaxe")),
            };
            SocketedItemRows.AddFor(
                items, Dusk, Bank, 1, null, new[] { Infusion, Infusion });
            SocketedItemRows.AddFor(
                items, Spear, Worn("Divineaxe"), 1, null, new[] { Infusion });
            Name(items, Infusion, "+7 Agony Infusion");

            var row = SearchOneRow(items, "Agony");

            Assert.Equal(3, row.TotalCount);
            Assert.Equal(
                "Equipped: Divineaxe (in Suun's Impaler) (1)  Bank (in Dusk) (2)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void TwoBankedPiecesShareOneBankLabel()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Dusk, "Dusk", 1, Bank),
                Named(Spear, "Suun's Impaler", 1, Bank),
            };
            SocketedItemRows.AddFor(items, Dusk, Bank, 1, new[] { Sigil }, null);
            SocketedItemRows.AddFor(items, Spear, Bank, 1, new[] { Sigil }, null);
            Name(items, Sigil, "Superior Sigil of Rage");

            var row = SearchOneRow(items, "Rage");

            // One bracket over both, because the place is the same place.
            Assert.Equal(2, row.TotalCount);
            Assert.Equal(
                "Bank (in Dusk, Suun's Impaler)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void ALooseStackAndASocketInOnePlaceStayApart()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Sigil, "Superior Sigil of Rage", 3, Bank),
                Named(Dusk, "Dusk", 1, Bank),
            };
            SocketedItemRows.AddFor(items, Dusk, Bank, 1, new[] { Sigil }, null);

            var row = SearchOneRow(items, "Rage");

            Assert.Equal(4, row.TotalCount);
            Assert.Equal(
                "Bank (3)  Bank (in Dusk) (1)", SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void ASlotDrawingALegendaryFromTheArmoryStillReportsItsSockets()
        {
            // The gear itself is the armory's row. Only its sockets come
            // from the character, which is the whole of what used to be
            // lost.
            var items = new List<SnapshotItemEntry>
            {
                Named(Helm, "Obsidian Heavy Helmet", 1, AccountItemIndex.SourceLegendaryArmory),
            };
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Sigil }, null);
            Name(items, Sigil, "Superior Sigil of Force");

            var row = SearchOneRow(items, "Force");

            Assert.Equal(1, row.TotalCount);
            Assert.Equal(
                "Equipped: Divineaxe (in Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void TwoPiecesOnOneCharacterNameThatCharacterOnce()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Helm, "Obsidian Heavy Helmet", 1, Worn("Divineaxe")),
                Named(Coat, "Obsidian Heavy Breastplate", 1, Worn("Divineaxe")),
            };
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Coat, Worn("Divineaxe"), 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var row = SearchOneRow(items, "Scholar");

            Assert.Equal(2, row.TotalCount);
            Assert.Equal(
                "Equipped: Divineaxe (in Obsidian Heavy Breastplate, "
                    + "Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        // ---- One name per character ----
        [Fact]
        public void ARuneInSevenPiecesOnOneCharacterNamesThatCharacterOnce()
        {
            // Shapes from the owner's capture of 2026-09-11: Superior Rune
            // of the Pack across Oso Grueso's six Zojja's pieces and its
            // aquatic helm.
            var items = new List<SnapshotItemEntry>();
            foreach (var piece in PackSet())
            {
                items.Add(Named(piece.Key, piece.Value, 1, Worn("Oso Grueso")));
                SocketedItemRows.AddFor(
                    items, piece.Key, Worn("Oso Grueso"), 1, new[] { Rune }, null);
            }

            Name(items, Rune, "Superior Rune of the Pack");

            var row = SearchOneRow(items, "Rune of the Pack");

            Assert.Equal(7, row.TotalCount);
            Assert.Equal(
                "Equipped: Oso Grueso (in Zojja's Striders, Zojja's Guise, "
                    + "Zojja's Grips, Zojja's Visage, Zojja's Leggings, "
                    + "Zojja's Shoulderguard, Wegloop's Air Mask)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void TwoCharactersWearingTheSameRuneEachGetTheirOwnPieceList()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Helm, "Obsidian Heavy Helmet", 1, Worn("Divineaxe")),
                Named(Coat, "Obsidian Heavy Breastplate", 1, Worn("Divineaxe")),
                Named(Striders, "Zojja's Striders", 1, Worn("Oso Grueso")),
            };
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Coat, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(
                items, Striders, Worn("Oso Grueso"), 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Pack");

            var row = SearchOneRow(items, "Rune of the Pack");

            Assert.Equal(3, row.TotalCount);
            Assert.Equal(
                "Equipped: Divineaxe (in Obsidian Heavy Breastplate, "
                    + "Obsidian Heavy Helmet), Oso Grueso (in Zojja's Striders)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void AGroupHoldingOnePerPieceItNamesPrintsNoCount()
        {
            // Three names and three copies say the same thing twice, so the
            // line says it once. The loose stack beside them does not, so
            // every place on that line takes its count back.
            var items = new List<SnapshotItemEntry>
            {
                Named(Helm, "Obsidian Heavy Helmet", 1, Worn("Divineaxe")),
                Named(Coat, "Obsidian Heavy Breastplate", 1, Worn("Divineaxe")),
            };
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Coat, Worn("Divineaxe"), 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Pack");

            Assert.Equal(
                "Equipped: Divineaxe (in Obsidian Heavy Breastplate, "
                    + "Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(SearchOneRow(items, "Rune of the Pack").Breakdown));

            items.Add(Named(Rune, "Superior Rune of the Pack", 5, Bank));

            Assert.Equal(
                "Equipped: Divineaxe (in Obsidian Heavy Breastplate, "
                    + "Obsidian Heavy Helmet) (2)  Bank (5)",
                SnapshotHoldLine.Format(SearchOneRow(items, "Rune of the Pack").Breakdown));
        }

        [Fact]
        public void ARuneTheCharacterOwnsOutrightIsItsOwnPlaceBesideItsSockets()
        {
            // The equipment endpoint reports a rune a character owns but has
            // fitted into nothing as an equipment entry of its own. That is
            // a real copy, and it holds no gear, so it never joins the
            // bracket naming the pieces beside it.
            var items = new List<SnapshotItemEntry>
            {
                Named(Rune, "Superior Rune of the Defender", 1, Worn("Divineaxe")),
                Named(Helm, "Obsidian Heavy Helmet", 1, Worn("Divineaxe")),
            };
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);

            var row = SearchOneRow(items, "Defender");

            Assert.Equal(2, row.TotalCount);
            Assert.Equal(
                "Equipped: Divineaxe, Divineaxe (in Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void ARuneReportedOnItsOwnSurvivesTheArmorySettlement()
        {
            // The suppression rule drops socket rows alone. A rune the
            // character owns outright is not one, so it keeps its row even
            // while the armory holds the gear it sits beside.
            var items = new List<SnapshotItemEntry>
            {
                Named(Rune, "Superior Rune of the Defender", 1, Worn("Divineaxe")),
                Named(Helm, "Obsidian Heavy Helmet", 1, AccountItemIndex.SourceLegendaryArmory),
            };

            SocketedItemRows.SettleArmoryOwned(
                items, new List<SnapshotArmoryEquip>(), new HashSet<int> { Helm });

            Assert.Equal(2, items.Count);
            Assert.Equal(
                "Equipped: Divineaxe",
                SnapshotHoldLine.Format(SearchOneRow(items, "Defender").Breakdown));
        }

        [Fact]
        public void ASnapshotFromBeforeTheKeyCarriedItsPlaceStillNamesOnePlaceOnce()
        {
            // Builds before the socket key carried the whole container key
            // wrote the bare character name there. Those rows read as one
            // unrecognized place, which is what a refresh replaces.
            var items = new List<SnapshotItemEntry>
            {
                Named(Helm, "Obsidian Heavy Helmet", 1, Worn("Divineaxe")),
                Named(Coat, "Obsidian Heavy Breastplate", 1, Worn("Divineaxe")),
                Named(Rune, "Superior Rune of the Pack", 1, "Socketed:101544:Divineaxe"),
                Named(Rune, "Superior Rune of the Pack", 1, "Socketed:101521:Divineaxe"),
            };

            var row = SearchOneRow(items, "Rune of the Pack");

            Assert.Equal(2, row.TotalCount);
            Assert.Equal(
                "Divineaxe (in Obsidian Heavy Breastplate, Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void ThePlaceIsNamedWithoutTheGearWhenTheCaptureCannotNameIt()
        {
            // The host id resolves to nothing, and an id is never shown
            // (repo invariant), so the place names itself alone.
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Dusk, Bank, 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var row = SearchOneRow(items, "Scholar");

            Assert.Equal(
                "Equipped: Divineaxe  Bank", SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void ADamagedSocketKeyNamesNoIdOnTheLine()
        {
            // A key too damaged to read a place out of still reaches the
            // line, and the line still shows no id.
            var items = new List<SnapshotItemEntry>
            {
                Named(Rune, "Superior Rune of the Scholar", 1, "Socketed:not-an-id"),
                Named(Sigil, "Superior Sigil of Rage", 1, "Socketed:30684:Cellar"),
            };

            Assert.Equal(
                "Unknown", SnapshotHoldLine.Format(SearchOneRow(items, "Scholar").Breakdown));
            Assert.Equal(
                "Cellar", SnapshotHoldLine.Format(SearchOneRow(items, "Rage").Breakdown));
        }

        // ---- What the source filter hides ----
        [Fact]
        public void UncheckingACharacterHidesWhatIsSocketedIntoItsGear()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Coat, Bags("Divineaxe"), 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var filter = new SnapshotSourceFilter();
            filter.UncheckedCharacters.Add("Divineaxe");

            Assert.Empty(SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(items),
                new AccountItemIndex(items),
                "",
                filter,
                null));
        }

        [Fact]
        public void UncheckingTheBankHidesWhatIsSocketedIntoABankedPiece()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Dusk, Bank, 1, new[] { Sigil }, null);
            SocketedItemRows.AddFor(items, Dusk, Shared, 1, new[] { Sigil }, null);
            Name(items, Sigil, "Superior Sigil of Rage");

            var filter = new SnapshotSourceFilter { Bank = false };

            var row = Assert.Single(SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(items),
                new AccountItemIndex(items),
                "",
                filter,
                null));

            Assert.Equal(1, row.TotalCount);
            Assert.Equal("Shared Inventory", SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void SearchingTheWearersNameFindsWhatIsSocketedIntoItsGear()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var row = SearchOneRow(items, "Divineaxe");

            Assert.Equal(Rune, row.ItemId);
        }

        // ---- The armory's own copies ----
        [Fact]
        public void ALegendaryRuneMakesNoRowAndNamesItsWearerOnTheArmoryRow()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(LegendaryRune, "Legendary Rune", 6, AccountItemIndex.SourceLegendaryArmory),
            };
            foreach (int host in new[] { Helm, Coat })
            {
                SocketedItemRows.AddFor(
                    items, host, Worn("Divineaxe"), 1, new[] { LegendaryRune }, null);
            }

            SocketedItemRows.AddFor(
                items, Helm, Worn("Apoyu"), 1, new[] { LegendaryRune }, null);

            var snapshot = new AccountSnapshot { Items = items };
            SocketedItemRows.SettleArmoryOwned(
                snapshot.Items, snapshot.LegendaryArmoryEquipped,
                new HashSet<int> { LegendaryRune });

            // One armory row, still carrying the armory's own count.
            Assert.Single(snapshot.Items);
            Assert.Equal(6, snapshot.Items[0].Count);

            var row = SearchOneRow(snapshot.Items, "Legendary Rune", snapshot);
            Assert.Equal(6, row.TotalCount);
            Assert.Equal(
                "Legendary Armory - Equipped: Divineaxe, Apoyu",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void ALegendaryRuneInStoredGearMakesNoRowAndNamesThatPlaceInstead()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(LegendaryRune, "Legendary Rune", 6, AccountItemIndex.SourceLegendaryArmory),
                Named(Helm, "Obsidian Heavy Helmet", 1, Bank),
            };
            SocketedItemRows.AddFor(items, Helm, Bank, 1, new[] { LegendaryRune }, null);

            var snapshot = new AccountSnapshot { Items = items };
            SocketedItemRows.SettleArmoryOwned(
                snapshot.Items, snapshot.LegendaryArmoryEquipped,
                new HashSet<int> { LegendaryRune });

            // The armory's count must not gain one for a banked piece that
            // is drawing on it.
            Assert.Equal(2, snapshot.Items.Count);
            Assert.Equal(
                6, new AccountItemIndex(snapshot.Items).GetQuantity(
                    LegendaryRune, AccountItemIndex.SourceLegendaryArmory));

            var row = SearchOneRow(snapshot.Items, "Legendary Rune", snapshot);
            Assert.Equal(6, row.TotalCount);
            Assert.Equal(
                "Legendary Armory - Bank (in Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void UncheckingTheBankStopsNamingABankedPieceUnderTheArmory()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(LegendaryRune, "Legendary Rune", 6, AccountItemIndex.SourceLegendaryArmory),
                Named(Helm, "Obsidian Heavy Helmet", 1, Bank),
            };
            SocketedItemRows.AddFor(items, Helm, Bank, 1, new[] { LegendaryRune }, null);

            var snapshot = new AccountSnapshot { Items = items };
            SocketedItemRows.SettleArmoryOwned(
                snapshot.Items, snapshot.LegendaryArmoryEquipped,
                new HashSet<int> { LegendaryRune });

            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(snapshot.Items),
                new AccountItemIndex(snapshot.Items),
                "Legendary Rune",
                new SnapshotSourceFilter { Bank = false },
                null,
                null,
                SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(snapshot));

            Assert.Equal(
                "Legendary Armory", SnapshotHoldLine.Format(Assert.Single(rows).Breakdown));
        }

        [Fact]
        public void SettleArmoryOwned_LeavesEveryOtherRowAlone()
        {
            var items = new List<SnapshotItemEntry>
            {
                Row(LegendaryRune, 2, AccountItemIndex.SourceBank),
                Row(Rune, 3, AccountItemIndex.SourceBank),
            };
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Dusk, Bank, 1, new[] { Rune }, null);

            SocketedItemRows.SettleArmoryOwned(items, null, new HashSet<int> { LegendaryRune });

            // A bank stack is the account's own copy however the armory
            // reports the same id, and an ordinary rune is nothing to do
            // with the armory at all.
            Assert.Equal(4, items.Count);
            Assert.Equal(2, new AccountItemIndex(items).GetQuantity(
                LegendaryRune, AccountItemIndex.SourceBank));
            Assert.Equal(5, Total(new AccountItemIndex(items), Rune));
        }

        [Fact]
        public void SettleArmoryOwned_DoesNothingWithoutAnArmoryReading()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Dusk, Bank, 1, new[] { LegendaryRune }, null);

            SocketedItemRows.SettleArmoryOwned(items, null, new HashSet<int>());
            SocketedItemRows.SettleArmoryOwned(items, null, null);
            SocketedItemRows.SettleArmoryOwned(null, null, new HashSet<int> { LegendaryRune });

            Assert.Single(items);
        }

        // ---- What already read the socket lists ----
        [Fact]
        public void TheRowsChangeNeitherTheSocketIndexNorTheRuneSetCount()
        {
            var gearOnly = new List<SnapshotItemEntry>
            {
                Sockets(
                    Named(Helm, "Obsidian Heavy Helmet", 1, Worn("Divineaxe")),
                    new List<int> { Rune }),
                Sockets(Named(Dusk, "Dusk", 1, Bank), new List<int> { Sigil }),
            };

            var withRows = new List<SnapshotItemEntry>(gearOnly);
            SocketedItemRows.AddFor(withRows, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(withRows, Dusk, Bank, 1, new[] { Sigil }, null);

            var before = SocketedUpgradeIndex.Build(gearOnly);
            var after = SocketedUpgradeIndex.Build(withRows);
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(before[Helm].Upgrades, after[Helm].Upgrades);
            Assert.Equal(before[Dusk].Upgrades, after[Dusk].Upgrades);

            Assert.Equal(
                new EquippedRuneSetIndex(gearOnly).WornCopies(Helm, Rune),
                new EquippedRuneSetIndex(withRows).WornCopies(Helm, Rune));
        }

        // ---- What a plan does with them ----
        [Fact]
        public void APlanSpendsALooseRuneBeforeOneItHasToPullOutOfGear()
        {
            var items = new List<SnapshotItemEntry>
            {
                Row(Rune, 1, AccountItemIndex.SourceBank),
            };
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);

            var used = ReduceForOneRune(items);

            Assert.Equal(AccountItemIndex.SourceBank, used.Sources[0].Source);
        }

        [Fact]
        public void APlanSpendsARuneInStoredGearBeforeOneInGearBeingWorn()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Helm, Bank, 1, new[] { Rune }, null);

            var used = ReduceForOneRune(items);

            // Pulling a rune out of a worn set breaks something the player
            // is using; pulling one out of a banked piece breaks nothing.
            Assert.Equal("Socketed:101544:Bank", used.Sources[0].Source);
        }

        // ---- Through a real store ----
        [Fact]
        public void TheRowsSurviveASaveAndLoad()
        {
            var store = new SnapshotStore(_tempDir);
            var snapshot = new AccountSnapshot
            {
                CapturedAt = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
                Items = new List<SnapshotItemEntry>
                {
                    Named(Helm, "Obsidian Heavy Helmet", 1, Worn("Divineaxe")),
                    Named(Dusk, "Dusk", 1, Bank),
                },
            };
            SocketedItemRows.AddFor(
                snapshot.Items, Helm, Worn("Divineaxe"), 1, new[] { Rune }, new[] { Infusion });
            SocketedItemRows.AddFor(snapshot.Items, Dusk, Bank, 1, null, new[] { Infusion });
            Name(snapshot.Items, Rune, "Superior Rune of the Scholar");
            Name(snapshot.Items, Infusion, "+7 Agony Infusion");

            store.Save(snapshot);
            var loaded = store.LoadLatest();

            var index = new AccountItemIndex(loaded.Items);
            Assert.Equal(1, Total(index, Rune));
            Assert.Equal(2, Total(index, Infusion));

            var row = SearchOneRow(loaded.Items, "Agony");
            Assert.Equal(
                "Equipped: Divineaxe (in Obsidian Heavy Helmet)  Bank (in Dusk)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        // ---- Helpers ----
        private static string Worn(string characterName)
        {
            return AccountItemIndex.CharacterEquipmentSourcePrefix + characterName;
        }

        private static string Bags(string characterName)
        {
            return AccountItemIndex.CharacterSourcePrefix + characterName;
        }

        /// <summary>
        /// Seven pieces of one character's gear, by the ids and names the
        /// owner's capture of 2026-09-11 carried, in the order the hold line
        /// reads them out.
        /// </summary>
        private static List<KeyValuePair<int, string>> PackSet()
        {
            return new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(48084, "Zojja's Striders"),
                new KeyValuePair<int, string>(48085, "Zojja's Guise"),
                new KeyValuePair<int, string>(48086, "Zojja's Grips"),
                new KeyValuePair<int, string>(48087, "Zojja's Visage"),
                new KeyValuePair<int, string>(48088, "Zojja's Leggings"),
                new KeyValuePair<int, string>(48089, "Zojja's Shoulderguard"),
                new KeyValuePair<int, string>(79838, "Wegloop's Air Mask"),
            };
        }

        private static List<SnapshotItemEntry> SixPieceSet(string characterName)
        {
            var rows = new List<SnapshotItemEntry>();
            for (int piece = 0; piece < 6; piece++)
            {
                rows.Add(Row(Helm + piece, 1, Worn(characterName)));
                SocketedItemRows.AddFor(
                    rows, Helm + piece, Worn(characterName), 1, new[] { Rune }, null);
            }

            return rows;
        }

        private static int Total(AccountItemIndex index, int itemId)
        {
            return index.GetSources(itemId).Sum(s => index.GetQuantity(itemId, s));
        }

        private static UsedMaterial ReduceForOneRune(IReadOnlyList<SnapshotItemEntry> items)
        {
            var node = new RecipeNode { Id = Rune, Quantity = 1, IngredientType = "Item" };
            var root = new RecipeNode
            {
                Id = 9000,
                Quantity = 1,
                IngredientType = "Item",
                Recipes =
                {
                    new RecipeOption
                    {
                        RecipeId = 1,
                        OutputCount = 1,
                        CraftsNeeded = 1,
                        Ingredients = { node },
                    },
                },
            };
            RecipeNodeIds.Assign(root);

            var result = new InventoryReducer().Reduce(
                root, new AccountItemIndex(items), "Divineaxe");

            var used = Assert.Single(result.UsedMaterials);
            Assert.Equal(Rune, used.ItemId);
            Assert.Equal(1, used.QuantityUsed);
            return used;
        }

        private static SnapshotSearchRow SearchOneRow(
            IReadOnlyList<SnapshotItemEntry> items, string search, AccountSnapshot snapshot = null)
        {
            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(items),
                new AccountItemIndex(items),
                search,
                null,
                null,
                null,
                SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(snapshot));
            return Assert.Single(rows);
        }

        private static SnapshotItemEntry Row(int itemId, int count, string source)
        {
            return new SnapshotItemEntry { ItemId = itemId, Count = count, Source = source };
        }

        private static SnapshotItemEntry Named(
            int itemId, string name, int count, string source)
        {
            var entry = Row(itemId, count, source);
            entry.Name = name;
            return entry;
        }

        private static SnapshotItemEntry Sockets(SnapshotItemEntry entry, List<int> upgrades)
        {
            entry.Upgrades = upgrades;
            return entry;
        }

        /// <summary>
        /// Stands in for the resolve pass that names every row after the
        /// capture, which is what the Snapshot tab searches on.
        /// </summary>
        private static void Name(IEnumerable<SnapshotItemEntry> items, int itemId, string name)
        {
            foreach (var entry in items)
            {
                if (entry.ItemId == itemId)
                {
                    entry.Name = name;
                }
            }
        }
    }
}
