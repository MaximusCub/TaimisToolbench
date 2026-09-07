using System;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// How long a whole account-snapshot fetch is allowed to take, given
    /// how many characters the account has.
    /// <para>
    /// A flat budget is a cliff: the account-wide calls cost the same for
    /// everyone, but the per-character work grows with the roster, so a
    /// large account runs out of allowance on work a small one never does.
    /// Measured on a 14-character account before the fan-out landed: a
    /// successful fetch took 35-55s of a flat 60s. The floor keeps the
    /// old allowance for small accounts, and the ceiling stops a very
    /// large roster from parking a refresh for minutes.
    /// </para>
    /// </summary>
    internal static class SnapshotFetchBudget
    {
        public static readonly TimeSpan Floor = TimeSpan.FromSeconds(60);

        public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(180);

        private const int BaseSeconds = 30;

        private const int SecondsPerCharacter = 3;

        public static TimeSpan For(int characterCount)
        {
            if (characterCount < 0)
            {
                characterCount = 0;
            }

            double seconds = BaseSeconds + (SecondsPerCharacter * (double)characterCount);
            if (seconds <= Floor.TotalSeconds)
            {
                return Floor;
            }

            if (seconds >= Ceiling.TotalSeconds)
            {
                return Ceiling;
            }

            return TimeSpan.FromSeconds(seconds);
        }
    }
}
