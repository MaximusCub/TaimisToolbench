namespace TaimisToolbench.Tests.Helpers
{
    /// <summary>
    /// Verbatim /v2/items responses, captured from the live GW2 API, for
    /// the item classes the stat-tooltip work has to survive: fixed-stat
    /// ascended armour, a stat-selectable legendary weapon, a rune, a
    /// sigil, an infusion, fine and ascended food, a detail-less crafting
    /// material, a stat-selectable exotic, a legendary whose flavour text
    /// carries markup and non-ASCII bullets, and the equipment-slot
    /// shapes below.
    /// <para>
    /// Non-ASCII bytes in the API's own text are written as \u escapes so
    /// the source stays ASCII-only (repo invariant); the RUNTIME string is
    /// byte-identical to what the API returned.
    /// </para>
    /// </summary>
    internal static class RealItemJson
    {
        public const string ZojjasWarfists =
            "{\"name\":\"Zojja's Warfists\",\"description\":\"<c=@flavor>Crafted in the style of the renowned asuran genius, Zojja.</c>\",\"type\":\"Armor\",\"level\":80,\"rarity\":\"Ascended\",\"vendor_value\":240,\"default_skin\":116,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"HideSuffix\",\"AccountBound\",\"AccountBindOnUse\"],\"restrictions\":[],\"id\":48074,\"chat_link\":\"[&AgHKuwAA]\",\"icon\":\"https://render.guildwars2.com/file/BD20599D290345BE7D98BD270FBE502CF5212654/699217.png\",\"details\":{\"type\":\"Gloves\",\"weight_class\":\"Heavy\",\"defense\":191,\"infusion_slots\":[{\"flags\":[\"Infusion\"]}],\"attribute_adjustment\":134.442,\"infix_upgrade\":{\"id\":161,\"attributes\":[{\"attribute\":\"Power\",\"modifier\":47},{\"attribute\":\"Precision\",\"modifier\":34},{\"attribute\":\"CritDamage\",\"modifier\":34}]},\"secondary_suffix_item_id\":\"\"}}";

        public const string Bolt =
            "{\"name\":\"Bolt\",\"type\":\"Weapon\",\"level\":80,\"rarity\":\"Legendary\",\"vendor_value\":100000,\"default_skin\":4684,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"HideSuffix\",\"NoSalvage\",\"NoSell\",\"AccountBindOnUse\",\"DeleteWarning\"],\"restrictions\":[],\"id\":30699,\"chat_link\":\"[&AgHrdwAA]\",\"icon\":\"https://render.guildwars2.com/file/FE47E046D10DF27508910869B5EB040F6BBBE793/456026.png\",\"details\":{\"type\":\"Sword\",\"damage_type\":\"Lightning\",\"min_power\":950,\"max_power\":1050,\"defense\":0,\"infusion_slots\":[{\"flags\":[\"Infusion\"]}],\"attribute_adjustment\":358.512,\"suffix_item_id\":24554,\"stat_choices\":[161,155,159,157,158,160,153,605,700,616,154,156,162,686,559,754,753,799,1026,1067,628,1032,1111,1109,1123,1140,1085,1153,1118,1131,1222,1344,1363,1364,1559,1556,1681,1686,1826],\"secondary_suffix_item_id\":\"\"}}";

        public const string RuneOfTheScholar =
            "{\"name\":\"Superior Rune of the Scholar\",\"description\":\"<c=@abilitytype>Element: </c>Brilliance<br>Double-click to apply to a piece of armor.\",\"type\":\"UpgradeComponent\",\"level\":60,\"rarity\":\"Exotic\",\"vendor_value\":65,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[],\"restrictions\":[],\"id\":24836,\"chat_link\":\"[&AgEEYQAA]\",\"icon\":\"https://render.guildwars2.com/file/4378ABC0415950DAC6A05C76920392D72E242EC2/220736.png\",\"details\":{\"type\":\"Rune\",\"flags\":[\"HeavyArmor\",\"LightArmor\",\"MediumArmor\"],\"infusion_upgrade_flags\":[],\"bonuses\":[\"+25 Power\",\"+35 Ferocity\",\"+50 Power\",\"+65 Ferocity\",\"+100 Power\",\"+125 Ferocity\"],\"attribute_adjustment\":0,\"infix_upgrade\":{\"id\":112,\"attributes\":[]},\"suffix\":\"of the Scholar\"}}";

        public const string SigilOfForce =
            "{\"name\":\"Superior Sigil of Force\",\"description\":\"<c=@abilitytype>Element: </c>Enhancement<br>Double-click to apply to a weapon.\",\"type\":\"UpgradeComponent\",\"level\":60,\"rarity\":\"Exotic\",\"vendor_value\":216,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[],\"restrictions\":[],\"id\":24615,\"chat_link\":\"[&AgEnYAAA]\",\"icon\":\"https://render.guildwars2.com/file/D7420E430D002E07382035EF0D0F77370C4EE6B8/220662.png\",\"details\":{\"type\":\"Sigil\",\"flags\":[\"ShortBow\",\"Dagger\",\"Focus\",\"Greatsword\",\"Hammer\",\"Harpoon\",\"Mace\",\"Pistol\",\"Rifle\",\"Scepter\",\"Shield\",\"Speargun\",\"Axe\",\"Staff\",\"Sword\",\"Torch\",\"Trident\",\"Warhorn\",\"LongBow\"],\"infusion_upgrade_flags\":[],\"attribute_adjustment\":0,\"infix_upgrade\":{\"id\":261,\"buff\":{\"skill_id\":9322,\"description\":\"+5% Damage\"},\"attributes\":[]},\"suffix\":\"of Force\"}}";

        public const string AgonyInfusion =
            "{\"name\":\"+1 Agony Infusion\",\"description\":\"Double-click to apply to an unused infusion slot. Used by artificers to craft more powerful agony infusions.\",\"type\":\"UpgradeComponent\",\"level\":0,\"rarity\":\"Ascended\",\"vendor_value\":330,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"NoSalvage\",\"NoSell\"],\"restrictions\":[],\"id\":49424,\"chat_link\":\"[&AgEQwQAA]\",\"icon\":\"https://render.guildwars2.com/file/C605E2EF280B5E4CF9A249E80AB3053843C5EBE3/511839.png\",\"details\":{\"type\":\"Default\",\"flags\":[\"ShortBow\",\"HeavyArmor\",\"LightArmor\",\"Dagger\",\"MediumArmor\",\"Focus\",\"Greatsword\",\"Hammer\",\"Trinket\",\"Harpoon\",\"Mace\",\"Pistol\",\"Rifle\",\"Scepter\",\"Shield\",\"Speargun\",\"Axe\",\"Staff\",\"Sword\",\"Torch\",\"Trident\",\"Warhorn\",\"LongBow\"],\"infusion_upgrade_flags\":[\"Infusion\"],\"attribute_adjustment\":0,\"infix_upgrade\":{\"id\":764,\"buff\":{\"skill_id\":22100,\"description\":\"+1 Agony Resistance\"},\"attributes\":[{\"attribute\":\"AgonyResistance\",\"modifier\":1}]},\"suffix\":\"\"},\"upgrades_into\":[{\"upgrade\":\"Attunement\",\"item_id\":104755},{\"upgrade\":\"Attunement\",\"item_id\":104864},{\"upgrade\":\"Attunement\",\"item_id\":104797},{\"upgrade\":\"Attunement\",\"item_id\":104706},{\"upgrade\":\"Attunement\",\"item_id\":104681},{\"upgrade\":\"Attunement\",\"item_id\":104741}]}";

        /// <summary>A sigil whose effect text carries a reminder run: the
        /// cooldown arrives inside infix_upgrade.buff.description, behind a
        /// &lt;br&gt;, and has to reach the reader as its own grey line.</summary>
        public const string SigilOfEarth =
            "{\"name\":\"Superior Sigil of Earth\",\"description\":\"<c=@abilitytype>Element: </c>Pain<br>Double-click to apply to a weapon.\",\"type\":\"UpgradeComponent\",\"level\":60,\"rarity\":\"Exotic\",\"vendor_value\":216,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[],\"restrictions\":[],\"id\":24560,\"chat_link\":\"[&AgHwXwAA]\",\"icon\":\"https://render.guildwars2.com/file/251EE3B8B5ADB8D7F7A35DBAEFABA35AEACDF51B/220677.png\",\"details\":{\"type\":\"Sigil\",\"flags\":[\"ShortBow\",\"Dagger\",\"Focus\",\"Greatsword\",\"Hammer\",\"Harpoon\",\"Mace\",\"Pistol\",\"Rifle\",\"Scepter\",\"Shield\",\"Speargun\",\"Axe\",\"Staff\",\"Sword\",\"Torch\",\"Trident\",\"Warhorn\",\"LongBow\"],\"infusion_upgrade_flags\":[],\"attribute_adjustment\":0,\"infix_upgrade\":{\"id\":203,\"buff\":{\"skill_id\":9452,\"description\":\"Inflict bleeding for 6 seconds upon critically hitting a foe. <br><c=@reminder>(Cooldown: 2 Seconds)</c>\"},\"attributes\":[]},\"suffix\":\"of Earth\"}}";

        /// <summary>A rune whose FOURTH bonus string carries the same
        /// reminder markup - the case that proves details.bonuses is
        /// sanitized rather than printed raw.</summary>
        public const string RuneOfTheWater =
            "{\"name\":\"Major Rune of the Water\",\"description\":\"<c=@abilitytype>Element: </c>Skill<br>Double-click to apply to a piece of armor.\",\"type\":\"UpgradeComponent\",\"level\":39,\"rarity\":\"Rare\",\"vendor_value\":30,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[],\"restrictions\":[],\"id\":24838,\"chat_link\":\"[&AgEGYQAA]\",\"icon\":\"https://render.guildwars2.com/file/BF001AE4ED5BD7335A49D64D2AD1090E1CA19E09/221147.png\",\"details\":{\"type\":\"Rune\",\"flags\":[\"HeavyArmor\",\"LightArmor\",\"MediumArmor\"],\"infusion_upgrade_flags\":[],\"bonuses\":[\"+15 Healing\",\"+3% Boon Duration\",\"+30 Healing\",\"Remove a condition when you are struck. <c=@reminder>(Cooldown: 30 Seconds)</c>\"],\"attribute_adjustment\":0,\"infix_upgrade\":{\"id\":112,\"attributes\":[]},\"suffix\":\"of the Water\"}}";

        /// <summary>An enrichment: infusion_upgrade_flags ["Enrichment"],
        /// a real buff description and an EMPTY attribute list, which is
        /// why display text is built from the description alone.</summary>
        public const string KarmicEnrichment =
            "{\"name\":\"Karmic Enrichment\",\"description\":\"Double-click to apply to an unused enrichment slot.\",\"type\":\"UpgradeComponent\",\"level\":0,\"rarity\":\"Fine\",\"vendor_value\":8,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"AccountBound\",\"NoMysticForge\",\"NoSalvage\",\"NoSell\",\"AccountBindOnUse\"],\"restrictions\":[],\"id\":39332,\"chat_link\":\"[&AgGkmQAA]\",\"icon\":\"https://render.guildwars2.com/file/F508A75DDFBFC87133FFC5ADA3DADFF5640A2E0C/534272.png\",\"details\":{\"type\":\"Default\",\"flags\":[\"Trinket\"],\"infusion_upgrade_flags\":[\"Enrichment\"],\"attribute_adjustment\":0,\"infix_upgrade\":{\"id\":643,\"buff\":{\"skill_id\":16978,\"description\":\"+15% Karma\"},\"attributes\":[]},\"suffix\":\"\"}}";

        public const string LotusFries =
            "{\"name\":\"Cup of Lotus Fries\",\"type\":\"Consumable\",\"level\":80,\"rarity\":\"Fine\",\"vendor_value\":33,\"game_types\":[\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"NoSell\"],\"restrictions\":[],\"id\":12472,\"chat_link\":\"[&AgG4MAAA]\",\"icon\":\"https://render.guildwars2.com/file/4120B6390F071AF9DF0D633097C00DB12C80056D/219456.png\",\"details\":{\"type\":\"Food\",\"duration_ms\":1800000,\"apply_count\":1,\"name\":\"Nourishment\",\"icon\":\"https://render.guildwars2.com/file/779D3F0ABE5B46C09CFC57374DA8CC3A495F291C/436367.png\",\"description\":\"30% Magic Find\\n+70 Condition Damage\\n+10% Experience from Kills\"}}";

        public const string CilantroSteak =
            "{\"name\":\"Cilantro Lime Sous-Vide Steak\",\"description\":\"Gourmet Feast: Double-click to serve Cilantro Lime Sous-Vide Steaks to anyone nearby. Feast stays active for 5 minutes.\",\"type\":\"Consumable\",\"level\":80,\"rarity\":\"Ascended\",\"vendor_value\":165,\"game_types\":[\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"AccountBound\",\"AccountBindOnUse\"],\"restrictions\":[],\"id\":91805,\"chat_link\":\"[&AgGdZgEA]\",\"icon\":\"https://render.guildwars2.com/file/D2C00407A3FFE06251BDE9DC13525FE167ABA3E6/2191069.png\",\"details\":{\"type\":\"Food\"}}";

        public const string MithrilOre =
            "{\"name\":\"Mithril Ore\",\"description\":\"Refine into Ingots.\",\"type\":\"CraftingMaterial\",\"level\":0,\"rarity\":\"Basic\",\"vendor_value\":7,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"NoSalvage\"],\"restrictions\":[],\"id\":19700,\"chat_link\":\"[&AgH0TAAA]\",\"icon\":\"https://render.guildwars2.com/file/E90FE803CDC205CDEB13FE03694D4D04757ACF5D/65928.png\"}";

        public const string Rebreather =
            "{\"name\":\"Rime-Rimmed Mariner's Rebreather\",\"description\":\"This deep-diving mask creates small ice crystals in the water around you.\",\"type\":\"Armor\",\"level\":80,\"rarity\":\"Exotic\",\"vendor_value\":330,\"default_skin\":9071,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"HideSuffix\",\"AccountBound\",\"NoMysticForge\",\"NoSalvage\",\"NoSell\",\"SoulBindOnUse\"],\"restrictions\":[],\"id\":68357,\"chat_link\":\"[&AgEFCwEA]\",\"icon\":\"https://render.guildwars2.com/file/07A4305C7AEDB430E023C89FC5F978CFD596F4CD/924583.png\",\"details\":{\"type\":\"HelmAquatic\",\"weight_class\":\"Light\",\"defense\":73,\"infusion_slots\":[],\"attribute_adjustment\":170.72,\"stat_choices\":[161,155,159,157,158,160,153,605,700,616,154,156,162,686,559,754,753,799,1026,1067,628,1032,1231,1232,1226,1225,1229,1224,1228,1227,1230,1379,1377,1378,1484,1539,1717,1687,1826],\"secondary_suffix_item_id\":\"\"}}";

        public const string Sunrise =
            "{\"name\":\"Sunrise\",\"description\":\"<c=@flavor>This weapon is used to craft the legendary greatsword Eternity by combining it in the Mystic Forge with:\\n\u2022 Twilight\\n\u2022 5 Piles of Crystalline Dust\\n\u2022 10 Philosopher's Stones</c>\",\"type\":\"Weapon\",\"level\":80,\"rarity\":\"Legendary\",\"vendor_value\":100000,\"default_skin\":4679,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"HideSuffix\",\"NoSalvage\",\"NoSell\",\"AccountBindOnUse\",\"DeleteWarning\"],\"restrictions\":[],\"id\":30703,\"chat_link\":\"[&AgHvdwAA]\",\"icon\":\"https://render.guildwars2.com/file/EFF16C4F19792627355DC294E6D7093F544921E7/456030.png\",\"details\":{\"type\":\"Greatsword\",\"damage_type\":\"Physical\",\"min_power\":1045,\"max_power\":1155,\"defense\":0,\"infusion_slots\":[{\"flags\":[\"Infusion\"]},{\"flags\":[\"Infusion\"]}],\"attribute_adjustment\":717.024,\"suffix_item_id\":24562,\"stat_choices\":[161,155,159,157,158,160,153,605,700,616,154,156,162,686,559,754,753,799,1026,1067,628,1032,1111,1109,1123,1140,1085,1153,1118,1131,1222,1344,1363,1364,1559,1556,1681,1686,1826],\"secondary_suffix_item_id\":\"\"}}";

        /// <summary>
        /// The A/B fidelity item: the same tooltip was captured in
        /// the module and in the live game, so its rendered lines are a
        /// direct fidelity datum rather than an inference. A Trophy whose
        /// description carries a hard paragraph break, a bullet list, and
        /// the AccountBound + AccountBindOnUse flag pair.
        /// </summary>
        public const string GiftOfTwilight =
            "{\"name\":\"Gift of Twilight\",\"description\":\"A gift used to create the legendary greatsword Twilight.\\n\\nMade by combining these items in the Mystic Forge:\\n\u2022 1 Gift of Metal\\n\u2022 1 Gift of Darkness\\n\u2022 100 Icy Runestones\\n\u2022 1 Superior Sigil of Blood\",\"type\":\"Trophy\",\"level\":0,\"rarity\":\"Legendary\",\"vendor_value\":640,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"AccountBound\",\"NoSalvage\",\"AccountBindOnUse\",\"DeleteWarning\"],\"restrictions\":[],\"id\":19648,\"chat_link\":\"[&AgHATAAA]\",\"icon\":\"https://render.guildwars2.com/file/01D07FABAE26C0E5240892B00DA7AF90AB0EA022/455828.png\"}";

        /// <summary>
        /// The three slot shapes /v2/items expresses that no other fixture
        /// here has: an ascended amulet's ENRICHMENT slot (77482), an
        /// ascended back item whose second infusion slot is already filled
        /// (37010, item_id 49428), and an exotic ring, which has an upgrade
        /// slot and no infusion slot where its ascended equivalent has the
        /// reverse (36551).
        /// </summary>
        public const string VialOfSalt =
            "{\"name\":\"Vial of Salt\",\"description\":\"<c=@flavor>It took a lot of tears to create this much salt.</c>\",\"type\":\"Trinket\",\"level\":80,\"rarity\":\"Ascended\",\"vendor_value\":660,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"HideSuffix\",\"AccountBound\",\"NotUpgradeable\",\"Unique\",\"AccountBindOnUse\"],\"restrictions\":[],\"id\":77482,\"chat_link\":\"[&AgGqLgEA]\",\"icon\":\"https://render.guildwars2.com/file/5C6AC7BB0B70C95D4DD4D07209546B6FB56A1E0D/1313083.png\",\"details\":{\"type\":\"Amulet\",\"infusion_slots\":[{\"flags\":[\"Enrichment\"]}],\"attribute_adjustment\":358.512,\"stat_choices\":[584,656,658,1119,657,1038,1097,659,690,583,585,1037,586,1035,588,1114,1128,1163,1066,1064,660,1430,1436,591,581,592,1263,1271,1265,1270,1262,1268,1264,1267,1269,1366,1367,1374,1549,1566,1691,1706,1827],\"secondary_suffix_item_id\":\"\"}}";

        public const string KossOnKossInfused =
            "{\"name\":\"Koss on Koss (Infused)\",\"description\":\"<c=@flavor>First edition. This book appears to be written in the author's own hand.</c>\",\"type\":\"Back\",\"level\":80,\"rarity\":\"Ascended\",\"vendor_value\":330,\"default_skin\":2376,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"HideSuffix\",\"AccountBound\",\"NoSell\",\"NotUpgradeable\",\"Unique\",\"AccountBindOnUse\"],\"restrictions\":[],\"id\":37010,\"chat_link\":\"[&AgGSkAAA]\",\"icon\":\"https://render.guildwars2.com/file/22D31C930DFAFC955209201535DABB6C956DD7F0/511798.png\",\"details\":{\"infusion_slots\":[{\"flags\":[\"Infusion\"]},{\"flags\":[\"Infusion\"],\"item_id\":49428}],\"attribute_adjustment\":89.628,\"infix_upgrade\":{\"id\":601,\"buff\":{\"skill_id\":15757,\"description\":\"+32 Power\\n+18 Toughness\\n+18 Vitality\\n+5 Agony Resistance\"},\"attributes\":[{\"attribute\":\"Power\",\"modifier\":63},{\"attribute\":\"Toughness\",\"modifier\":40},{\"attribute\":\"Vitality\",\"modifier\":40}]},\"secondary_suffix_item_id\":\"\"}}";

        public const string InfinityLoop =
            "{\"name\":\"Infinity Loop\",\"description\":\"<c=@flavor>A single twist in the band creates a continuous surface.</c>\",\"type\":\"Trinket\",\"level\":80,\"rarity\":\"Exotic\",\"vendor_value\":396,\"game_types\":[\"Activity\",\"Wvw\",\"Dungeon\",\"Pve\"],\"flags\":[\"HideSuffix\",\"SoulBindOnUse\"],\"restrictions\":[],\"id\":36551,\"chat_link\":\"[&AgHHjgAA]\",\"icon\":\"https://render.guildwars2.com/file/A3473FFC3353576C2EF60C5A9CC47CDA6AB562C1/63613.png\",\"details\":{\"type\":\"Ring\",\"infusion_slots\":[],\"attribute_adjustment\":256.08,\"infix_upgrade\":{\"id\":153,\"attributes\":[{\"attribute\":\"Vitality\",\"modifier\":90},{\"attribute\":\"Healing\",\"modifier\":64},{\"attribute\":\"ConditionDamage\",\"modifier\":64}]},\"secondary_suffix_item_id\":\"\"}}";

        public static string Array(params string[] items)
        {
            return "[" + string.Join(",", items) + "]";
        }
    }
}
