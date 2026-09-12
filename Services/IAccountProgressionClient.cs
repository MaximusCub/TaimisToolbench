using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal interface IAccountProgressionClient
    {
        /// <summary>
        /// Whether the module can read achievements and masteries, and
        /// when it cannot, why - see Models/AccountProgression.cs.
        /// Expansion access is read without "progression", so a refused
        /// answer still allows a partial fetch. Never returns
        /// <see cref="AccountProgressionAccess.NotNeeded"/>: that is the
        /// pipeline's own answer for a plan that asked nothing.
        /// </summary>
        AccountProgressionAccess ProgressionAccess();

        Task<AccountProgression> GetProgressionAsync(CancellationToken ct);
    }
}
