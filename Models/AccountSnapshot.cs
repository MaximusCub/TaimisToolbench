using System;
using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    internal class AccountSnapshot
    {
        // When the last holding was read, not when the fetch started. The
        // two differ by the whole fetch, which the snapshot budget allows
        // up to a minute (see Module.SnapshotFetchTimeout).
        public DateTime CapturedAt { get; set; }

        // How many characters this snapshot reached, and how many of those
        // it could not read in full. A character counts incomplete when its
        // bags, its equipment or its disciplines failed to fetch; its
        // holdings are then missing, so the plan can tell the user to buy
        // something they own. Both default to 0, which a snapshot.json
        // written before these fields existed loads as "nothing known to be
        // missing" - the same claim those older builds already made.
        public int CharacterCount { get; set; }

        public int IncompleteCharacterCount { get; set; }

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
