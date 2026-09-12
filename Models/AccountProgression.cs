using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// Whether the module could read the account's achievements and
    /// masteries, and when it could not, why. Each answer maps to one
    /// action the player can take, which is the whole reason the cases are
    /// distinguished: a Blish HUD consent toggle and a Guild Wars 2 API key
    /// are different things in different places.
    /// </summary>
    internal enum AccountProgressionAccess
    {
        /// <summary>No vendor in this plan gates anything, so nothing was
        /// asked for.</summary>
        NotNeeded,

        /// <summary>The module's subtoken carries "progression".</summary>
        Granted,

        /// <summary>
        /// The permission is declared in manifest.json but is not in the
        /// module's consented list, so Blish HUD never requested it. Blish
        /// builds the subtoken from ModuleState.UserEnabledPermissions
        /// alone (Blish HUD/GameServices/Modules/Managers/Gw2ApiManager.cs,
        /// GetModuleInstance), and a newly declared optional permission is
        /// not added to a module's saved state. The player ticks it in the
        /// module's own permission panel, which Blish only makes editable
        /// while the module is disabled.
        /// </summary>
        NotConsented,

        /// <summary>
        /// Consented, and a subtoken arrived without it: the account's own
        /// API key does not grant progression, so the subtoken request
        /// could not either.
        /// </summary>
        KeyMissingScope,

        /// <summary>Consented, and no subtoken has arrived yet.</summary>
        SubtokenNotReady,

        /// <summary>The request itself failed.</summary>
        FetchFailed,
    }

    /// <summary>
    /// What the account has unlocked, as far as a vendor requirement needs
    /// to know.
    /// <para>
    /// Each field is null when that question was not answered, and answered
    /// separately from the others: expansion access comes from /v2/account
    /// on the "account" scope the module already requires, while
    /// achievements and masteries need "progression". So an account that
    /// declines the new scope still gets its expansion gates checked. Null
    /// never means "has none" - see VendorRequirementStatus.Unknown.
    /// </para>
    /// </summary>
    internal class AccountProgression
    {
        public ISet<int> CompletedAchievementIds { get; set; }

        /// <summary>
        /// Mastery track id to the highest level index reached on it. A
        /// track the account has not started is absent.
        /// </summary>
        public IReadOnlyDictionary<int, int> MasteryLevelsByMasteryId { get; set; }

        /// <summary>
        /// The raw strings /v2/account reports in its "access" array.
        /// </summary>
        public ISet<string> ExpansionAccess { get; set; }
    }
}
