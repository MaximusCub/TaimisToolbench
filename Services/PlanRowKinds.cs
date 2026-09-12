using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Which Crafting Steps rows are plain informational lines rather than
    /// numbered craft steps.
    /// <para>
    /// The renderer and the height math both have to agree on this, and
    /// they used to agree only by each testing the one notice row type by
    /// name. A second notice type added to one site and not the other draws
    /// a notice as a numbered craft step, or reserves the wrong height for
    /// it.
    /// </para>
    /// </summary>
    internal static class PlanRowKinds
    {
        public static bool IsCraftingStepsNotice(PlanRowType rowType)
        {
            return rowType == PlanRowType.TimegatedNotice;
        }
    }
}
