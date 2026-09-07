using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // TreeRowTooltipComposer is the
    // Blish-free half of the Recipe Tree row tooltip (see
    // TreeSectionController.RenderTreeNode for the actual
    // BasicTooltipText/right-click wiring, which cannot be unit tested per
    // repo invariant).
    public class TreeRowTooltipComposerTests
    {
        private static CraftingTreeNode Node(
            CraftingDecision decision,
            string name = "Bolt of Damask",
            int quantity = 1,
            long? unitCost = null,
            bool priceSideFellBack = false,
            bool isCostComponent = false,
            IReadOnlyList<CostLine> vendorCurrencyCosts = null,
            string acquisitionHint = null,
            bool vendorHasBarterItemCost = false)
        {
            return new CraftingTreeNode
            {
                NodeId = 1,
                Name = name,
                Decision = decision,
                Quantity = quantity,
                UnitCost = unitCost,
                PriceSideFellBack = priceSideFellBack,
                IsCostComponent = isCostComponent,
                VendorCurrencyCosts = vendorCurrencyCosts,
                VendorHasBarterItemCost = vendorHasBarterItemCost,
                AcquisitionHint = acquisitionHint,
            };
        }

        [Fact]
        public void NullNode_ReturnsEmptyList()
        {
            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(null, null, null).ToPlainLines();

            Assert.Empty(lines);
        }

        [Fact]
        public void EveryFieldEmpty_ReturnsEmptyList()
        {
            var node = Node(CraftingDecision.Have, name: null, quantity: 0);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.Empty(lines);
        }

        [Fact]
        public void QuantityGreaterThanOne_BuyFromTp_AddsUnitPriceLine()
        {
            var node = Node(CraftingDecision.BuyFromTp, quantity: 5, unitCost: 12345);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.Contains("Unit price: 1g 23s 45c", lines);
        }

        [Fact]
        public void QuantityOne_BuyFromTp_NoUnitPriceLine()
        {
            // gw2e parity display rule: at quantity 1 the cost column
            // already shows the unit's own total, so a duplicate "Unit
            // price" tooltip line would be redundant.
            var node = Node(CraftingDecision.BuyFromTp, quantity: 1, unitCost: 12345);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.DoesNotContain(lines, l => l.StartsWith("Unit price:"));
        }

        [Fact]
        public void ZeroCoinUnitCost_WithCurrencyCosts_SuppressesCoinLine()
        {
            // A pure-currency vendor offer has
            // UnitCost == 0 (not null), which used to render a misleading
            // "0g 0s 0c" line - suppressed only when a currency cost line
            // exists to show instead of it.
            var currencyCosts = new List<CostLine> { new CostLine { Type = "Currency", Id = 2, Count = 10 } };
            var node = Node(
                CraftingDecision.BuyFromVendor, quantity: 5, unitCost: 0,
                vendorCurrencyCosts: currencyCosts);
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                { 2, new CurrencyMetadata { CurrencyId = 2, Name = "Karma" } },
            };
            var plan = new PlanViewModel { CurrencyMetadata = metadata };

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, plan).ToPlainLines();

            Assert.DoesNotContain(lines, l => l.StartsWith("Unit price: 0g"));
            Assert.Contains("Unit price: 2 Karma", lines);
        }

        [Fact]
        public void ZeroCoinUnitCost_NoCurrencyCosts_StillRendersCoinLine()
        {
            // The suppression above is conditional on a currency cost
            // existing - a genuinely zero-cost buy (no currency fallback)
            // must still render its real "0g 0s 0c" line, not go silent.
            var node = Node(CraftingDecision.BuyFromTp, quantity: 5, unitCost: 0);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            // Coin spelling changed with the CoinSegmentMath.GameStyleText
            // consolidation: every composer now spells a coin amount the
            // way the icons beside it do (leading all-zero units omitted,
            // trailing units zero-padded).
            Assert.Contains("Unit price: 0c", lines);
        }

        [Fact]
        public void PriceSideFellBack_BuyFromTp_NullPlan_UsesBasisAgnosticSentence()
        {
            // A null
            // plan cannot know the actual PriceBasis, so it must get a
            // basis-agnostic sentence rather than silently reading null as
            // "false" and picking one side's wording as an unearned claim.
            var node = Node(CraftingDecision.BuyFromTp, priceSideFellBack: true);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.Contains("The other trading post price side is shown.", lines);
        }

        [Fact]
        public void PriceSideFellBack_BuyFromTp_BuyOrderBasis_NamesInstantBuyFallback()
        {
            var node = Node(CraftingDecision.BuyFromTp, priceSideFellBack: true);
            var plan = new PlanViewModel { PriceBasis = PriceBasis.BuyOrder };

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, plan).ToPlainLines();

            Assert.Contains("The buy-order price is unavailable, so the instant-buy price is shown.", lines);
        }

        [Fact]
        public void PriceSideFellBack_BuyFromTp_InstantBuyBasis_NamesBuyOrderFallback()
        {
            var node = Node(CraftingDecision.BuyFromTp, priceSideFellBack: true);
            var plan = new PlanViewModel { PriceBasis = PriceBasis.InstantBuy };

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, plan).ToPlainLines();

            Assert.Contains("The instant-buy price is unavailable, so the buy-order price is shown.", lines);
        }

        [Fact]
        public void PriceSideFellBack_CostComponentLeaf_UsesRowSentenceNotVendorSentence()
        {
            // A cost-component leaf's own Decision is always BuyFromVendor
            // (see BuildVendorCostComponentLeaves), but IsCostComponent
            // routes it into the "this row's own TP price fell back"
            // branch, not the "one of this vendor row's cost items fell
            // back" aggregate branch.
            var node = Node(CraftingDecision.BuyFromVendor, priceSideFellBack: true, isCostComponent: true);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.Contains("The other trading post price side is shown.", lines);
            Assert.DoesNotContain(lines, l => l.StartsWith("A vendor cost item's"));
        }

        [Fact]
        public void PriceSideFellBack_BuyFromVendorParent_NullPlan_UsesVendorAggregateSentence()
        {
            var node = Node(CraftingDecision.BuyFromVendor, priceSideFellBack: true);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.Contains("A vendor cost item shows its other trading post price side.", lines);
        }

        [Fact]
        public void PriceSideFellBack_BuyFromVendorParent_BuyOrderBasis_UsesVendorInstantBuySentence()
        {
            var node = Node(CraftingDecision.BuyFromVendor, priceSideFellBack: true);
            var plan = new PlanViewModel { PriceBasis = PriceBasis.BuyOrder };

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, plan).ToPlainLines();

            // 83 characters, and it stays ONE line: the rich surface wraps
            // by measured pixel width, so the composer hands it over whole.
            // (This used to assert the character-budget split applied by the
            // deleted plain wrapper - TooltipTextFormatTests still covers
            // that seam for the callers that still use it.)
            Assert.Contains(
                lines,
                l => l.StartsWith(
                    "A vendor cost item's buy-order price is unavailable, so its instant-buy price is used."));
        }

        [Fact]
        public void PriceSideFellBack_BuyFromVendorParent_InstantBuyBasis_UsesVendorBuyOrderSentence()
        {
            // The BuyFromTp/IsCostComponent branch above already pins all
            // three PriceBasis arms of its own ternary; this is the
            // BuyFromVendor-parent aggregate branch's InstantBuy arm, the
            // one remaining untested arm of that second ternary.
            var node = Node(CraftingDecision.BuyFromVendor, priceSideFellBack: true);
            var plan = new PlanViewModel { PriceBasis = PriceBasis.InstantBuy };

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, plan).ToPlainLines();

            Assert.Contains(
                lines,
                l => l.StartsWith(
                    "A vendor cost item's instant-buy price is unavailable, so its buy-order price is used."));
        }

        [Fact]
        public void PureBarterVendorOffer_SuppressesTheZeroCoinUnitPriceLine()
        {
            // A barter-only offer has UnitCost 0 (not null - see
            // CraftingTreeBuilder.BuildNode) and, unlike a currency-priced
            // one, an EMPTY VendorCurrencyCosts. Rendering the coin line
            // would state "Unit price: 0c" for an item that really costs
            // account-bound tokens.
            var node = Node(
                CraftingDecision.BuyFromVendor, quantity: 5, unitCost: 0,
                vendorHasBarterItemCost: true);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.DoesNotContain(lines, l => l.StartsWith("Unit price:"));
        }

        [Fact]
        public void MixedCoinAndBarterVendorOffer_StillShowsItsRealCoinUnitPrice()
        {
            // The suppression above is scoped to a genuinely zero coin
            // part: an offer that also charges coin must still show it.
            var node = Node(
                CraftingDecision.BuyFromVendor, quantity: 5, unitCost: 500,
                vendorHasBarterItemCost: true);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.Contains("Unit price: 5s 0c", lines);
        }

        [Fact]
        public void MixedCoinAndCurrencyVendorOffer_ShowsBothUnitPriceLinesInOrder()
        {
            // The composer's own comment claims "a mixed coin+currency
            // offer still shows both lines below" - pin that a non-zero
            // UnitCost together with a non-empty VendorCurrencyCosts
            // renders both the coin line and the currency line, coin
            // first, rather than the zero-coin suppression path.
            var currencyCosts = new List<CostLine> { new CostLine { Type = "Currency", Id = 2, Count = 25 } };
            var node = Node(
                CraftingDecision.BuyFromVendor, quantity: 5, unitCost: 500,
                vendorCurrencyCosts: currencyCosts);
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                { 2, new CurrencyMetadata { CurrencyId = 2, Name = "Karma" } },
            };
            var plan = new PlanViewModel { CurrencyMetadata = metadata };

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, plan).ToPlainLines();

            // Coin spelling changed with the CoinSegmentMath.GameStyleText
            // consolidation: every composer now spells a coin amount the
            // way the icons beside it do (leading all-zero units omitted,
            // trailing units zero-padded).
            Assert.Equal(
                new[] { "Unit price: 5s 0c", "Unit price: 5 Karma", "Right-click to open the wiki page." },
                lines);
        }

        [Fact]
        public void CurrencyUnitAmount_NonEvenDivision_UsesBundleLabel()
        {
            // ResolveTreeNodeUnitAmounts falls back to a "N for M" bundle
            // label (Amount left at 0) whenever the total does not divide
            // evenly by quantity - pin that BuildExtraTooltipContent renders
            // that label rather than the numeric Amount.ToString() fork.
            var currencyCosts = new List<CostLine> { new CostLine { Type = "Currency", Id = 2, Count = 10 } };
            var node = Node(
                CraftingDecision.BuyFromVendor, quantity: 3, unitCost: 0,
                vendorCurrencyCosts: currencyCosts);
            var metadata = new Dictionary<int, CurrencyMetadata>
            {
                { 2, new CurrencyMetadata { CurrencyId = 2, Name = "Karma" } },
            };
            var plan = new PlanViewModel { CurrencyMetadata = metadata };

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, plan).ToPlainLines();

            Assert.Contains("Unit price: 10 for 3 Karma", lines);
        }

        [Fact]
        public void NoPriceSideFellBack_NoCaveatLine()
        {
            var node = Node(CraftingDecision.BuyFromTp, priceSideFellBack: false);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.DoesNotContain(lines, l => l.Contains("trading post price side"));
        }

        [Theory]
        [InlineData(nameof(CraftingDecision.Unknown))]
        [InlineData(nameof(CraftingDecision.GuildUpgrade))]
        public void AcquisitionHint_UnknownOrGuildUpgrade_IsIncluded(string decisionName)
        {
            var decision = EnumArg.Parse<CraftingDecision>(decisionName);
            var node = Node(decision, acquisitionHint: "Purchased from a Karma vendor.");

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.Contains("Purchased from a Karma vendor.", lines);
        }

        [Fact]
        public void AcquisitionHint_OtherDecision_IsIgnored()
        {
            // AcquisitionHint is only meaningful on the "no priceable
            // source, here is why" decisions - a Craft node carrying a
            // stray hint value must not surface it.
            var node = Node(CraftingDecision.Craft, acquisitionHint: "Purchased from a Karma vendor.");

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.DoesNotContain("Purchased from a Karma vendor.", lines);
        }

        [Fact]
        public void AcquisitionHint_NullOrEmpty_NotAdded()
        {
            var node = Node(CraftingDecision.Unknown, name: null, acquisitionHint: null);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.Empty(lines);
        }

        [Fact]
        public void CaptionText_InsertedAtFront_AheadOfOtherLines()
        {
            var node = Node(CraftingDecision.Unknown, acquisitionHint: "No listed source.");

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, "What if: crafted instead", null).ToPlainLines();

            Assert.Equal("What if: crafted instead", lines[0]);
            Assert.Contains("No listed source.", lines);
        }

        [Fact]
        public void CaptionText_NullOrEmpty_NotInserted()
        {
            var node = Node(CraftingDecision.Have, name: null);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, "", null).ToPlainLines();

            Assert.Empty(lines);
        }

        [Fact]
        public void RealItemName_AddsWikiLinkLine()
        {
            var node = Node(CraftingDecision.Have, name: "Bolt of Damask");

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.Contains("Right-click to open the wiki page.", lines);
        }

        [Theory]
        [InlineData("Unknown Item")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        public void SentinelOrEmptyName_SuppressesWikiLinkLine(string name)
        {
            var node = Node(CraftingDecision.Have, name: name);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, null, null).ToPlainLines();

            Assert.DoesNotContain("Right-click to open the wiki page.", lines);
        }

        [Fact]
        public void AllLinesTogether_RenderInEstablishedOrder()
        {
            // Order matters for on-screen readability: caption first (when
            // present), then unit price, then the price-side caveat, then
            // the acquisition hint, then the wiki-link affordance last -
            // matching TreeSectionController.RenderTreeNode's original
            // build order verbatim.
            var node = Node(
                CraftingDecision.BuyFromTp, name: "Bolt of Damask", quantity: 3, unitCost: 100,
                priceSideFellBack: true);

            var lines = TreeRowTooltipComposer.BuildExtraTooltipContent(node, "Caption line", null).ToPlainLines();

            Assert.Equal(
                new[]
                {
                    "Caption line",
                    "Unit price: 1s 0c",
                    "The other trading post price side is shown.",
                    "Right-click to open the wiki page.",
                },
                lines);
        }

        // --- Stat tooltip gate (item-stat-tooltips) ---
        //
        // CraftingTreeNode.ItemId is one numeric slot shared by three id
        // spaces, so the stat lookup - which is keyed by ITEM id - must not
        // run on a row whose id is a wallet currency or a guild upgrade.
        // Mirrors CraftingTreeBuilderTests' own
        // CurrencyNode_NeverResolvesIconOrRarityViaItemMetadata_
        // EvenWhenIdCollides for the same real collision: id 24 is both a
        // vendor-offer outputItemId and the currency "Pristine Fractal
        // Relics".
        private const int CollidingId = 24;

        private static ItemStatBlock CollidingItemStats()
        {
            return new ItemStatBlock
            {
                ItemId = CollidingId,
                Name = "Unrelated Item",
                Rarity = "Legendary",
                ItemType = "Trophy",
                VendorValue = 1000,
            };
        }

        private static CraftingTreeNode StatGateNode(
            CraftingDecision decision,
            string name,
            bool isCostComponent = false,
            long? subtreeCost = null)
        {
            return new CraftingTreeNode
            {
                NodeId = 1,
                ItemId = CollidingId,
                Name = name,
                Decision = decision,
                Quantity = 5,
                IsCostComponent = isCostComponent,
                SubtreeCost = subtreeCost,
            };
        }

        [Fact]
        public void CurrencyRow_ShowsNoStatBlock_EvenWhenItsIdCollidesWithACachedItem()
        {
            var node = StatGateNode(CraftingDecision.Currency, "Pristine Fractal Relics");

            var content = TreeRowTooltipComposer.BuildStatTooltipContent(node, id => CollidingItemStats());

            Assert.True(content.IsEmpty);
        }

        [Fact]
        public void GuildUpgradeRow_ShowsNoStatBlock_EvenWhenItsIdCollidesWithACachedItem()
        {
            var node = StatGateNode(CraftingDecision.GuildUpgrade, "Guild upgrade (unresolved)");

            var content = TreeRowTooltipComposer.BuildStatTooltipContent(node, id => CollidingItemStats());

            Assert.True(content.IsEmpty);
        }

        [Fact]
        public void UnrecognizedIngredientRow_ShowsNoStatBlock()
        {
            var node = StatGateNode(CraftingDecision.UnrecognizedIngredient, "Unrecognized ingredient type");

            var content = TreeRowTooltipComposer.BuildStatTooltipContent(node, id => CollidingItemStats());

            Assert.True(content.IsEmpty);
        }

        [Fact]
        public void CurrencyCostComponentLeaf_ShowsNoStatBlock_ButItsBarterItemSiblingDoes()
        {
            // Both leaves are Decision == BuyFromVendor; only the barter
            // ITEM carries a SubtreeCost, because a currency component's
            // cost cell is deliberately blank.
            var currencyLeaf = StatGateNode(
                CraftingDecision.BuyFromVendor, "Pristine Fractal Relics", isCostComponent: true);
            var itemLeaf = StatGateNode(
                CraftingDecision.BuyFromVendor, "Unrelated Item", isCostComponent: true, subtreeCost: 5000);

            Assert.True(TreeRowTooltipComposer
                .BuildStatTooltipContent(currencyLeaf, id => CollidingItemStats()).IsEmpty);

            var itemLines = TreeRowTooltipComposer
                .BuildStatTooltipContent(itemLeaf, id => CollidingItemStats()).ToPlainLines();
            Assert.Equal("Unrelated Item", itemLines[0]);
        }

        [Fact]
        public void OrdinaryItemRow_StillShowsItsStatBlock()
        {
            var node = StatGateNode(CraftingDecision.BuyFromTp, "Unrelated Item");

            var lines = TreeRowTooltipComposer
                .BuildStatTooltipContent(node, id => CollidingItemStats()).ToPlainLines();

            Assert.Equal("Unrelated Item", lines[0]);
            // The stat block is present (its type line), but no
            // "Legendary" rarity WORD: the game shows none on a Trophy
            // (live3 fury-scorched / heart-of-destroyer, 2026-08-26). The
            // name line still carries the rarity as its colour role.
            Assert.Contains("Trophy", lines);
            Assert.DoesNotContain("Legendary", lines);
        }

        [Fact]
        public void NoLookupOrNoStatsForTheId_ReturnsEmptyContent()
        {
            var node = StatGateNode(CraftingDecision.BuyFromTp, "Unrelated Item");

            Assert.True(TreeRowTooltipComposer.BuildStatTooltipContent(node, null).IsEmpty);
            Assert.True(TreeRowTooltipComposer.BuildStatTooltipContent(node, id => null).IsEmpty);
            Assert.True(TreeRowTooltipComposer.BuildStatTooltipContent(null, id => CollidingItemStats()).IsEmpty);
        }
    }
}
