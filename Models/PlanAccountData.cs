using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// The account data one plan generation solves against, handed to
    /// CraftingPlanPipeline as a task rather than a value.
    /// <para>
    /// A Generate Plan click refreshes the account first. Resolving that
    /// refresh before the pipeline starts would add its whole duration to
    /// the generation; the pipeline instead awaits it after the price
    /// fetch, which is the last step before anything reads the account.
    /// </para>
    /// </summary>
    internal class PlanAccountData
    {
        /// <summary>
        /// What the plan subtracts from its ingredients. Null when the user
        /// turned Use Own Materials off, which is the same null the
        /// pipeline's snapshot parameter already means.
        /// </summary>
        public AccountSnapshot Snapshot { get; set; }

        /// <summary>
        /// Threaded separately from <see cref="Snapshot"/> for the reason
        /// the pipeline's own parameter is: the plan reports required
        /// disciplines whether or not it uses the account's holdings.
        /// </summary>
        public IReadOnlyList<SnapshotCharacterDiscipline> CharacterDisciplines { get; set; }
    }
}
