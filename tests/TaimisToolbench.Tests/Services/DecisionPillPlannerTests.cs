using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RecipeNodeBuilders;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Full CanCraft/CanBuyTp/CanBuyVendor combination matrix (the
    /// decision -> pill table) plus the HAVE/CURRENCY
    /// short-circuits, exercising the real DecisionPillPlanner.BuildPillSpecs
    /// production code - KNOWN-ISSUES #18. Also covers:
    /// the non-interactive "HAVE N/M NEEDED" annotation and the interactive
    /// "IGNORE"/"IGNORED" toggle, appended to every non-Have/non-Currency
    /// pill set (and, when active, alongside HAVE too).
    /// </summary>
    public class DecisionPillPlannerTests
    {
        private static CraftingTreeNode Node(
            CraftingDecision decision,
            bool canCraft = false, bool canBuyTp = false, bool canBuyVendor = false,
            string acquisitionBadge = null, int ownedQuantityUsed = 0, bool isIgnored = false,
            bool isAchievementBitDeduped = false, int quantity = 1,
            bool isCostComponent = false, int componentOwnedQuantity = 0,
            // Lets cost-component tests pick between the
            // item-type leaf shape (non-null SubtreeCost, a real gold
            // value - see CraftingTreeBuilder.BuildVendorCostComponentLeaves'
            // item-line branch) and the currency-type shape (SubtreeCost
            // left null - the "deliberately blank cost cell" the CURRENCY
            // badge keys off, see its currency-line branch). Null by
            // default, matching every pre-existing caller of this helper
            // (none of which ever set SubtreeCost), so this is purely
            // additive.
            long? subtreeCost = null)
        {
            return new CraftingTreeNode
            {
                ItemId = 1,
                NodeId = 1,
                Name = "Test Item",
                Quantity = quantity,
                Decision = decision,
                CanCraft = canCraft,
                CanBuyTp = canBuyTp,
                CanBuyVendor = canBuyVendor,
                AcquisitionBadge = acquisitionBadge,
                OwnedQuantityUsed = ownedQuantityUsed,
                IsIgnored = isIgnored,
                IsAchievementBitDeduped = isAchievementBitDeduped,
                IsCostComponent = isCostComponent,
                ComponentOwnedQuantity = componentOwnedQuantity,
                SubtreeCost = subtreeCost,
            };
        }

        // --- HAVE / CURRENCY short-circuits ---
        [Fact]
        public void Have_SingleHavePill_NotInteractive()
        {
            var node = Node(CraftingDecision.Have);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("HAVE", specs[0].Text);
            Assert.Equal(PillKind.Have, specs[0].Kind);
            Assert.Null(specs[0].Source);
        }

        [Fact]
        public void Have_NotIgnored_NoIgnorePill()
        {
            // A naturally-owned node (Quantity == 0 via real reduction, IsIgnored
            // false) has nothing to un-ignore - stays the single plain HAVE pill.
            var node = Node(CraftingDecision.Have, isIgnored: false);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Ignore);
        }

        [Fact]
        public void Have_Ignored_AddsActiveIgnoredPill()
        {
            var node = Node(CraftingDecision.Have, isIgnored: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(2, specs.Count);
            Assert.Equal("HAVE", specs[0].Text);
            var ignorePill = specs.Single(s => s.Kind == PillKind.Ignore);
            Assert.Equal("IGNORED", ignorePill.Text);
            Assert.Null(ignorePill.Source); // toggled via node identity, not an AcquisitionSource
        }

        // KNOWN-ISSUES #20.4: a node can be BOTH
        // manually ignored AND carry a nonzero OwnedQuantityUsed from an
        // earlier real reduction - CraftingTreeBuilder.BuildNode sets
        // OwnedQuantityUsed unconditionally BEFORE its IsIgnored early
        // return (see that class's own doc comment), so the two fields
        // coexist on the same node by construction. BuildPillSpecs' Have
        // branch never calls AppendOwnershipPills, so the "HAVE N/M NEEDED"
        // annotation is silently dropped here - a deliberate scope decision
        // (a fully-owned/ignored node keeps the plain HAVE+IGNORED
        // treatment - see PartialOwnership_AddsOwnedInfoPill_SourcePillUnchanged
        // and FullOwnership_CollapsesToHave_NoOwnedInfoPill for the
        // non-ignored halves of this same distinction), not an oversight.
        // This pins the actual rendered output for the combination.
        [Fact]
        public void Have_IgnoredAndPartiallyOwned_ShowsIgnoredNotOwnedInfo()
        {
            var node = Node(CraftingDecision.Have, isIgnored: true, ownedQuantityUsed: 3);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(2, specs.Count);
            Assert.Equal("HAVE", specs[0].Text);
            var ignorePill = specs.Single(s => s.Kind == PillKind.Ignore);
            Assert.Equal("IGNORED", ignorePill.Text);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.OwnedInfo);
        }

        // ---- Achievement-bit dedup pill ----
        [Fact]
        public void Have_AchievementBitDeduped_SingleCountedElsewherePill_NoPlainHave()
        {
            // Unlike Ignore, the dedup pill REPLACES HAVE entirely (Section
            // 4.3 of the research report - "a single non-interactive
            // pill") since nothing here is actually owned.
            var node = Node(CraftingDecision.Have, isAchievementBitDeduped: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("COUNTED ELSEWHERE", specs[0].Text);
            Assert.Equal(PillKind.AchievementBitDeduped, specs[0].Kind);
            Assert.Null(specs[0].Source);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Have);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Ignore);
        }

        [Fact]
        public void Have_NotAchievementBitDeduped_PlainHaveUnaffected()
        {
            // Regression: the new check must not change the plain HAVE case
            // (the overwhelming majority - every existing seed row).
            var node = Node(CraftingDecision.Have, isAchievementBitDeduped: false);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("HAVE", specs[0].Text);
            Assert.Equal(PillKind.Have, specs[0].Kind);
        }

        [Fact]
        public void Currency_SingleLockedPill_NotInteractive()
        {
            var node = Node(CraftingDecision.Currency);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("CURRENCY", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
            Assert.Null(specs[0].Source);
        }

        [Fact]
        public void Currency_NeverGetsIgnorePill_EvenWithOwnedQuantityUsed()
        {
            // Currency ownership is out of scope for the Ignore toggle
            // (Ignore is deliberately scoped to Item nodes).
            var node = Node(CraftingDecision.Currency, ownedQuantityUsed: 5);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Ignore);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.OwnedInfo);
        }

        // ---- guildupgrade-ingredients fix ----
        [Fact]
        public void GuildUpgrade_SingleLockedPill_NotInteractive()
        {
            var node = Node(CraftingDecision.GuildUpgrade);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("GUILD UPGRADE", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
            Assert.Null(specs[0].Source);
        }

        [Fact]
        public void GuildUpgrade_NeverGetsIgnorePill_EvenWithOwnedQuantityUsed()
        {
            // Mirrors Currency_NeverGetsIgnorePill_EvenWithOwnedQuantityUsed:
            // GuildUpgrade is a locked single-pill short-circuit, same as
            // Currency - never appends the Ignore/OwnedInfo pills.
            var node = Node(CraftingDecision.GuildUpgrade, ownedQuantityUsed: 5);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Ignore);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.OwnedInfo);
        }

        // ---- guildupgrade-ingredients fix, second
        // pass: "UnrecognizedIngredient" is its own Decision value now,
        // distinct from Unknown, specifically so it takes this same locked
        // single-pill short-circuit instead of falling into the
        // options.Count == 0 branch below and picking up the interactive
        // IGNORE pill - see CraftingDecision.UnrecognizedIngredient's own
        // doc comment for the full explanation of the bug this closes. ----
        [Fact]
        public void UnrecognizedIngredient_SingleLockedPill_NotInteractive()
        {
            var node = Node(CraftingDecision.UnrecognizedIngredient);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("UNRECOGNIZED", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
            Assert.Null(specs[0].Source);
        }

        [Fact]
        public void UnrecognizedIngredient_NeverGetsIgnorePill_EvenWithOwnedQuantityUsed()
        {
            // Mirrors GuildUpgrade_NeverGetsIgnorePill_EvenWithOwnedQuantityUsed.
            // This is the direct regression test for the reintroduced
            // instance-vs-class gap: before the fix, this Decision value
            // did not exist and this node shared CraftingDecision.Unknown
            // with a genuine no-source "Item" node, so it fell into the
            // options.Count == 0 branch and got a live, clickable IGNORE
            // pill keyed on a non-item id.
            var node = Node(CraftingDecision.UnrecognizedIngredient, ownedQuantityUsed: 5);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Ignore);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.OwnedInfo);
        }

        // --- (F,F,F): no feasible source at all ---
        [Fact]
        public void NoSource_NoBadge_LockedUnknownPill()
        {
            var node = Node(CraftingDecision.Unknown);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(2, specs.Count); // UNKNOWN + IGNORE
            Assert.Equal("UNKNOWN", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
            Assert.Null(specs[0].Source);
            var ignorePill = specs.Single(s => s.Kind == PillKind.Ignore);
            Assert.Equal("IGNORE", ignorePill.Text);
        }

        [Fact]
        public void NoSource_WithBadge_LockedBadgePill_NotUnknown()
        {
            var node = Node(CraftingDecision.Unknown, acquisitionBadge: "SALVAGE");
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(2, specs.Count); // SALVAGE + IGNORE
            Assert.Equal("SALVAGE", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
        }

        [Theory]
        [InlineData("VENDOR")]
        [InlineData("Vendor")]
        [InlineData("CRAFT")]
        [InlineData("TP")]
        public void NoSource_BadgeCollidingWithASourcePill_FallsBackToUnknown(string badge)
        {
            // A badge pill and a single-source pill are byte-identical in
            // text, Kind and styling but mean opposite things: the source
            // pill's cost is in Plan.TotalCoinCost, the badge's node is
            // Unknown and contributes 0. Compare with
            // OnlyVendor_SingleLockedVendorPill below - that is the row this
            // one must not be mistaken for.
            var node = Node(CraftingDecision.Unknown, acquisitionBadge: badge);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(2, specs.Count); // UNKNOWN + IGNORE
            Assert.Equal("UNKNOWN", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
            Assert.Null(specs[0].Source);
        }

        // --- Exactly one feasible source: single Locked pill (+ IGNORE) ---
        [Fact]
        public void OnlyTp_SingleLockedTpPill()
        {
            var node = Node(CraftingDecision.BuyFromTp, canBuyTp: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(2, specs.Count); // TP + IGNORE
            Assert.Equal("TP", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
            Assert.Null(specs[0].Source);
        }

        [Fact]
        public void OnlyVendor_SingleLockedVendorPill()
        {
            var node = Node(CraftingDecision.BuyFromVendor, canBuyVendor: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(2, specs.Count); // VENDOR + IGNORE
            Assert.Equal("VENDOR", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
        }

        [Fact]
        public void OnlyCraft_SingleLockedCraftPill()
        {
            var node = Node(CraftingDecision.Craft, canCraft: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(2, specs.Count); // CRAFT + IGNORE
            Assert.Equal("CRAFT", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
        }

        // --- Two feasible sources: multi-pill, selected == node.Decision ---
        [Theory]
        [InlineData(nameof(CraftingDecision.BuyFromTp), "TP", "VENDOR")]
        [InlineData(nameof(CraftingDecision.BuyFromVendor), "VENDOR", "TP")]
        public void TpAndVendor_TwoPills_SelectedMatchesDecision(
            string decisionName, string selectedText, string availableText)
        {
            var decision = EnumArg.Parse<CraftingDecision>(decisionName);
            var node = Node(decision, canBuyTp: true, canBuyVendor: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(3, specs.Count); // TP + VENDOR + IGNORE
            var selected = specs.Single(s => s.Kind == PillKind.Selected);
            var available = specs.Single(s => s.Kind == PillKind.Available);

            Assert.Equal(selectedText, selected.Text);
            Assert.Null(selected.Source); // selected pill is a no-op, never clickable
            Assert.Equal(availableText, available.Text);
            Assert.NotNull(available.Source); // available pill applies an override
            Assert.Contains(specs, s => s.Kind == PillKind.Ignore && s.Text == "IGNORE");
        }

        [Theory]
        [InlineData(nameof(CraftingDecision.Craft), "CRAFT", "TP")]
        [InlineData(nameof(CraftingDecision.BuyFromTp), "TP", "CRAFT")]
        public void CraftAndTp_TwoPills_SelectedMatchesDecision(
            string decisionName, string selectedText, string availableText)
        {
            var decision = EnumArg.Parse<CraftingDecision>(decisionName);
            var node = Node(decision, canCraft: true, canBuyTp: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(3, specs.Count); // CRAFT + TP + IGNORE
            Assert.Equal(selectedText, specs.Single(s => s.Kind == PillKind.Selected).Text);
            Assert.Equal(availableText, specs.Single(s => s.Kind == PillKind.Available).Text);
        }

        [Theory]
        [InlineData(nameof(CraftingDecision.Craft), "CRAFT", "VENDOR")]
        [InlineData(nameof(CraftingDecision.BuyFromVendor), "VENDOR", "CRAFT")]
        public void CraftAndVendor_TwoPills_SelectedMatchesDecision(
            string decisionName, string selectedText, string availableText)
        {
            var decision = EnumArg.Parse<CraftingDecision>(decisionName);
            var node = Node(decision, canCraft: true, canBuyVendor: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(3, specs.Count); // CRAFT + VENDOR + IGNORE
            Assert.Equal(selectedText, specs.Single(s => s.Kind == PillKind.Selected).Text);
            Assert.Equal(availableText, specs.Single(s => s.Kind == PillKind.Available).Text);
        }

        // --- All three feasible: the highlighted pill MUST match the
        // solver's actual committed Source, whichever of the three it is
        // ---
        [Theory]
        [InlineData(nameof(CraftingDecision.Craft), "CRAFT")]
        [InlineData(nameof(CraftingDecision.BuyFromTp), "TP")]
        [InlineData(nameof(CraftingDecision.BuyFromVendor), "VENDOR")]
        public void AllThreeFeasible_SelectedPillAlwaysMatchesCommittedSource(
            string decisionName, string expectedSelectedText)
        {
            var decision = EnumArg.Parse<CraftingDecision>(decisionName);
            var node = Node(decision, canCraft: true, canBuyTp: true, canBuyVendor: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(4, specs.Count); // CRAFT + TP + VENDOR + IGNORE
            Assert.Equal(new[] { "CRAFT", "TP", "VENDOR", "IGNORE" }, specs.Select(s => s.Text));

            var selected = specs.Single(s => s.Kind == PillKind.Selected);
            Assert.Equal(expectedSelectedText, selected.Text);
            Assert.Null(selected.Source);

            // Every other SOURCE pill (excluding the trailing IGNORE
            // annotation, which has its own Kind) is Available and
            // independently clickable - the per-pill override model,
            // not a single cycle button.
            foreach (var other in specs.Where(s => s.Kind != PillKind.Selected && s.Kind != PillKind.Ignore))
            {
                Assert.Equal(PillKind.Available, other.Kind);
                Assert.NotNull(other.Source);
            }
        }

        [Fact]
        public void AvailablePill_SourceMatchesItsOwnAcquisitionSource()
        {
            var node = Node(CraftingDecision.BuyFromTp, canCraft: true, canBuyTp: true, canBuyVendor: true);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            var craftPill = specs.Single(s => s.Text == "CRAFT");
            var vendorPill = specs.Single(s => s.Text == "VENDOR");
            Assert.Equal(AcquisitionSource.Craft, craftPill.Source);
            Assert.Equal(AcquisitionSource.BuyFromVendor, vendorPill.Source);
        }

        // --- "HAVE N/M NEEDED" annotation (reported in game:
        // widened to show the original total demand, not just the covered
        // count, alongside the tree row's own remaining-need "Nx" prefix;
        // the final wording pass moved OWNED away
        // from sitting next to the total - see AppendOwnershipPills' doc
        // comment) ---
        [Theory]
        [InlineData(nameof(CraftingDecision.Craft), true, false, false, "CRAFT")]
        [InlineData(nameof(CraftingDecision.BuyFromTp), false, true, false, "TP")]
        [InlineData(nameof(CraftingDecision.BuyFromVendor), false, false, true, "VENDOR")]
        [InlineData(nameof(CraftingDecision.Unknown), false, false, false, "UNKNOWN")]
        public void PartialOwnership_AddsOwnedInfoPill_SourcePillUnchanged(
            string decisionName, bool canCraft, bool canBuyTp, bool canBuyVendor, string expectedSourceText)
        {
            var decision = EnumArg.Parse<CraftingDecision>(decisionName);
            // node.Quantity defaults to 1 (Node() helper), so total demand
            // = 4 owned + 1 remaining = 5.
            var node = Node(decision, canCraft, canBuyTp, canBuyVendor, ownedQuantityUsed: 4);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Equal(expectedSourceText, specs[0].Text);
            var ownedPill = specs.Single(s => s.Kind == PillKind.OwnedInfo);
            Assert.Equal("HAVE 4/5 NEEDED", ownedPill.Text);
            Assert.Null(ownedPill.Source);
            Assert.Contains(specs, s => s.Kind == PillKind.Ignore && s.Text == "IGNORE");
        }

        [Fact]
        public void PartialOwnership_TotalDemand_SumsOwnedAndRemainingQuantity()
        {
            // Regression guard for finding A's paradox, reported in game:
            // a large remaining need (120) alongside a large owned count
            // (130) must show the true original total (250), not either
            // number alone.
            var node = Node(CraftingDecision.BuyFromTp, canBuyTp: true, ownedQuantityUsed: 130, quantity: 120);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            var ownedPill = specs.Single(s => s.Kind == PillKind.OwnedInfo);
            Assert.Equal("HAVE 130/250 NEEDED", ownedPill.Text);
        }

        [Theory]
        [InlineData(nameof(CraftingDecision.Craft), true, false, false)]
        [InlineData(nameof(CraftingDecision.BuyFromTp), false, true, false)]
        [InlineData(nameof(CraftingDecision.BuyFromVendor), false, false, true)]
        [InlineData(nameof(CraftingDecision.Unknown), false, false, false)]
        public void NoOwnership_NoOwnedInfoPill(
            string decisionName, bool canCraft, bool canBuyTp, bool canBuyVendor)
        {
            var decision = EnumArg.Parse<CraftingDecision>(decisionName);
            var node = Node(decision, canCraft, canBuyTp, canBuyVendor, ownedQuantityUsed: 0);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.DoesNotContain(specs, s => s.Kind == PillKind.OwnedInfo);
            Assert.Contains(specs, s => s.Kind == PillKind.Ignore && s.Text == "IGNORE");
        }

        [Fact]
        public void FullOwnership_CollapsesToHave_NoOwnedInfoPill()
        {
            // "Full" ownership means the node's whole demand was covered,
            // which (per CraftingTreeBuilder) always means Decision == Have
            // in production - the OwnedInfo pill only ever fires on the
            // real craft/tp/vendor/unknown paths for a PARTIALLY-covered
            // node; a fully-owned node keeps the plain HAVE treatment.
            var node = Node(CraftingDecision.Have, ownedQuantityUsed: 10);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("HAVE", specs[0].Text);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.OwnedInfo);
        }

        // --- End-to-end via the real solver + tree builder: proves the
        // pill mapping never desyncs from an actual PlanSolver decision,
        // not just a hand-built CraftingTreeNode. ---
        // Leaf comes from Helpers/RecipeNodeBuilders.cs.
        [Fact]
        public void RealSolver_TpCheaperThanVendor_TpPillSelected()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice> { { 1, new ItemPrice { ItemId = 1, BuyInstant = 50 } } };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    1, new List<VendorOffer>
                    {
                        new VendorOffer
                        {
                            OfferId = "v1", OutputItemId = 1, OutputCount = 1,
                            CostLines = new List<CostLine> { new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 200 } },
                            MerchantName = "Test Vendor",
                        },
                    }
                },
            };

            var solver = new PlanSolver();
            var solveResult = solver.Solve(tree, prices, vendorOffers);
            var builder = new CraftingTreeBuilder();
            var node = builder.BuildTree(tree, solveResult.Decisions, new Dictionary<int, ItemMetadata>());

            Assert.Equal(CraftingDecision.BuyFromTp, node.Decision);

            var specs = DecisionPillPlanner.BuildPillSpecs(node);
            // TP + VENDOR (no recipe -> no CRAFT). No IGNORE: BuildTree
            // marked this node a plan root - see PlanRootIgnoreTests.
            Assert.Equal(2, specs.Count);
            var selected = specs.Single(s => s.Kind == PillKind.Selected);
            Assert.Equal("TP", selected.Text);
        }

        [Fact]
        public void RealSolver_VendorCheaperThanTp_VendorPillSelected()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice> { { 1, new ItemPrice { ItemId = 1, BuyInstant = 500 } } };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    1, new List<VendorOffer>
                    {
                        new VendorOffer
                        {
                            OfferId = "v1", OutputItemId = 1, OutputCount = 1,
                            CostLines = new List<CostLine> { new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 50 } },
                            MerchantName = "Test Vendor",
                        },
                    }
                },
            };

            var solver = new PlanSolver();
            var solveResult = solver.Solve(tree, prices, vendorOffers);
            var builder = new CraftingTreeBuilder();
            var node = builder.BuildTree(tree, solveResult.Decisions, new Dictionary<int, ItemMetadata>());

            Assert.Equal(CraftingDecision.BuyFromVendor, node.Decision);

            var specs = DecisionPillPlanner.BuildPillSpecs(node);
            var selected = specs.Single(s => s.Kind == PillKind.Selected);
            Assert.Equal("VENDOR", selected.Text);
        }

        [Fact]
        public void RealSolver_FallbackOnlyVendor_StillShowsAvailableVendorPill()
        {
            // A vendor offer priced entirely in an unvalued non-coin
            // currency is fallback-tier only (never actually compared
            // against TP in PickCheapest - PlanSolver.cs EvaluateVendorOffers),
            // yet CanBuyVendor is still true (B1's deliberate one-flag
            // design - "would overriding to Vendor succeed" - see
            // SolverDecision.CanBuyVendor's doc comment). Per KNOWN-ISSUES
            // #18a, the VENDOR pill still renders as a real, clickable
            // Available alternative - this is intentional, not a bug to
            // suppress.
            var tree = Leaf(1, 2);
            var prices = new Dictionary<int, ItemPrice> { { 1, new ItemPrice { ItemId = 1, BuyInstant = 50 } } };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    1, new List<VendorOffer>
                    {
                        new VendorOffer
                        {
                            OfferId = "v1", OutputItemId = 1, OutputCount = 1,
                            CostLines = new List<CostLine> { new CostLine { Type = "Currency", Id = 23, Count = 500 } },
                            MerchantName = "Test Vendor",
                        },
                    }
                },
            };

            var solver = new PlanSolver();
            var solveResult = solver.Solve(tree, prices, vendorOffers);
            var builder = new CraftingTreeBuilder();
            var node = builder.BuildTree(tree, solveResult.Decisions, new Dictionary<int, ItemMetadata>());

            // TP wins (comparable vendor value is null - only the fallback
            // tier exists), but the vendor offer is still overridable.
            Assert.Equal(CraftingDecision.BuyFromTp, node.Decision);
            Assert.True(node.CanBuyVendor);

            var specs = DecisionPillPlanner.BuildPillSpecs(node);
            var vendorPill = specs.Single(s => s.Text == "VENDOR");
            Assert.Equal(PillKind.Available, vendorPill.Kind);
            Assert.Equal(AcquisitionSource.BuyFromVendor, vendorPill.Source);
        }

        [Fact]
        public void RealSolver_UnknownSource_NeverHasChildren_NoLiveCraftSubtreeUnderUnknownPill()
        {
            // KNOWN-ISSUES #18c: the UNKNOWN pill must never coexist with a
            // live craft subtree. This is structurally
            // guaranteed (CanCraft is now always true whenever a recipe
            // exists, so Decision == Unknown implies no recipe at all,
            // hence no children could ever be built) - this test locks
            // that invariant in against a future regression.
            var tree = Leaf(1, 1); // no recipes, no price, no vendor offer
            var prices = new Dictionary<int, ItemPrice>();

            var solver = new PlanSolver();
            var solveResult = solver.Solve(tree, prices, null);
            var builder = new CraftingTreeBuilder();
            var node = builder.BuildTree(tree, solveResult.Decisions, new Dictionary<int, ItemMetadata>());

            Assert.Equal(CraftingDecision.Unknown, node.Decision);
            Assert.Empty(node.Children);
            Assert.False(node.CanCraft);

            var specs = DecisionPillPlanner.BuildPillSpecs(node);
            // UNKNOWN alone: this node is the plan root BuildTree
            // returned, so no IGNORE toggle - see PlanRootIgnoreTests.
            Assert.Equal("UNKNOWN", Assert.Single(specs).Text);
        }

        // ---- Cost-component leaves - informational-only pill
        // vocabulary ----
        //
        // In-game finding: the earlier HAVE/
        // "HAVE x/y NEEDED" vocabulary was replaced by a subdued "OWN n"
        // badge (PillKind.OwnedInfo, the same muted-gold kind the ordinary
        // partial-ownership annotation uses) showing the raw
        // ComponentOwnedQuantity holding - no full-vs-partial split,
        // because ownership never changes what a component leaf costs
        // either way (see DecisionPillPlanner.BuildPillSpecs' own doc
        // comment). Tests below use the item-type leaf shape (a real
        // SubtreeCost gold value) unless named "CurrencyType", so they
        // isolate the OWN-badge behavior from the separate CURRENCY-badge
        // behavior covered by its own block further down.
        [Fact]
        public void CostComponent_NoOwnership_NoPill()
        {
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 5,
                componentOwnedQuantity: 0, subtreeCost: 100);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Empty(specs);
        }

        [Fact]
        public void CostComponent_ItemType_PartialOwnership_ShowsOwnBadge_NoCurrencyBadge()
        {
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 10,
                componentOwnedQuantity: 4, subtreeCost: 100);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("OWN 4", specs[0].Text);
            Assert.Equal(PillKind.OwnedInfo, specs[0].Kind);
            Assert.Null(specs[0].Source);
        }

        [Fact]
        public void CostComponent_ItemType_FullOwnership_StillShowsOwnBadge_NotHave()
        {
            // Full coverage no longer collapses to a plain HAVE pill for a
            // cost component - the blue HAVE vocabulary means "reduced the
            // plan cost" everywhere else in the tree, which is never true
            // here, so the badge stays "OWN n" regardless of whether n
            // covers the full need.
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 6,
                componentOwnedQuantity: 6, subtreeCost: 100);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("OWN 6", specs[0].Text);
            Assert.Equal(PillKind.OwnedInfo, specs[0].Kind);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Have);
        }

        [Fact]
        public void CostComponent_OwnershipExceedsQuantity_BadgeShowsRawHolding()
        {
            // The badge shows the raw ComponentOwnedQuantity holding with
            // no second capping against Quantity at this layer - production
            // CraftingTreeBuilder.ResolveOwnedQuantity already caps it to
            // min(owned, needed) before it ever reaches here
            // (CraftingTreeBuilderTests covers that separately), so this
            // pins that BuildPillSpecs itself performs no clamp of its own.
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 6,
                componentOwnedQuantity: 999, subtreeCost: 100);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("OWN 999", specs[0].Text);
        }

        [Fact]
        public void CostComponent_NeverGetsDecisionOrIgnorePills()
        {
            // Even when CanCraft/CanBuyTp/CanBuyVendor happen to be true
            // (never set by the real builder, but the pill planner must
            // never let a decision pill leak through regardless), a cost
            // component gets ONLY the informational badge(s) - no CRAFT/TP/
            // VENDOR/UNKNOWN pill, no IGNORE toggle, not override-clickable.
            var node = Node(
                CraftingDecision.BuyFromVendor, canCraft: true, canBuyTp: true, canBuyVendor: true,
                isCostComponent: true, quantity: 6, componentOwnedQuantity: 3, subtreeCost: 100);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal(PillKind.OwnedInfo, specs[0].Kind);
            Assert.All(specs, s => Assert.Null(s.Source));
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Ignore);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Selected);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Available);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Locked);
        }

        // ---- "CURRENCY" badge on the blank-cost-cell
        // (currency-type) component shape - explains at a glance why no
        // gold value is shown, gw2efficiency's own grey Currency-badge
        // pattern. ----
        [Fact]
        public void CostComponent_CurrencyType_BlankCostCell_ShowsCurrencyBadge()
        {
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 5,
                componentOwnedQuantity: 0, subtreeCost: null);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("CURRENCY", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
            Assert.Null(specs[0].Source);
        }

        // currency-ux-package (Feature 2, supersedes the old "...
        // ShowsBothBadgesTogether..." test name/assertion below): a
        // currency-type component's row-scope "OWN n" badge is gone
        // outright now - see AppendCurrencyOwnershipPill's own doc comment
        // for why row-scope ComponentOwnedQuantity is no longer used at
        // all for a currency leaf. Without plan-scope
        // currencyPlanTotals/ownedCurrencyAmounts (both omitted here, the
        // pre-Feature-2 call shape), the leaf shows ONLY its CURRENCY
        // badge - see the *_WithPlanScopeOwnership tests below for the new
        // pill's actual appended shape.
        [Fact]
        public void CostComponent_CurrencyType_WithRowScopeOwnershipOnly_ShowsOnlyCurrencyBadge()
        {
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 5,
                componentOwnedQuantity: 3, subtreeCost: null);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Single(specs);
            Assert.Equal("CURRENCY", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
        }

        // --- currency-ux-package (Feature 2): plan-scope currency pill ---
        [Fact]
        public void CostComponent_CurrencyType_WithPlanScopeOwnership_PartialCoverage_AppendsHaveTotalPill()
        {
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 5,
                componentOwnedQuantity: 3, subtreeCost: null);
            var totals = new Dictionary<int, long> { { node.ItemId, 100 } };
            var owned = new Dictionary<int, int> { { node.ItemId, 40 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, totals, owned);

            Assert.Equal(2, specs.Count);
            Assert.Equal("CURRENCY", specs[0].Text);
            Assert.Equal(PillKind.Locked, specs[0].Kind);
            Assert.Equal("HAVE 40/100 TOTAL", specs[1].Text);
            Assert.Equal(PillKind.OwnedInfo, specs[1].Kind);
            Assert.Null(specs[1].Source);
        }

        [Fact]
        public void CostComponent_CurrencyType_WithPlanScopeOwnership_FullCoverage_CollapsesToPlainHavePill()
        {
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 5,
                componentOwnedQuantity: 3, subtreeCost: null);
            var totals = new Dictionary<int, long> { { node.ItemId, 100 } };
            var owned = new Dictionary<int, int> { { node.ItemId, 100 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, totals, owned);

            Assert.Equal(2, specs.Count);
            Assert.Equal("HAVE", specs[1].Text);
            Assert.Equal(PillKind.Have, specs[1].Kind);
        }

        [Fact]
        public void CurrencyDecision_WithPlanScopeOwnership_AppendsHaveTotalPillAfterCurrencyBadge()
        {
            var node = Node(CraftingDecision.Currency, quantity: 50);
            var totals = new Dictionary<int, long> { { node.ItemId, 500 } };
            var owned = new Dictionary<int, int> { { node.ItemId, 200 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, totals, owned);

            Assert.Equal(2, specs.Count);
            Assert.Equal("CURRENCY", specs[0].Text);
            Assert.Equal("HAVE 200/500 TOTAL", specs[1].Text);
            Assert.Equal(PillKind.OwnedInfo, specs[1].Kind);
        }

        [Fact]
        public void CurrencyDecision_NoWalletSnapshot_OmitsHaveTotalPillEntirely()
        {
            // ownedCurrencyAmounts null (no snapshot at all) - "have" is
            // genuinely unknown, not zero, so the pill must be omitted
            // rather than implying 0 owned.
            var node = Node(CraftingDecision.Currency, quantity: 50);
            var totals = new Dictionary<int, long> { { node.ItemId, 500 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, totals, ownedCurrencyAmounts: null);

            Assert.Single(specs);
            Assert.Equal("CURRENCY", specs[0].Text);
        }

        [Fact]
        public void CurrencyDecision_SnapshotPresentButThisCurrencyAbsent_OmitsHaveTotalPill()
        {
            // A snapshot exists but has no entry at all for THIS currency
            // id - same "unknown, not zero" reasoning as the null-snapshot
            // case above.
            var node = Node(CraftingDecision.Currency, quantity: 50);
            var totals = new Dictionary<int, long> { { node.ItemId, 500 } };
            var owned = new Dictionary<int, int> { { 999, 10 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, totals, owned);

            Assert.Single(specs);
            Assert.Equal("CURRENCY", specs[0].Text);
        }

        [Fact]
        public void CurrencyDecision_ZeroOwned_ShowsHaveZeroTotalPill()
        {
            // A snapshot exists and explicitly reports 0 for this currency -
            // distinct from "unknown" above - still shows the pill.
            var node = Node(CraftingDecision.Currency, quantity: 50);
            var totals = new Dictionary<int, long> { { node.ItemId, 500 } };
            var owned = new Dictionary<int, int> { { node.ItemId, 0 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, totals, owned);

            Assert.Equal(2, specs.Count);
            Assert.Equal("HAVE 0/500 TOTAL", specs[1].Text);
        }

        [Fact]
        public void CurrencyDecision_ZeroOwnedNoPlanTotal_OmitsHaveTotalPillEntirely()
        {
            // The old
            // `long planTotal = 0; currencyPlanTotals?.TryGetValue(...)`
            // default made "this id has no plan total at all" (reachable
            // whenever ownedCurrencyAmounts is widened - via
            // CraftingPlanPipeline.BuildOwnedCurrencyAmounts's own vendor-
            // offer scan - beyond plan.CurrencyCosts, which is exactly
            // where currencyPlanTotals comes from) indistinguishable from
            // "the plan genuinely needs zero of this currency". With
            // have=0 too, `0 &gt;= 0` rendered a plain blue "HAVE" (full
            // coverage) pill - this test is the have=0/no-plan-total case
            // the existing CurrencyDecision_ZeroOwned_ShowsHaveZeroTotalPill
            // above does not cover (that one always supplies planTotal=500).
            var node = Node(CraftingDecision.Currency, quantity: 50);
            var totals = new Dictionary<int, long>(); // no entry at all for node.ItemId
            var owned = new Dictionary<int, int> { { node.ItemId, 0 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, totals, owned);

            Assert.Single(specs);
            Assert.Equal("CURRENCY", specs[0].Text);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.Have);
            Assert.DoesNotContain(specs, s => s.Kind == PillKind.OwnedInfo);
        }

        [Fact]
        public void CurrencyDecision_HaveExceedsButNoPlanTotal_OmitsHaveTotalPillEntirely()
        {
            // Same gap as above, with a non-zero `have` too (rules out any
            // reliance on have's own zero-ness rather than the missing
            // plan total being what gates the pill).
            var node = Node(CraftingDecision.Currency, quantity: 50);
            var totals = new Dictionary<int, long>();
            var owned = new Dictionary<int, int> { { node.ItemId, 999 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, totals, owned);

            Assert.Single(specs);
            Assert.Equal("CURRENCY", specs[0].Text);
        }

        [Fact]
        public void CostComponent_ItemType_WithPlanScopeArgs_StillShowsOwnBadgeUnchanged()
        {
            // Feature 2 scope: an ITEM-type cost component (non-null
            // SubtreeCost) keeps its row-scope "OWN n" badge unchanged,
            // even when plan-scope currency args are supplied (they are
            // simply irrelevant to an item-type leaf).
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 5,
                componentOwnedQuantity: 3, subtreeCost: 100);
            var totals = new Dictionary<int, long> { { node.ItemId, 500 } };
            var owned = new Dictionary<int, int> { { node.ItemId, 500 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, totals, owned);

            Assert.Single(specs);
            Assert.Equal("OWN 3", specs[0].Text);
            Assert.Equal(PillKind.OwnedInfo, specs[0].Kind);
        }

        [Fact]
        public void CostComponent_ItemType_NeverGetsCurrencyBadge()
        {
            // A non-null SubtreeCost (the item-type leaf shape) must never
            // carry the CURRENCY badge, even when the gold value itself
            // happens to be 0 - the badge is keyed off SubtreeCost.HasValue,
            // not the amount, so "no pill at all" stays exactly empty here,
            // not a stray CURRENCY badge.
            var node = Node(
                CraftingDecision.BuyFromVendor, isCostComponent: true, quantity: 5,
                componentOwnedQuantity: 0, subtreeCost: 0);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Empty(specs);
        }

        // --- IsInteractive: which pills advertise a click ---
        //
        // The view wires handlers from this predicate on a live row, and
        // (because a dimmed reference branch wires none at all) uses the
        // same predicate to decide which dimmed pills need the "why did
        // nothing happen" tooltip. A pill that answers true here and gets
        // no handler is exactly the dead click that has to be explained.
        [Fact]
        public void IsInteractive_SourcePillsAndIgnore_True_SelectedAndAnnotationsFalse()
        {
            var node = Node(CraftingDecision.Craft, canCraft: true, canBuyTp: true, ownedQuantityUsed: 2);
            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            var craft = specs.Single(s => s.Text == "CRAFT");
            var tp = specs.Single(s => s.Text == "TP");
            var owned = specs.Single(s => s.Kind == PillKind.OwnedInfo);
            var ignore = specs.Single(s => s.Kind == PillKind.Ignore);

            // CRAFT is the committed choice: non-interactive, since
            // clicking it would be a no-op re-solve.
            Assert.Equal(PillKind.Selected, craft.Kind);
            Assert.False(DecisionPillPlanner.IsInteractive(craft));
            Assert.True(DecisionPillPlanner.IsInteractive(tp));
            Assert.False(DecisionPillPlanner.IsInteractive(owned));
            Assert.True(DecisionPillPlanner.IsInteractive(ignore));
        }

        [Fact]
        public void IsInteractive_SubduedPill_StaysTrue()
        {
            // A decisively-losing option is styled muted but is still a
            // real override - it must not be mistaken for chrome.
            var spec = new PillSpec("VENDOR", AcquisitionSource.BuyFromVendor, PillKind.Subdued);

            Assert.True(DecisionPillPlanner.IsInteractive(spec));
        }

        [Fact]
        public void IsInteractive_SoleSourceAndBadgePills_False()
        {
            // One feasible source collapses to a Locked pill with no
            // AcquisitionSource; UNKNOWN/UNRECOGNIZED/CURRENCY/GUILD
            // UPGRADE do the same. None of them is a click target, so none
            // needs a dead-click explanation when dimmed.
            var soleSource = DecisionPillPlanner.BuildPillSpecs(
                Node(CraftingDecision.Craft, canCraft: true)).Single(s => s.Text == "CRAFT");
            var unrecognized = DecisionPillPlanner.BuildPillSpecs(
                Node(CraftingDecision.UnrecognizedIngredient)).Single();
            var guildUpgrade = DecisionPillPlanner.BuildPillSpecs(
                Node(CraftingDecision.GuildUpgrade)).Single();

            Assert.False(DecisionPillPlanner.IsInteractive(soleSource));
            Assert.False(DecisionPillPlanner.IsInteractive(unrecognized));
            Assert.False(DecisionPillPlanner.IsInteractive(guildUpgrade));
        }

        [Fact]
        public void IsInteractive_IgnoredToggle_StaysTrue()
        {
            // The "IGNORED" state of the toggle carries no
            // AcquisitionSource either, but un-ignoring is the whole point
            // of the pill - Kind alone has to carry it.
            var specs = DecisionPillPlanner.BuildPillSpecs(
                Node(CraftingDecision.Have, isIgnored: true));

            Assert.True(DecisionPillPlanner.IsInteractive(specs.Single(s => s.Text == "IGNORED")));
            Assert.False(DecisionPillPlanner.IsInteractive(specs.Single(s => s.Text == "HAVE")));
        }
    }
}
