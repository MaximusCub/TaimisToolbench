using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class CurrencyDisplayResolverTests
    {
        // --- ResolveName ---
        [Fact]
        public void ResolveName_MetadataPresent_PrefersMetadataName()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "Spirit Shard (Live)", IconUrl = "s.png" },
            };

            Assert.Equal("Spirit Shard (Live)", CurrencyDisplayResolver.ResolveName(23, metadata));
        }

        [Fact]
        public void ResolveName_MetadataNull_FallsBackToGw2Constants()
        {
            Assert.Equal("Spirit Shards", CurrencyDisplayResolver.ResolveName(23, null));
        }

        [Fact]
        public void ResolveName_IdMissingFromMetadata_FallsBackToGw2Constants()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [2] = new CurrencyMetadata { CurrencyId = 2, Name = "Karma" },
            };

            Assert.Equal("Spirit Shards", CurrencyDisplayResolver.ResolveName(23, metadata));
        }

        [Fact]
        public void ResolveName_EmptyNameInMetadata_FallsBackToGw2Constants()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "" },
            };

            Assert.Equal("Spirit Shards", CurrencyDisplayResolver.ResolveName(23, metadata));
        }

        [Fact]
        public void ResolveName_UnknownId_FallsBackToGenericCurrency_NoIdDisplayed()
        {
            var name = CurrencyDisplayResolver.ResolveName(99999, null);

            Assert.Equal("Currency", name);
            Assert.DoesNotContain("99999", name);
        }

        // --- ResolveDescription ---
        [Fact]
        public void ResolveDescription_MetadataPresent_ReturnsTheApisOwnProse()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata
                {
                    CurrencyId = 23,
                    Name = "Spirit Shards",
                    Description = "Gained after reaching level 80.",
                },
            };

            Assert.Equal(
                "Gained after reaching level 80.",
                CurrencyDisplayResolver.ResolveDescription(23, metadata));
        }

        [Fact]
        public void ResolveDescription_NoMetadata_ReturnsNullRatherThanInventingProse()
        {
            // Unlike ResolveName there is no offline table to fall back to,
            // and a sentence the module wrote itself would be invented data.
            Assert.Null(CurrencyDisplayResolver.ResolveDescription(23, null));

            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [2] = new CurrencyMetadata { CurrencyId = 2, Name = "Karma", Description = "k" },
            };
            Assert.Null(CurrencyDisplayResolver.ResolveDescription(23, metadata));
        }

        [Fact]
        public void ResolveDescription_EmptyDescription_ReadsAsAbsent()
        {
            // An older saved plan carries CurrencyMetadata with no
            // Description at all; Newtonsoft leaves it null, and a reply
            // that carried none parses to "". Both mean the same thing.
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "Spirit Shards", Description = "" },
                [24] = new CurrencyMetadata { CurrencyId = 24, Name = "Relics" },
            };

            Assert.Null(CurrencyDisplayResolver.ResolveDescription(23, metadata));
            Assert.Null(CurrencyDisplayResolver.ResolveDescription(24, metadata));
        }

        // --- ResolveIconUrl ---
        [Fact]
        public void ResolveIconUrl_MetadataPresent_ReturnsIcon()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "Spirit Shards", IconUrl = "spirit_shard.png" },
            };

            Assert.Equal("spirit_shard.png", CurrencyDisplayResolver.ResolveIconUrl(23, metadata));
        }

        [Fact]
        public void ResolveIconUrl_MetadataNull_ReturnsNull()
        {
            Assert.Null(CurrencyDisplayResolver.ResolveIconUrl(23, null));
        }

        [Fact]
        public void ResolveIconUrl_IdMissingFromMetadata_ReturnsNull()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [2] = new CurrencyMetadata { CurrencyId = 2, IconUrl = "karma.png" },
            };

            Assert.Null(CurrencyDisplayResolver.ResolveIconUrl(23, metadata));
        }

        [Fact]
        public void ResolveIconUrl_EmptyIconInMetadata_ReturnsNull()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, IconUrl = "" },
            };

            Assert.Null(CurrencyDisplayResolver.ResolveIconUrl(23, metadata));
        }

        // --- ResolveAmounts ---
        [Fact]
        public void ResolveAmounts_Null_ReturnsNull()
        {
            Assert.Null(CurrencyDisplayResolver.ResolveAmounts(null, null));
        }

        [Fact]
        public void ResolveAmounts_Empty_ReturnsNull()
        {
            Assert.Null(CurrencyDisplayResolver.ResolveAmounts(new List<CostLine>(), null));
        }

        [Fact]
        public void ResolveAmounts_SingleLine_ResolvesNameIconAmount()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "Spirit Shards", IconUrl = "s.png" },
            };
            var costLines = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 500 } };

            var result = CurrencyDisplayResolver.ResolveAmounts(costLines, metadata);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal(500, result[0].Amount);
            Assert.Equal("Spirit Shards", result[0].Name);
            Assert.Equal("s.png", result[0].IconUrl);

            // A total is already exact, so it carries no per-unit rate.
            Assert.Null(result[0].UnitRate);
        }

        [Fact]
        public void ResolveAmounts_MultipleLines_ResolvesEachIndependently()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "Spirit Shards", IconUrl = "s.png" },
            };
            var costLines = new List<CostLine>
            {
                new CostLine { Type = "Currency", Id = 23, Count = 50 },
                new CostLine { Type = "Currency", Id = 2, Count = 1000 },
            };

            var result = CurrencyDisplayResolver.ResolveAmounts(costLines, metadata);

            Assert.Equal(2, result.Count);
            Assert.Equal("Spirit Shards", result[0].Name);
            Assert.Equal("s.png", result[0].IconUrl);
            Assert.Equal("Karma", result[1].Name); // Gw2Constants fallback, no metadata entry for id 2
            Assert.Null(result[1].IconUrl);
        }

        // --- ResolveAmounts ownedCurrencyAmounts ---
        [Fact]
        public void ResolveAmounts_OwnedAmountsNull_OwnedQuantityStaysNull()
        {
            var costLines = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 500 } };

            var result = CurrencyDisplayResolver.ResolveAmounts(costLines, null, null);

            Assert.Null(result[0].OwnedQuantity);
            Assert.Null(result[0].RawOwnedQuantity);
        }

        [Fact]
        public void ResolveAmounts_OwnedLessThanNeeded_OwnedQuantityIsRawWalletAmount()
        {
            var costLines = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 500 } };
            var owned = new Dictionary<int, int> { { 23, 200 } };

            var result = CurrencyDisplayResolver.ResolveAmounts(costLines, null, owned);

            Assert.Equal(200, result[0].OwnedQuantity);
            Assert.Equal(200, result[0].RawOwnedQuantity);
        }

        [Fact]
        public void ResolveAmounts_OwnedExceedsNeeded_OwnedQuantityClampedToAmount()
        {
            // Owning MORE than this cost line needs must never surface an
            // "owned" figure bigger than the line's own Amount - the
            // clamped OwnedQuantity is deliberate (shoplist-have-format)
            // so the tooltip's HAVE/Amount pair always reads as a
            // coverage fraction. RawOwnedQuantity is the escape hatch that
            // still carries the real, unclamped holding for that same
            // tooltip to state alongside it.
            var costLines = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 500 } };
            var owned = new Dictionary<int, int> { { 23, 999999 } };

            var result = CurrencyDisplayResolver.ResolveAmounts(costLines, null, owned);

            Assert.Equal(500, result[0].OwnedQuantity);
            Assert.Equal(999999, result[0].RawOwnedQuantity);
        }

        [Fact]
        public void ResolveAmounts_OwnedAmountsMissingThisCurrencyId_OwnedQuantityStaysNull()
        {
            var costLines = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 500 } };
            var owned = new Dictionary<int, int> { { 2, 100 } }; // different currency id

            var result = CurrencyDisplayResolver.ResolveAmounts(costLines, null, owned);

            Assert.Null(result[0].OwnedQuantity);
            Assert.Null(result[0].RawOwnedQuantity);
        }

        [Fact]
        public void ResolveAmounts_MultipleLines_OwnedQuantityResolvedPerLine()
        {
            var costLines = new List<CostLine>
            {
                new CostLine { Type = "Currency", Id = 23, Count = 500 },
                new CostLine { Type = "Currency", Id = 2, Count = 1000 },
            };
            var owned = new Dictionary<int, int> { { 23, 100 } }; // only the first line has wallet data

            var result = CurrencyDisplayResolver.ResolveAmounts(costLines, null, owned);

            Assert.Equal(100, result[0].OwnedQuantity);
            Assert.Equal(100, result[0].RawOwnedQuantity);
            Assert.Null(result[1].OwnedQuantity);
            Assert.Null(result[1].RawOwnedQuantity);
        }

        [Fact]
        public void ResolveAmounts_NeverPutsTheRawCurrencyIdInAnythingDrawn()
        {
            // The view model carries the id so a tooltip can be resolved
            // from it. Nothing a reader sees may be that number, which is
            // the invariant this used to assert by having no id field at
            // all - and having none is what made each surface carry its own
            // name and description instead.
            var costLines = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 5 } };

            var result = CurrencyDisplayResolver.ResolveAmounts(costLines, null);

            Assert.Equal(23, result[0].CurrencyId);
            foreach (var text in new[] { result[0].Name, result[0].IconUrl, result[0].BundleLabel })
            {
                Assert.True(text == null || text.IndexOf("23", System.StringComparison.Ordinal) < 0, text);
            }
        }

        // --- ResolveUnitAmounts (winning-offer true per-unit
        // rate, not a truncated total/quantity average) ---
        [Fact]
        public void ResolveUnitAmounts_EvenDivision_ResolvesWholeNumberAmount_NoBundleLabel()
        {
            // A "3 for 3" batch (the exact Obsidian Shard live-repro shape):
            // 3 currency per 3 output units divides evenly to 1 each.
            var perBatch = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 3 } };

            var result = CurrencyDisplayResolver.ResolveUnitAmounts(3, perBatch, null);

            Assert.NotNull(result);
            Assert.Equal(1, result[0].Amount);
            Assert.Null(result[0].BundleLabel);
            Assert.Equal(1d, result[0].UnitRate.Value, 6);
        }

        [Fact]
        public void ResolveUnitAmounts_UnevenDivision_UsesBundleLabel_NotRoundedAmount()
        {
            // A "2 for 3" batch does not divide evenly - the true per-unit
            // rate is not a whole number, so the resolver must not invent a
            // rounded figure; it carries the literal bundle text instead.
            var perBatch = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 2 } };

            var result = CurrencyDisplayResolver.ResolveUnitAmounts(3, perBatch, null);

            Assert.NotNull(result);
            Assert.Equal(0, result[0].Amount);
            Assert.Equal("2 for 3", result[0].BundleLabel);

            // The exact rate survives as a non-display number so a sort on
            // the Each column has something real to key on - PlanTableSorter.
            Assert.Equal(2d / 3d, result[0].UnitRate.Value, 6);
        }

        [Fact]
        public void ResolveUnitAmounts_NeverTruncatesAggregateTotal()
        {
            // Regression guard for the pre-fix bug: a merged row's
            // aggregated total (186) and quantity (179) must play NO part
            // in the Each computation at all - only the winning offer's own
            // per-batch rate (3-for-3 = 1 each) matters, never
            // 186/179 (which truncates to a misleading "1" too, but for the
            // wrong reason and would differ for other aggregate shapes).
            var perBatch = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 3 } };

            var result = CurrencyDisplayResolver.ResolveUnitAmounts(3, perBatch, null);

            Assert.Equal(1, result[0].Amount);
        }

        [Fact]
        public void ResolveUnitAmounts_ZeroOutputCount_ReturnsNull_NoDivideByZero()
        {
            var perBatch = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 3 } };

            Assert.Null(CurrencyDisplayResolver.ResolveUnitAmounts(0, perBatch, null));
        }

        [Fact]
        public void ResolveUnitAmounts_NegativeOutputCount_ReturnsNull()
        {
            var perBatch = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 3 } };

            Assert.Null(CurrencyDisplayResolver.ResolveUnitAmounts(-1, perBatch, null));
        }

        [Fact]
        public void ResolveUnitAmounts_NullCostLines_ReturnsNull()
        {
            // Covers both a non-vendor row and a vendor row whose tree
            // occurrences resolved to more than one distinct offer (the
            // Conflict case) - PlanStep leaves this null in both cases.
            Assert.Null(CurrencyDisplayResolver.ResolveUnitAmounts(3, null, null));
        }

        [Fact]
        public void ResolveUnitAmounts_EmptyCostLines_ReturnsNull()
        {
            Assert.Null(CurrencyDisplayResolver.ResolveUnitAmounts(3, new List<CostLine>(), null));
        }

        // --- ResolveTreeNodeUnitAmounts (in-game finding B: recipe-tree
        // "Unit price:" tooltip for a pure/mixed-currency vendor node) ---
        [Fact]
        public void ResolveTreeNodeUnitAmounts_EvenDivision_ResolvesWholeNumberAmount_NoBundleLabel()
        {
            // 100 total across a Quantity of 2 divides evenly to 50 each.
            var total = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 100 } };

            var result = CurrencyDisplayResolver.ResolveTreeNodeUnitAmounts(total, 2, null);

            Assert.NotNull(result);
            Assert.Equal(50, result[0].Amount);
            Assert.Null(result[0].BundleLabel);
        }

        [Fact]
        public void ResolveTreeNodeUnitAmounts_UnevenDivision_UsesBundleLabel_NotRoundedAmount()
        {
            // 10 total across a Quantity of 3 does not divide evenly - never
            // invent a rounded per-unit figure (mirrors ResolveUnitAmounts).
            var total = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 10 } };

            var result = CurrencyDisplayResolver.ResolveTreeNodeUnitAmounts(total, 3, null);

            Assert.NotNull(result);
            Assert.Equal(0, result[0].Amount);
            Assert.Equal("10 for 3", result[0].BundleLabel);
        }

        [Fact]
        public void ResolveTreeNodeUnitAmounts_ResolvesNameAndIcon()
        {
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "Spirit Shards", IconUrl = "s.png" },
            };
            var total = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 100 } };

            var result = CurrencyDisplayResolver.ResolveTreeNodeUnitAmounts(total, 2, metadata);

            Assert.Equal("Spirit Shards", result[0].Name);
            Assert.Equal("s.png", result[0].IconUrl);
        }

        [Fact]
        public void ResolveTreeNodeUnitAmounts_ZeroQuantity_ReturnsNull_NoDivideByZero()
        {
            var total = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 100 } };

            Assert.Null(CurrencyDisplayResolver.ResolveTreeNodeUnitAmounts(total, 0, null));
        }

        [Fact]
        public void ResolveTreeNodeUnitAmounts_NegativeQuantity_ReturnsNull()
        {
            var total = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 100 } };

            Assert.Null(CurrencyDisplayResolver.ResolveTreeNodeUnitAmounts(total, -1, null));
        }

        [Fact]
        public void ResolveTreeNodeUnitAmounts_NullCostLines_ReturnsNull()
        {
            Assert.Null(CurrencyDisplayResolver.ResolveTreeNodeUnitAmounts(null, 2, null));
        }

        [Fact]
        public void ResolveTreeNodeUnitAmounts_EmptyCostLines_ReturnsNull()
        {
            Assert.Null(CurrencyDisplayResolver.ResolveTreeNodeUnitAmounts(new List<CostLine>(), 2, null));
        }

        [Fact]
        public void ResolveTreeNodeUnitAmounts_MultipleLines_ResolvedIndependently()
        {
            var total = new List<CostLine>
            {
                new CostLine { Type = "Currency", Id = 23, Count = 100 },
                new CostLine { Type = "Currency", Id = 2, Count = 7 },
            };

            var result = CurrencyDisplayResolver.ResolveTreeNodeUnitAmounts(total, 2, null);

            Assert.Equal(2, result.Count);
            Assert.Equal(50, result[0].Amount);
            Assert.Null(result[0].BundleLabel);
            Assert.Equal(0, result[1].Amount);
            Assert.Equal("7 for 2", result[1].BundleLabel);
        }
    }
}
