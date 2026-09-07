using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The transmuted-name path end to end: which copies let a row take a
    /// skin's name, what the row is then called, what still finds it, and
    /// what the tooltip reads. The stat block comes from verbatim live
    /// /v2/items JSON through the real parser and the real factory, so the
    /// tooltip assertions are about the game's own data.
    /// </summary>
    public class TransmutedNameTests
    {
        private const int Warfists = 48074;

        private const string ItemIcon = "https://render.guildwars2.com/file/item.png";

        private static SnapshotItemEntry Stack(
            string source, string name = "Zojja's Warfists", int skinId = 0,
            string skinName = "", string skinIconUrl = "")
        {
            return new SnapshotItemEntry
            {
                ItemId = Warfists,
                Name = name,
                IconUrl = ItemIcon,
                Count = 1,
                Source = source,
                SkinId = skinId,
                SkinName = skinName,
                SkinIconUrl = skinIconUrl,
            };
        }

        // A skin the capture resolved whole: both halves present, which is
        // the only shape TransmutedSkin accepts.
        private static SnapshotItemEntry Skinned(
            string source, int skinId, string skinName, string skinIconUrl = null)
        {
            return Stack(
                source,
                skinId: skinId,
                skinName: skinName,
                skinIconUrl: skinIconUrl ?? IconFor(skinName));
        }

        private static string IconFor(string skinName)
        {
            return "https://render.guildwars2.com/file/" + skinName.Replace(" ", "") + ".png";
        }

        private static List<SnapshotSearchRow> Rows(
            IReadOnlyList<SnapshotItemEntry> items,
            string searchText,
            IReadOnlyDictionary<int, IReadOnlyList<TransmutedItemCopy>> transmuted,
            SnapshotSourceFilter sourceFilter = null)
        {
            return SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(items),
                new AccountItemIndex(items),
                searchText,
                sourceFilter ?? new SnapshotSourceFilter(),
                activeCharacterName: null,
                transmutedCopies: transmuted);
        }

        // The whole account is checked, so every copy counts.
        private static TransmutedSkin Shown(
            IReadOnlyDictionary<int, IReadOnlyList<TransmutedItemCopy>> index)
        {
            return TransmutedNameIndex.AgreedSkin(index[Warfists], null);
        }

        private static string[] SkinNamesFinding(
            IReadOnlyDictionary<int, IReadOnlyList<TransmutedItemCopy>> index,
            params string[] searches)
        {
            var found = new List<string>();
            foreach (string search in searches)
            {
                if (TransmutedNameIndex.AnySkinNameMatches(index[Warfists], search, null))
                {
                    found.Add(search);
                }
            }

            return found.ToArray();
        }

        private static SnapshotSourceFilter WithoutCharacter(string name)
        {
            return new SnapshotSourceFilter
            {
                UncheckedCharacters = new HashSet<string>(StringComparer.Ordinal) { name },
            };
        }

        // One transmuted copy on a character, one bare copy in the bank.
        private static List<SnapshotItemEntry> OneSkinnedOneBare()
        {
            return new List<SnapshotItemEntry>
            {
                Skinned("Character:Alice", 5432, "Glyphic Gauntlets"),
                Stack("Bank"),
            };
        }

        // ---- TransmutedNameIndex ----
        [Fact]
        public void Build_OneSkinnedCopy_ReportsTheSkinAsTheNameToShow()
        {
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                Skinned("Bank", 5432, "Glyphic Gauntlets"),
            });

            Assert.Equal("Glyphic Gauntlets", Shown(index).Name);
            Assert.Equal(
                new[] { "Glyphic Gauntlets" },
                SkinNamesFinding(index, "Glyphic Gauntlets", "Chaos Gloves"));
        }

        [Fact]
        public void Build_EveryCopyWearsTheSameSkin_StillReportsIt()
        {
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                Skinned("Bank", 5432, "Glyphic Gauntlets"),
                Skinned("Equipped:Taimi", 5432, "Glyphic Gauntlets"),
            });

            Assert.Equal("Glyphic Gauntlets", Shown(index).Name);
        }

        [Fact]
        public void Build_NoCopyIsTransmuted_OmitsTheItem()
        {
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                Stack("Bank"),
                Stack("Character:Taimi"),
            });

            Assert.Empty(index);
        }

        [Fact]
        public void Build_CopiesWearDifferentSkins_NamesNeitherButKeepsBoth()
        {
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                Skinned("Bank", 5432, "Glyphic Gauntlets"),
                Skinned("Equipped:Taimi", 99, "Chaos Gloves"),
            });

            Assert.False(Shown(index).IsPresent);
            Assert.Equal(
                new[] { "Glyphic Gauntlets", "Chaos Gloves" },
                SkinNamesFinding(index, "Glyphic Gauntlets", "Chaos Gloves"));
        }

        [Fact]
        public void Build_OneBareCopyAndOneSkinnedCopy_NamesNeitherButKeepsTheSkin()
        {
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                Skinned("Bank", 5432, "Glyphic Gauntlets"),
                Stack("Character:Taimi"),
            });

            Assert.False(Shown(index).IsPresent);
            Assert.Equal(
                new[] { "Glyphic Gauntlets" },
                SkinNamesFinding(index, "Glyphic Gauntlets", "Chaos Gloves"));
        }

        [Fact]
        public void Build_SkinNamedAfterTheItemItself_IsNotATransmutation()
        {
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                Skinned("Bank", 116, "Zojja's Warfists"),
            });

            Assert.Empty(index);
        }

        [Fact]
        public void Build_SkinIdWithNoResolvedName_ReadsAsNoSkin()
        {
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                Stack("Bank", skinId: 5432, skinIconUrl: IconFor("Glyphic Gauntlets")),
            });

            Assert.Empty(index);
        }

        [Fact]
        public void Build_SkinIdWithNoResolvedIcon_ReadsAsNoSkin()
        {
            // Half a skin is not a skin. Taking the name here would draw
            // the item's own picture under it.
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                Stack("Bank", skinId: 5432, skinName: "Glyphic Gauntlets"),
            });

            Assert.Empty(index);
        }

        [Fact]
        public void Build_CopiesWearSkinsSharingANameButNotAnIcon_NameNeither()
        {
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                Skinned("Bank", 5432, "Glyphic Gauntlets"),
                Skinned("Equipped:Taimi", 99, "Glyphic Gauntlets", "https://example.invalid/other.png"),
            });

            Assert.False(Shown(index).IsPresent);
        }

        [Fact]
        public void Build_NullOrEmptyItems_ReturnsEmpty()
        {
            Assert.Empty(TransmutedNameIndex.Build(null));
            Assert.Empty(TransmutedNameIndex.Build(new List<SnapshotItemEntry>()));
        }

        [Fact]
        public void Build_SkipsNullEntriesAndIdlessRows()
        {
            var index = TransmutedNameIndex.Build(new List<SnapshotItemEntry>
            {
                null,
                new SnapshotItemEntry
                {
                    ItemId = 0,
                    SkinName = "Glyphic Gauntlets",
                    SkinIconUrl = IconFor("Glyphic Gauntlets"),
                },
                Skinned("Bank", 5432, "Glyphic Gauntlets"),
            });

            Assert.Equal(new[] { Warfists }, index.Keys.ToArray());
        }

        // ---- the name a row shows, and what finds it ----
        [Fact]
        public void BuildItemRows_TransmutedItem_RowIsNamedAfterTheSkin()
        {
            var items = new List<SnapshotItemEntry> { Skinned("Bank", 5432, "Glyphic Gauntlets") };
            var row = Assert.Single(Rows(items, "", TransmutedNameIndex.Build(items)));

            Assert.Equal("Glyphic Gauntlets", row.Name);
            Assert.Equal("Glyphic Gauntlets", row.Skin.Name);

            // The picture moves with the name. A row naming one item and
            // drawing another is the same fault in a different channel.
            Assert.Equal(IconFor("Glyphic Gauntlets"), row.IconUrl);
            Assert.Equal(IconFor("Glyphic Gauntlets"), row.Skin.IconUrl);
        }

        [Fact]
        public void BuildItemRows_TransmutedItem_IsFoundByTheNameTheGameShows()
        {
            var items = new List<SnapshotItemEntry> { Skinned("Bank", 5432, "Glyphic Gauntlets") };
            var row = Assert.Single(Rows(items, "Glyphic", TransmutedNameIndex.Build(items)));

            Assert.Equal("Glyphic Gauntlets", row.Name);
        }

        [Fact]
        public void BuildItemRows_TransmutedItem_IsStillFoundByTheItemsOwnName()
        {
            var items = new List<SnapshotItemEntry> { Skinned("Bank", 5432, "Glyphic Gauntlets") };
            var row = Assert.Single(Rows(items, "Zojja", TransmutedNameIndex.Build(items)));

            Assert.Equal("Glyphic Gauntlets", row.Name);
        }

        [Fact]
        public void BuildItemRows_CopiesWearDifferentSkins_RowKeepsTheItemsOwnName()
        {
            var items = new List<SnapshotItemEntry>
            {
                Skinned("Bank", 5432, "Glyphic Gauntlets"),
                Skinned("Equipped:Taimi", 99, "Chaos Gloves"),
            };
            var index = TransmutedNameIndex.Build(items);

            var row = Assert.Single(Rows(items, "", index));
            Assert.Equal("Zojja's Warfists", row.Name);
            Assert.Equal(ItemIcon, row.IconUrl);
            Assert.False(row.Skin.IsPresent);

            // Neither spelling may lose an item the row does not name.
            Assert.Equal("Zojja's Warfists", Assert.Single(Rows(items, "Glyphic", index)).Name);
            Assert.Equal("Zojja's Warfists", Assert.Single(Rows(items, "Chaos", index)).Name);
        }

        [Fact]
        public void BuildItemRows_FilterLeavesOnlyTheTransmutedCopy_RowIsNamedAfterTheSkin()
        {
            var items = OneSkinnedOneBare();
            var index = TransmutedNameIndex.Build(items);
            var filter = new SnapshotSourceFilter { Bank = false };

            var row = Assert.Single(Rows(items, "", index, filter));
            Assert.Equal("Glyphic Gauntlets", row.Name);
            Assert.Equal(IconFor("Glyphic Gauntlets"), row.IconUrl);
            Assert.True(row.Skin.IsPresent);
        }

        [Fact]
        public void BuildItemRows_FilterHidesTheTransmutedCopy_SkinNameFindsNothing()
        {
            var items = OneSkinnedOneBare();
            var index = TransmutedNameIndex.Build(items);
            var filter = WithoutCharacter("Alice");

            Assert.Empty(Rows(items, "Glyphic", index, filter));

            // The bank copy is still there under the item's own name.
            Assert.Equal(
                "Zojja's Warfists",
                Assert.Single(Rows(items, "Zojja", index, filter)).Name);
        }

        [Fact]
        public void BuildItemRows_WithoutTheIndex_EveryRowKeepsTheItemsOwnName()
        {
            var items = new List<SnapshotItemEntry> { Skinned("Bank", 5432, "Glyphic Gauntlets") };

            var row = Assert.Single(Rows(items, "", null));
            Assert.Equal("Zojja's Warfists", row.Name);
            Assert.Equal(ItemIcon, row.IconUrl);
            Assert.False(row.Skin.IsPresent);
            Assert.Empty(Rows(items, "Glyphic", null));
        }

        // ---- the tooltip block ----
        private static async Task<TooltipContent> TooltipContentFor(TransmutedSkin skin)
        {
            var raws = await RealItemFixtures.ParseAsync(RealItemJson.ZojjasWarfists);
            var stats = ItemStatBlockFactory.Build(raws[Warfists]);
            return ItemStatTooltipComposer.BuildContent(stats, SocketedUpgradeView.None, skin);
        }

        private static async Task<string[]> Tooltip(string skinName)
        {
            var skin = string.IsNullOrEmpty(skinName)
                ? TransmutedSkin.None
                : TransmutedSkin.Of(skinName, IconFor(skinName));
            return (await TooltipContentFor(skin)).ToPlainLines().ToArray();
        }

        [Fact]
        public async Task Tooltip_TransmutedItem_HeadingIsTheSkinsName()
        {
            var lines = await Tooltip("Glyphic Gauntlets");

            Assert.Equal("Glyphic Gauntlets", lines[0]);
            Assert.DoesNotContain("Zojja's Warfists", lines[0]);
        }

        [Fact]
        public async Task Tooltip_TransmutedItem_NamesTheItemUnderATransmutedLine()
        {
            var lines = await Tooltip("Glyphic Gauntlets");

            int at = System.Array.IndexOf(lines, "Transmuted");
            Assert.True(at > 0, "the tooltip carries no Transmuted line");
            Assert.Equal("Zojja's Warfists", lines[at + 1]);

            // One blank row above the pair and one below it, and the
            // identity block follows: measured on the ascended-sword
            // capture ItemStatTooltipComposer.BuildTransmutedBlock cites.
            Assert.Equal("", lines[at - 1]);
            Assert.Equal("", lines[at + 2]);
            Assert.Equal("Ascended", lines[at + 3]);

            // Below the socket lines, not above them.
            Assert.True(System.Array.IndexOf(lines, "Unused Infusion Slot") < at);
        }

        [Fact]
        public async Task Tooltip_TransmutedItem_HeadingDrawsTheSkinsIcon()
        {
            var skin = TransmutedSkin.Of("Glyphic Gauntlets", IconFor("Glyphic Gauntlets"));
            var content = await TooltipContentFor(skin);

            // The heading names the skin, so it must draw the skin.
            Assert.Equal(IconFor("Glyphic Gauntlets"), content.Lines[0].IconUrl);
        }

        [Fact]
        public async Task Tooltip_ItemWearingItsOwnLook_HeadingDrawsTheItemsIcon()
        {
            var content = await TooltipContentFor(TransmutedSkin.None);

            Assert.Equal(
                "https://render.guildwars2.com/file/BD20599D290345BE7D98BD270FBE502CF5212654/699217.png",
                content.Lines[0].IconUrl);
        }

        [Fact]
        public async Task Tooltip_ItemWearingItsOwnLook_HasNoTransmutedLine()
        {
            foreach (string skin in new[] { null, "", "Zojja's Warfists" })
            {
                var lines = await Tooltip(skin);

                Assert.Equal("Zojja's Warfists", lines[0]);
                Assert.DoesNotContain("Transmuted", lines);
            }
        }

        [Fact]
        public void TheNoSkinsIndex_RefusesAWriteThroughACast()
        {
            // Every snapshot with no skinned copy in it is answered with
            // one shared instance, so a write through a cast back to
            // Dictionary would land on all of them.
            var empty = TransmutedNameIndex.Build(new List<SnapshotItemEntry>());

            Assert.Same(empty, TransmutedNameIndex.Build(null));
            Assert.Empty(empty);
            Assert.Throws<NotSupportedException>(
                () => ((IDictionary<int, IReadOnlyList<TransmutedItemCopy>>)empty).Add(1, null));
        }
    }
}
