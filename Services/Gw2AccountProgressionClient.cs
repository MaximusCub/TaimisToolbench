using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD.Modules.Managers;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Reads the account facts a vendor requirement is checked against.
    /// <para>
    /// Three independent calls, each of which leaves its own field null on
    /// failure rather than discarding the other two. Nothing is cached: a
    /// cache of exactly this shape was deleted once already for telling a
    /// player they lacked something they had just earned.
    /// </para>
    /// </summary>
    internal class Gw2AccountProgressionClient : IAccountProgressionClient
    {
        private const Gw2Sharp.WebApi.V2.Models.TokenPermission Progression =
            Gw2Sharp.WebApi.V2.Models.TokenPermission.Progression;

        private readonly Gw2ApiManager _apiManager;

        public Gw2AccountProgressionClient(Gw2ApiManager apiManager)
        {
            _apiManager = apiManager;
        }

        /// <summary>
        /// Blish HUD builds a module's subtoken from
        /// ModuleState.UserEnabledPermissions alone, never from the
        /// manifest (Blish HUD/GameServices/Modules/Managers/
        /// Gw2ApiManager.cs, GetModuleInstance), and Gw2ApiManager.
        /// Permissions is that consented list. So a permission the
        /// manifest declares but the list omits was never requested, which
        /// is a different fix for the player than a key that refused it.
        /// </summary>
        public AccountProgressionAccess ProgressionAccess()
        {
            if (_apiManager.HasPermission(Progression))
            {
                return AccountProgressionAccess.Granted;
            }

            if (!_apiManager.Permissions.Contains(Progression))
            {
                return AccountProgressionAccess.NotConsented;
            }

            return _apiManager.HasSubtoken
                ? AccountProgressionAccess.KeyMissingScope
                : AccountProgressionAccess.SubtokenNotReady;
        }

        public async Task<AccountProgression> GetProgressionAsync(CancellationToken ct)
        {
            var progression = new AccountProgression
            {
                ExpansionAccess = await ReadExpansionAccessAsync(ct),
            };

            if (ProgressionAccess() != AccountProgressionAccess.Granted)
            {
                return progression;
            }

            progression.CompletedAchievementIds = await ReadCompletedAchievementsAsync(ct);
            progression.MasteryLevelsByMasteryId = await ReadMasteryLevelsAsync(ct);
            return progression;
        }

        private async Task<ISet<string>> ReadExpansionAccessAsync(CancellationToken ct)
        {
            try
            {
                var account = await _apiManager.Gw2ApiClient.V2.Account.GetAsync(ct);
                var access = new HashSet<string>(StringComparer.Ordinal);
                if (account?.Access != null)
                {
                    foreach (var flag in account.Access)
                    {
                        // RawValue, not the typed enum: Gw2Sharp 1.7.4 has
                        // no member for End of Dragons or anything after it,
                        // and maps an unrecognized value onto a member that
                        // names a DIFFERENT expansion.
                        string raw = flag.RawValue;
                        if (!string.IsNullOrEmpty(raw))
                        {
                            access.Add(raw);
                        }
                    }
                }

                return access;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<ISet<int>> ReadCompletedAchievementsAsync(CancellationToken ct)
        {
            try
            {
                var achievements = await _apiManager.Gw2ApiClient.V2.Account.Achievements.GetAsync(ct);
                var done = new HashSet<int>();
                foreach (var achievement in achievements)
                {
                    if (achievement != null && achievement.Done)
                    {
                        done.Add(achievement.Id);
                    }
                }

                return done;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<IReadOnlyDictionary<int, int>> ReadMasteryLevelsAsync(CancellationToken ct)
        {
            try
            {
                var masteries = await _apiManager.Gw2ApiClient.V2.Account.Masteries.GetAsync(ct);
                var levels = new Dictionary<int, int>();
                foreach (var mastery in masteries)
                {
                    if (mastery == null)
                    {
                        continue;
                    }

                    // "level" is the HIGHEST 0-indexed level the account
                    // has unlocked on this track, and the endpoint lists
                    // only tracks it has started, so a requirement naming
                    // level k is met when this value is at least k.
                    levels[mastery.Id] = mastery.Level;
                }

                return levels;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
