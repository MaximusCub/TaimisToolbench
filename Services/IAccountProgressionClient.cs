using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal interface IAccountProgressionClient
    {
        /// <summary>
        /// Whether the key carries "progression". Expansion access is read
        /// without it, so a false answer still allows a partial fetch.
        /// </summary>
        bool HasProgressionPermission();

        Task<AccountProgression> GetProgressionAsync(CancellationToken ct);
    }
}
