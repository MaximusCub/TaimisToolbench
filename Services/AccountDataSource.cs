using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// One of the account-data reads a snapshot refresh makes, named rather
    /// than counted. <see cref="SnapshotFetchFailedException"/> already
    /// carried how many reads failed; a caller that wants to say WHICH part
    /// of the account went unread needs this.
    /// <para>
    /// <see cref="Characters"/> covers the character list and everything
    /// fanned out from it - every character's bags, worn equipment and
    /// crafting disciplines. Those are not separate top-level reads and the
    /// snapshot service does not tally them as such.
    /// </para>
    /// </summary>
    internal enum AccountDataSource
    {
        Wallet,
        Bank,
        SharedInventory,
        MaterialStorage,
        LegendaryArmory,
        Characters,
    }

    internal static class AccountDataSources
    {
        /// <summary>
        /// Every source, in the order a message should list them. Used when
        /// a refresh failed without naming a single read - a whole-fetch
        /// timeout, say - where the honest claim is that none of them was
        /// refreshed.
        /// </summary>
        public static readonly IReadOnlyList<AccountDataSource> All = new[]
        {
            AccountDataSource.Wallet,
            AccountDataSource.Bank,
            AccountDataSource.SharedInventory,
            AccountDataSource.MaterialStorage,
            AccountDataSource.LegendaryArmory,
            AccountDataSource.Characters,
        };

        /// <summary>
        /// What a player calls this part of their account. Lower case: every
        /// call site drops it mid-sentence or into a comma list.
        /// </summary>
        public static string Label(AccountDataSource source)
        {
            switch (source)
            {
                case AccountDataSource.Wallet:
                    return "wallet";
                case AccountDataSource.Bank:
                    return "bank";
                case AccountDataSource.SharedInventory:
                    return "shared inventory";
                case AccountDataSource.MaterialStorage:
                    return "material storage";
                case AccountDataSource.LegendaryArmory:
                    return "legendary armory";
                case AccountDataSource.Characters:
                    return "characters";
                default:
                    return "account data";
            }
        }

        /// <summary>
        /// Which source a <see cref="Models.SnapshotItemEntry.Source"/>
        /// string came from, or null for a source string this build does not
        /// recognise. The vocabulary is AccountItemIndex's; the two
        /// character-scoped keys carry a name after the prefix and both fold
        /// into <see cref="AccountDataSource.Characters"/>.
        /// </summary>
        public static AccountDataSource? ForItemSource(string itemSource)
        {
            if (string.IsNullOrEmpty(itemSource))
            {
                return null;
            }

            if (itemSource.StartsWith(AccountItemIndex.CharacterSourcePrefix, StringComparison.Ordinal) ||
                itemSource.StartsWith(AccountItemIndex.CharacterEquipmentSourcePrefix, StringComparison.Ordinal))
            {
                return AccountDataSource.Characters;
            }

            if (string.Equals(itemSource, AccountItemIndex.SourceBank, StringComparison.Ordinal))
            {
                return AccountDataSource.Bank;
            }

            if (string.Equals(itemSource, AccountItemIndex.SourceSharedInventory, StringComparison.Ordinal))
            {
                return AccountDataSource.SharedInventory;
            }

            if (string.Equals(itemSource, AccountItemIndex.SourceMaterialStorage, StringComparison.Ordinal))
            {
                return AccountDataSource.MaterialStorage;
            }

            if (string.Equals(itemSource, AccountItemIndex.SourceLegendaryArmory, StringComparison.Ordinal))
            {
                return AccountDataSource.LegendaryArmory;
            }

            return null;
        }
    }
}
