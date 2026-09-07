using System;
using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    internal class AccountSnapshot
    {
        public DateTime CapturedAt { get; set; }

        public int CoinCopper { get; set; }

        public List<SnapshotItemEntry> Items { get; set; } = new List<SnapshotItemEntry>();

        public List<SnapshotWalletEntry> Wallet { get; set; } = new List<SnapshotWalletEntry>();

        // Per-character learned crafting disciplines. Deliberately NOT
        // defaulted to an empty list like Items/Wallet: null means "no
        // discipline data was ever captured" (old snapshot.json, degraded
        // fetch), distinct from "captured and empty". Consumers rely on
        // the distinction to never fabricate a "not trained" claim for a
        // snapshot that never looked.
        public List<SnapshotCharacterDiscipline> CharacterDisciplines { get; set; }

        // Which characters are wearing which Legendary Armory item. Names
        // only - see SnapshotArmoryEquip for why nothing here may be
        // counted. Defaulted to an empty list like Items/Wallet: a
        // snapshot.json written before this field existed loads to "nobody
        // named", which prints no wearers rather than a wrong set.
        public List<SnapshotArmoryEquip> LegendaryArmoryEquipped { get; set; }
            = new List<SnapshotArmoryEquip>();
    }
}
