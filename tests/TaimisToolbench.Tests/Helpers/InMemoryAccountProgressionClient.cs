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
        private readonly bool _hasPermission;

        public InMemoryAccountProgressionClient(
            AccountProgression progression, bool hasPermission = true)
        {
            _progression = progression;
            _hasPermission = hasPermission;
        }

        /// <summary>
        /// When true, GetProgressionAsync throws - a transient failure of
        /// the account endpoints, which must read downstream as "not
        /// checked" rather than "not met".
        /// </summary>
        public bool ThrowOnGet { get; set; }

        public int GetCallCount { get; private set; }

        public bool HasProgressionPermission()
        {
            return _hasPermission;
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

        public static AccountProgression WithAchievements(params int[] achievementIds)
        {
            return new AccountProgression
            {
                CompletedAchievementIds = new HashSet<int>(achievementIds),
            };
        }
    }
}
