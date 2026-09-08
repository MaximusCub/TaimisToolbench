using System.Collections.Generic;

namespace TaimisToolbench.Models
{
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
