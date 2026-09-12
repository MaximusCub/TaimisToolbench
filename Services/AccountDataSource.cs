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
        /// Which read a <see cref="Models.SnapshotItemEntry.Source"/>
        /// string came from, or null for a source string this build does not
        /// recognise. The vocabulary is AccountItemIndex's. A socket key
        /// answers for the container it names, because a rune in a banked
        /// helm was read by the bank call, not the character call.
        /// </summary>
        public static AccountDataSource? ForItemSource(string itemSource)
        {
            if (string.IsNullOrEmpty(itemSource))
            {
                return null;
            }

            if (AccountItemIndex.CharacterNameOffset(itemSource) >= 0)
            {
                return AccountDataSource.Characters;
            }

            if (AccountItemIndex.ContainerIs(itemSource, AccountItemIndex.SourceBank))
            {
                return AccountDataSource.Bank;
            }

            if (AccountItemIndex.ContainerIs(itemSource, AccountItemIndex.SourceSharedInventory))
            {
                return AccountDataSource.SharedInventory;
            }

            if (AccountItemIndex.ContainerIs(itemSource, AccountItemIndex.SourceMaterialStorage))
            {
                return AccountDataSource.MaterialStorage;
            }

            if (AccountItemIndex.ContainerIs(itemSource, AccountItemIndex.SourceLegendaryArmory))
            {
                return AccountDataSource.LegendaryArmory;
            }

            return null;
        }
    }
}
