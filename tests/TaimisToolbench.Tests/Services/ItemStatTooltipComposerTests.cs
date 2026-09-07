using System;
using System.Linq;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Runs the whole live-JSON -> parser -> factory -> composer path for
    /// each item class the feature has to survive, and asserts on the lines
    /// a reader would actually see.
    /// </summary>
    public class ItemStatTooltipComposerTests
    {
        private static async Task<string[]> LinesFor(string itemJson)
        {
            var raw = await RealItemFixtures.ParseOneAsync(itemJson);
            return ItemStatTooltipComposer.BuildContent(ItemStatBlockFactory.Build(raw))
                .ToPlainLines().ToArray();
        }

        [Fact]
        public async Task FixedStatArmor_ReadsLikeTheInGameTooltip()
        {
            Assert.Equal(new[]
            {
                "Zojja's Warfists",
                "Defense: 191",
                "+47 Power",
                "+34 Precision",
                "+34 Ferocity",
                "",
                "Unused Upgrade Slot",
                "",
                "Unused Infusion Slot",
                "",
                "Ascended",
                "Heavy",
                "Gloves Armor",
                "Required Level: 80",
                "Crafted in the style of the renowned asuran genius, Zojja.",
                "Account Bound",
                "2s 40c",
            }, await LinesFor(RealItemJson.ZojjasWarfists));
        }

        [Fact]
        public async Task StatSelectableLegendary_ShowsSelectStatsAndNoInventedNumbers()
        {
            var lines = await LinesFor(RealItemJson.Bolt);

            Assert.Equal("Bolt", lines[0]);
            Assert.Equal("Weapon Strength: 950 - 1,050", lines[1]);
            Assert.Equal("", lines[2]);
            Assert.Equal("Unused Infusion Slot", lines[3]);
            // The game's own string for an unassigned stat-selectable
            // item, in the DESCRIPTION position inside the identity block.
            Assert.Contains("Double-click to select stats.", lines);
            Assert.Contains("(Main Hand)", lines);
            Assert.Contains("Damage Type: Lightning", lines);
            Assert.Contains("Legendary", lines);
            Assert.Contains("Sword", lines);

            // NoSell, and a weapon's defense:0 - neither may appear. The
            // value line is omitted ENTIRELY, so there is no last line to
            // hold it and no blank separator in front of one.
            Assert.False(await HasCoinSpan(RealItemJson.Bolt));
            Assert.DoesNotContain(lines, l => l.StartsWith("Defense"));
            // No attribute lines at all - a stat-selectable item has no
            // resolved numbers until a combination is chosen.
            Assert.DoesNotContain(lines, l => l.StartsWith("+"));
        }

        [Fact]
        public async Task CraftingMaterial_LeadsWithItsDescriptionAndShowsNoTypeLine()
        {
            // The live3 material captures (vials/eyes/almonds, 2026-08-26)
            // show NO "Crafting Material" type line and no rarity word for
            // any material rarity; the description runs contiguous under
            // the header (almonds' "Ingredient" IS 12337's description
            // field) and the value contiguous under the block above it
            // (vials: discipline line -> coin row at one 16px pitch).
            Assert.Equal(new[]
            {
                "Mithril Ore",
                "Refine into Ingots.",
                "7c",
            }, await LinesFor(RealItemJson.MithrilOre));
        }

        [Fact]
        public async Task GiftOfTwilight_MatchesTheInGameCaptureLineForLine()
        {
            // The A/B: item 19648 hovered in the module and in the live
            // game, both captured. The game's box
            // reads header, description paragraph, blank, forge line, four
            // bullets, blank, "Trophy", "Account Bound", 6s 40c - and the
            // bound line is BARE, which is what this capture corrected.
            // (The game additionally wraps the description after
            // "greatsword"; that is layout, pinned in
            // TooltipLayoutMathTests, not composition.)
            Assert.Equal(new[]
            {
                "Gift of Twilight",
                "A gift used to create the legendary greatsword Twilight.",
                "",
                "Made by combining these items in the Mystic Forge:",
                "\u2022 1 Gift of Metal",
                "\u2022 1 Gift of Darkness",
                "\u2022 100 Icy Runestones",
                "\u2022 1 Superior Sigil of Blood",
                "",
                "Trophy",
                "Account Bound",
                "6s 40c",
            }, await LinesFor(RealItemJson.GiftOfTwilight));
        }

        [Fact]
        public async Task AConsumablesValueFollowsTheLineAboveItWithNoBlank()
        {
            // Measured on steak.png (2012) and re-confirmed across live3
            // (2026-08-26): every capture that shows a value line at all -
            // vials, fury-scorched, red-festival-lantern,
            // counterfeit-ticket, sigil-rage, relic-livingcity - runs it
            // contiguous under the line above it.
            var lines = await LinesFor(RealItemJson.CilantroSteak);

            Assert.Equal("Account Bound", lines[lines.Length - 2]);
            Assert.Equal("1s 65c", lines[lines.Length - 1]);
        }

        [Theory]
        [InlineData("CraftingMaterial")]
        [InlineData("Gathering")]
        [InlineData("MiniPet")]
        [InlineData("Tool")]
        [InlineData("MountSkin")]
        public void EveryShapesValueRunsContiguousUnderTheLineAboveIt(string itemType)
        {
            // The old table gave FWDekker's Generic shape (materials,
            // unknown types) a blank above the value, on the replica's
            // <br /> before getValue(). The live3 vials capture - a
            // crafting material, exactly that shape - measures the coin
            // row ONE 16px pitch under the line above it, and no capture
            // anywhere shows the blank, so the value is contiguous for
            // every type (2026-08-26).
            var lines = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Copper Mining Pick",
                ItemType = itemType,
                Description = "Used to gather from copper ore.",
                VendorValue = 7,
            }).ToPlainLines();

            Assert.NotEqual("", lines[lines.Count - 2]);
            Assert.Equal("7c", lines[lines.Count - 1]);
        }

        [Fact]
        public async Task Rune_ShowsItsSixPositionalBonuses()
        {
            var lines = await LinesFor(RealItemJson.RuneOfTheScholar);

            // Header, one blank, then all six positional bonuses - the
            // shape FWDekker's UpgradeComponent builder emits and the one
            // the game shows for an unequipped rune (KNOWN-ISSUES #42).
            Assert.Equal("Superior Rune of the Scholar", lines[0]);
            Assert.Equal("", lines[1]);
            Assert.Equal("(1): +25 Power", lines[2]);
            Assert.Equal("(6): +125 Ferocity", lines[7]);
            Assert.Equal("65c", lines[lines.Length - 1]);

            // No "Rune" type line and no "Exotic" rarity word: the live3
            // sigil-rage capture (an Exotic UpgradeComponent) shows
            // neither, running its description straight into "Required
            // Level: 60" (2026-08-26; FWDekker's UpgradeComponent builder
            // agrees). And the description precedes the level line.
            Assert.DoesNotContain("Rune", lines);
            Assert.DoesNotContain("Exotic", lines);
            Assert.True(
                System.Array.IndexOf(lines, "Double-click to apply to a piece of armor.")
                    < System.Array.IndexOf(lines, "Required Level: 60"));
        }

        [Fact]
        public async Task SigilAndInfusion_ShowTheirBuffLine()
        {
            Assert.Contains("+5% Damage", await LinesFor(RealItemJson.SigilOfForce));

            var infusion = await LinesFor(RealItemJson.AgonyInfusion);
            // The API reports this fact twice - once as the infix buff
            // description, once as a resolved attribute that renders to the
            // identical string. The game prints it once, and so must we.
            Assert.Equal(1, infusion.Count(l => l == "+1 Agony Resistance"));
        }

        [Fact]
        public void ASigilsCooldownReminderSplitsOffTheBuffLineAndKeepsItsGrey()
        {
            // The cooldown IS in the API - inside
            // infix_upgrade.buff.description as
            // "<br><c=@reminder>(Cooldown: 20 Seconds)</c>" (live API,
            // 24561) - which the old raw emission would have printed as
            // literal markup in the bonus blue. The live3 sigil-rage
            // capture shows the split the markup encodes: blue effect
            // line, grey cooldown line under it (2026-08-26).
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Superior Sigil of Rage",
                ItemType = "UpgradeComponent",
                SubType = "Sigil",
                BuffDescription = "Gain quickness for 3 seconds upon critically hitting a foe. "
                    + "<br><c=@reminder>(Cooldown: 20 Seconds)</c>",
            });

            var spans = content.Lines.SelectMany(l => l.Spans).ToArray();
            var buff = spans.Single(
                s => s.Text.StartsWith("Gain quickness"));
            Assert.Equal(TooltipSpanRole.Bonus, buff.Role);

            var cooldown = spans.Single(s => s.Text == "(Cooldown: 20 Seconds)");
            Assert.Equal(TooltipSpanRole.Reminder, cooldown.Role);

            var lines = content.ToPlainLines();
            Assert.Equal(
                lines.IndexOf(lines.Single(l => l.StartsWith("Gain quickness"))) + 1,
                lines.IndexOf("(Cooldown: 20 Seconds)"));
        }

        [Fact]
        public void AConsumableWithBothAnEffectAndADescriptionTakesTheCandyCornShape()
        {
            // live3 candy-corn (36041, 2026-08-26): prompt, effect block,
            // then the item's own description CONTIGUOUS under the effect
            // block, then the shape's one blank, then "Consumable".
            var lines = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Piece of Candy Corn",
                ItemType = "Consumable",
                SubType = "Generic",
                EffectName = "Sugar Rush",
                EffectIconUrl = "https://render.example/sugar.png",
                NourishmentDurationMs = 10000,
                NourishmentDescription =
                    "Movement speed increased by 10%. Stacks duration. Causes sugar crash.",
                Description = "A sugary, delicious, versatile treat.",
            }).ToPlainLines();

            Assert.Equal(new[]
            {
                "Piece of Candy Corn",
                "Double-click to consume.",
                "Sugar Rush(10s): Movement speed increased by 10%. Stacks duration. "
                    + "Causes sugar crash.",
                "A sugary, delicious, versatile treat.",
                "",
                "Consumable",
            }, lines.ToArray());
        }

        [Fact]
        public void BuffDescriptionSurvivesWhenNoAttributeLineAlreadySaysIt()
        {
            // The other half of the de-duplication above: suppression is
            // exact-match only, so a buff that SUMMARISES several
            // attributes is its own distinct wording and still belongs.
            var stats = new ItemStatBlock
            {
                Name = "Test Upgrade",
                Attributes = new[]
                {
                    new ItemAttributeLine("Power", 5),
                    new ItemAttributeLine("Precision", 5),
                },
                BuffDescription = "+5 Power, +5 Precision",
            };

            var lines = ItemStatTooltipComposer.BuildContent(stats).ToPlainLines().ToArray();

            Assert.Contains("+5 Power", lines);
            Assert.Contains("+5 Precision", lines);
            Assert.Contains("+5 Power, +5 Precision", lines);
        }

        [Fact]
        public async Task FineFood_ShowsThePromptTheEffectBlockAndTheGamesTypeLine()
        {
            var lines = await LinesFor(RealItemJson.LotusFries);

            // The live3 food shape (soul-pastries, omnomberry,
            // 2026-08-26): "Double-click to consume." straight under the
            // header, then the effect block leading with the effect's own
            // name and its duration folded into a parenthetical - never a
            // separate "Duration:" line - and the effect block running
            // CONTIGUOUS into "Consumable" (both captures: one 16px pitch,
            // no blank). "Consumable" is the top-level type; the game
            // never shows details.type ("Food").
            Assert.Equal(new[]
            {
                "Cup of Lotus Fries",
                "Double-click to consume.",
                "Nourishment (30 m): 30% Magic Find",
                "+70 Condition Damage",
                "+10% Experience from Kills",
                "Consumable",
                "Required Level: 80",
            }, lines);
        }

        [Fact]
        public async Task TheWholeEffectBlockIsGreyAndCarriesItsIconOnTheFirstLineOnly()
        {
            // Every line of the effect block - the "Nourishment (...)"
            // lead-in included - saturates at the annotation grey
            // (170,170,170) on all three live3 effect captures
            // (soul-pastries, candy-corn, omnomberry, 2026-08-26). This
            // SUPERSEDES F7's white-first-line split, whose evidence was a
            // 2018 JPEG; the measurement wins. Never the upgrade-bonus
            // blue. The effect icon (details.icon) rides the block's first
            // line only.
            var raw = await RealItemFixtures.ParseOneAsync(RealItemJson.LotusFries);
            var content = ItemStatTooltipComposer.BuildContent(ItemStatBlockFactory.Build(raw));

            var effectLines = content.Lines
                .Where(l => l.Kind == TooltipLineKind.Effect)
                .ToArray();
            Assert.Equal(3, effectLines.Length);
            Assert.Equal(
                "https://render.guildwars2.com/file/779D3F0ABE5B46C09CFC57374DA8CC3A495F291C/436367.png",
                effectLines[0].IconUrl);
            Assert.Null(effectLines[1].IconUrl);
            Assert.Null(effectLines[2].IconUrl);

            var spans = effectLines.SelectMany(l => l.Spans).ToArray();
            Assert.All(spans, s => Assert.Equal(TooltipSpanRole.Muted, s.Role));
            Assert.DoesNotContain(
                content.Lines.SelectMany(l => l.Spans), s => s.Role == TooltipSpanRole.Bonus);
        }

        [Theory]
        [InlineData(10000, "Sugar Rush(10s): Zoom")]
        [InlineData(2700000, "Nourishment (45 m): Zoom")]
        [InlineData(5400000, "Nourishment (1 h 30 m): Zoom")]
        public void EffectDurationsAreTightForSecondsAndSpacedForMinutes(
            int durationMs, string expected)
        {
            // Seconds hug the name - "Sugar Rush(10s):" (live3 candy-corn)
            // and "Soul of the Titan(5s):" (relic-livingcity), both a 2px
            // kern where the minutes captures show a 6px space - while
            // minutes take the space on both sides of the unit:
            // "Nourishment (45 m):" (soul-pastries) and "Nourishment
            // (30 m):" (omnomberry, pixel-identical gap). The hour form
            // extends the minutes style; no capture of one exists.
            string name = durationMs < 60000 ? "Sugar Rush" : "Nourishment";
            var lines = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Treat",
                ItemType = "Consumable",
                EffectName = name,
                NourishmentDurationMs = durationMs,
                NourishmentDescription = "Zoom",
            }).ToPlainLines();

            Assert.Contains(expected, lines);
            Assert.Contains("Double-click to consume.", lines);
        }

        [Fact]
        public void ARunesBonusLinesKeepTheUpgradeBonusRole()
        {
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Superior Rune of the Scholar",
                ItemType = "UpgradeComponent",
                SubType = "Rune",
                UpgradeBonuses = new[] { "+25 Power", "+35 Ferocity" },
            });

            var bonuses = content.Lines
                .SelectMany(l => l.Spans)
                .Where(s => s.Text.StartsWith("("))
                .ToArray();

            Assert.Equal(2, bonuses.Length);
            Assert.All(bonuses, s => Assert.Equal(TooltipSpanRole.Bonus, s.Role));
        }

        [Fact]
        public void ABodyThatOpensWithTheIdentityBlockKeepsItsBlankUnderTheHeader()
        {
            // The other side of the rule above, measured on xyaren.png
            // (icon bottom y=34, first text band y=53 - one 16px pitch of
            // empty space).
            var lines = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Toymaker's Bag",
                Rarity = "Exotic",
                ItemType = "Back",
            }).ToPlainLines();

            Assert.Equal("Toymaker's Bag", lines[0]);
            Assert.Equal("", lines[1]);
            Assert.Equal("Exotic", lines[2]);
        }

        [Fact]
        public void ADurationOnlyNourishmentBlockAlsoOpensTheBodyUnderTheHeader()
        {
            // The nourishment block is one block whichever of its two lines
            // the item actually carries.
            var lines = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Timed Snack",
                NourishmentDurationMs = 1800000,
            }).ToPlainLines();

            Assert.Equal("Timed Snack", lines[0]);
            Assert.Equal("Duration: 30 m", lines[1]);
        }

        [Fact]
        public void ABonusRunStillTakesItsBlankEvenWhenNourishmentFollowsIt()
        {
            // The flag is about which block OPENS the body. FWDekker's
            // UpgradeComponent builder breaks before its buffs, so a bonus
            // run keeps the blank even when a nourishment line sits under
            // it (inferred - no unequipped-rune capture exists).
            var lines = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Odd Hybrid",
                BuffDescription = "+5% Damage",
                NourishmentDurationMs = 1800000,
            }).ToPlainLines();

            Assert.Equal("Odd Hybrid", lines[0]);
            Assert.Equal("", lines[1]);
            Assert.Equal("+5% Damage", lines[2]);
        }

        [Fact]
        public async Task AscendedFood_SaysNothingAboutAnEffectItHasNoDataFor()
        {
            var lines = await LinesFor(RealItemJson.CilantroSteak);

            Assert.Equal("Cilantro Lime Sous-Vide Steak", lines[0]);
            Assert.DoesNotContain(lines, l => l.StartsWith("Duration"));
            Assert.DoesNotContain(lines, l => l.Contains("No effect"));
            // No effect data means no consume prompt either - the prompt
            // is only measured beside a populated effect block.
            Assert.DoesNotContain("Double-click to consume.", lines);
            Assert.Contains("Account Bound", lines);
        }

        [Fact]
        public async Task SoulbindingItemShowsSoulboundAndSuppressesItsNoSellValue()
        {
            var lines = await LinesFor(RealItemJson.Rebreather);

            // Both dimensions stack, account line first, and each is
            // worded the way live3 relic-livingcity shows for this exact
            // flag pair: bare "Account Bound" over "Soulbound on Use"
            // (2026-08-26).
            Assert.Contains("Account Bound", lines);
            Assert.Contains("Soulbound on Use", lines);
            Assert.Contains("Defense: 73", lines);
            Assert.Contains("Double-click to select stats.", lines);
            Assert.Contains("Helm Aquatic Armor", lines);
            Assert.False(await HasCoinSpan(RealItemJson.Rebreather));
        }

        [Fact]
        public void UniqueSitsOnItsOwnLineAboveTheBindingLine()
        {
            var lines = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Unique Thing",
                IsUnique = true,
                Bindings = new[] { "Account Bound" },
            }).ToPlainLines();

            Assert.Equal(lines.IndexOf("Unique") + 1, lines.IndexOf("Account Bound"));
        }

        [Theory]
        [InlineData("Greatsword", "(Two-Handed)")]
        [InlineData("Focus", "(Off Hand)")]
        [InlineData("Trident", "(Aquatic)")]
        [InlineData("LargeBundle", null)]
        public void AWeaponNamesTheHandItIsHeldIn(string subType, string expected)
        {
            var lines = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Weapon",
                ItemType = "Weapon",
                SubType = subType,
            }).ToPlainLines();

            if (expected == null)
            {
                // An unknown weapon type renders no hand line rather than
                // a guessed one.
                Assert.DoesNotContain(lines, l => l.StartsWith("("));
                return;
            }

            Assert.Contains(expected, lines);
        }

        private static async Task<bool> HasCoinSpan(string itemJson)
        {
            var raw = await RealItemFixtures.ParseOneAsync(itemJson);
            return ItemStatTooltipComposer.BuildContent(ItemStatBlockFactory.Build(raw))
                .Lines.SelectMany(l => l.Spans).Any(s => s.IsCoin);
        }

        [Fact]
        public async Task NameLineCarriesTheRarityColourRoleAndTheCoinLineStaysACoinSpan()
        {
            var raw = await RealItemFixtures.ParseOneAsync(RealItemJson.ZojjasWarfists);
            var content = ItemStatTooltipComposer.BuildContent(ItemStatBlockFactory.Build(raw));

            var nameSpan = content.Lines[0].Spans.Single();
            Assert.Equal(TooltipSpanRole.Rarity, nameSpan.Role);
            Assert.Equal("Ascended", nameSpan.RarityKey);

            var coinSpan = content.Lines
                .SelectMany(l => l.Spans)
                .Single(s => s.IsCoin);
            Assert.Equal(240, coinSpan.CoinCopper);
        }

        [Fact]
        public async Task TheRarityWordCarriesTheRarityColourAndTheRestOfTheIdentityBlockIsWhite()
        {
            // Measured on the 2026-08-25 live captures: the rarity word is
            // drawn in the rarity colour, same as the name line - s07's
            // "Fine" reads (82,146,240), eq-weapon-full's "Legendary"
            // (153,51,255), both non-comparison hovers. Every other
            // identity line stays white; the description's own <c=@flavor>
            // run is the only coloured prose.
            var raw = await RealItemFixtures.ParseOneAsync(RealItemJson.ZojjasWarfists);
            var content = ItemStatTooltipComposer.BuildContent(ItemStatBlockFactory.Build(raw));

            var rarityWord = content.Lines
                .SelectMany(l => l.Spans)
                .Single(s => s.Text == "Ascended");
            Assert.Equal(TooltipSpanRole.Rarity, rarityWord.Role);
            Assert.Equal("Ascended", rarityWord.RarityKey);

            var identity = content.Lines
                .SelectMany(l => l.Spans)
                .Where(s => s.Text == "Gloves Armor" ||
                            s.Text == "Heavy" || s.Text == "Account Bound")
                .ToArray();

            Assert.Equal(3, identity.Length);
            Assert.All(identity, s => Assert.Equal(TooltipSpanRole.Default, s.Role));

            var flavor = content.Lines
                .SelectMany(l => l.Spans)
                .Single(s => s.Text.StartsWith("Crafted in the style"));
            Assert.Equal(TooltipSpanRole.Flavor, flavor.Role);
        }

        [Fact]
        public void ConsecutiveSlotsAreEachTheirOwnBlankSeparatedBlock()
        {
            // Measured on live/eq-weapon-full.png (2026-08-25): two sigil
            // blocks and two infusion lines each render blank / block /
            // blank, never one contiguous run (fidelity-audit F8).
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                ItemId = 1,
                Name = "Two-Slot Thing",
                UnusedSlots = new[] { ItemSlotKind.Infusion, ItemSlotKind.Infusion },
            });

            Assert.Equal(new[]
            {
                "Two-Slot Thing",
                "",
                "Unused Infusion Slot",
                "",
                "Unused Infusion Slot",
            }, content.ToPlainLines().ToArray());
        }

        [Fact]
        public async Task TheHandLineIsMutedGreyNotWhite()
        {
            // "(Two-Handed)" measures (160,161,162) on
            // live/eq-weapon-full.png (2026-08-25, lossless) - the game's
            // parenthetical grey, not white.
            var raw = await RealItemFixtures.ParseOneAsync(RealItemJson.Bolt);
            var content = ItemStatTooltipComposer.BuildContent(ItemStatBlockFactory.Build(raw));

            var hand = content.Lines
                .SelectMany(l => l.Spans)
                .Single(s => s.Text == "(Main Hand)");
            Assert.Equal(TooltipSpanRole.Muted, hand.Role);
        }

        [Fact]
        public void AnItemWithNoIconStillOpensWithAHeaderThatDrawsTheEmptySlotSquare()
        {
            var content = ItemStatTooltipComposer.BuildContent(
                new ItemStatBlock { ItemId = 1, Name = "Iconless Thing", IconUrl = null });

            Assert.Equal(TooltipLineKind.Header, content.Lines[0].Kind);
            Assert.Equal("", content.Lines[0].IconUrl);
        }

        [Fact]
        public void NullBlockYieldsEmptyContentSoTheSurfaceStaysHidden()
        {
            Assert.True(ItemStatTooltipComposer.BuildContent(null).IsEmpty);
        }

        [Fact]
        public void ABlockWithNothingButANameStillRendersThatName()
        {
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock { ItemId = 1, Name = "Thing" });
            Assert.Equal("Thing", content.ToPlainText());
        }

        [Fact]
        public void AnUnnamedBlockNeverRendersABlankFirstLine()
        {
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock { ItemId = 1 });
            Assert.Equal("Unknown Item", content.ToPlainText());
        }

        [Theory]
        [InlineData(1800000, "Duration: 30 m")]
        [InlineData(3600000, "Duration: 1 h")]
        [InlineData(5400000, "Duration: 1 h 30 m")]
        public void DurationsAboveAnHourReadAsHoursAndMinutes(int durationMs, string expected)
        {
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Food",
                NourishmentDurationMs = durationMs,
            });

            Assert.Contains(expected, content.ToPlainLines());
        }

        [Fact]
        public void ProfessionRestrictionsAreListedOnOneLine()
        {
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Restricted Thing",
                Restrictions = new[] { "Guardian", "Warrior" },
            });

            Assert.Contains("Restricted to: Guardian, Warrior", content.ToPlainLines());
        }

        [Fact]
        public void AnEmptyRestrictionListProducesNoLineAtAll()
        {
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Unrestricted Thing",
                Restrictions = new string[0],
            });

            Assert.DoesNotContain(content.ToPlainLines(), l => l.StartsWith("Restricted"));
        }

        [Fact]
        public void ANegativeAttributeKeepsItsOwnSignRatherThanGainingAPlus()
        {
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Odd",
                Attributes = new[] { new ItemAttributeLine("Power", -5) },
            });

            Assert.Contains("-5 Power", content.ToPlainLines());
        }

        [Fact]
        public void EverySlotLineCarriesTheGamesOwnGlyphForThatSlot()
        {
            var content = ItemStatTooltipComposer.BuildContent(new ItemStatBlock
            {
                Name = "Every Slot",
                UnusedSlots = new[]
                {
                    ItemSlotKind.Upgrade, ItemSlotKind.Infusion, ItemSlotKind.Enrichment,
                },
            });

            var slots = content.Lines
                .Where(l => l.Kind == TooltipLineKind.Slot)
                .Select(l => l.IconAssetId + " " + string.Concat(l.Spans.Select(s => s.Text)))
                .ToArray();

            Assert.Equal(
                new[]
                {
                    ItemSlotFacts.UpgradeSlotAssetId + " Unused Upgrade Slot",
                    ItemSlotFacts.InfusionSlotAssetId + " Unused Infusion Slot",
                    ItemSlotFacts.EnrichmentSlotAssetId + " Unused Enrichment Slot",
                },
                slots);
        }

        [Fact]
        public async Task AnAscendedArmorPieceShowsAnUpgradeLineAboveItsInfusionLine()
        {
            // The order the game lists them in, measured on a live
            // ascended-leggings tooltip: upgrade slot, then infusion slot.
            var raw = await RealItemFixtures.ParseOneAsync(RealItemJson.ZojjasWarfists);
            var lines = ItemStatTooltipComposer
                .BuildContent(ItemStatBlockFactory.Build(raw))
                .ToPlainLines()
                .ToArray();

            int upgrade = Array.IndexOf(lines, "Unused Upgrade Slot");
            int infusion = Array.IndexOf(lines, "Unused Infusion Slot");
            Assert.True(upgrade >= 0 && infusion > upgrade);
        }

        [Fact]
        public async Task AnItemWithNoSlotsPrintsNoSlotLineAtAll()
        {
            var raw = await RealItemFixtures.ParseOneAsync(RealItemJson.MithrilOre);
            var content = ItemStatTooltipComposer.BuildContent(ItemStatBlockFactory.Build(raw));

            Assert.DoesNotContain(content.Lines, l => l.Kind == TooltipLineKind.Slot);
            Assert.DoesNotContain(content.ToPlainLines(), l => l.Contains("Slot"));
        }
    }
}
