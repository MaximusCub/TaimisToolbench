using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Tests.Helpers
{
    internal class InMemoryAccountProgressionClient : IAccountProgressionClient
    {
        private readonly AccountProgression _progression;
        private readonly AccountProgressionAccess _access;

        public InMemoryAccountProgressionClient(
            AccountProgression progression,
            AccountProgressionAccess access = AccountProgressionAccess.Granted)
        {
            _progression = progression;
            _access = access;
        }

        /// <summary>
        /// When true, GetProgressionAsync throws - a transient failure of
        /// the account endpoints, which must read downstream as "not
        /// checked" rather than "not met".
        /// </summary>
        public bool ThrowOnGet { get; set; }

        public int GetCallCount { get; private set; }

        public AccountProgressionAccess ProgressionAccess()
        {
            return _access;
        }

        public Task<AccountProgression> GetProgressionAsync(CancellationToken ct)
        {
            GetCallCount++;

            if (ThrowOnGet)
            {
                throw new InvalidOperationException("account progression unavailable");
            }

            return Task.FromResult(_progression);
        }

        /// <summary>
        /// What the real client returns when the subtoken does not carry
        /// "progression": expansion access read off /v2/account, and null
        /// for everything that scope gates.
        /// </summary>
        public static AccountProgression WithoutProgressionScope()
        {
            return new AccountProgression
            {
                ExpansionAccess = new HashSet<string>(),
            };
        }

        public static AccountProgression WithAchievements(params int[] achievementIds)
        {
            return new AccountProgression
            {
                CompletedAchievementIds = new HashSet<int>(achievementIds),
            };
        }
    }
}
