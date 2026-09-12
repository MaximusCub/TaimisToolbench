namespace TaimisToolbench.Services
{
    /// <summary>
    /// Which unbroken run of failed account refreshes the module is in. A
    /// run opens on the first failure after a success and stays open,
    /// keeping one id, however many further attempts fail inside it. The
    /// next successful fetch closes it, so the failure after that opens a
    /// new one with a new id.
    /// <para>
    /// It exists so the Crafting Plan tab can tell a fault the user has
    /// already been told about from a fault they have not - see
    /// <see cref="StaleDataDialogGate"/>. Nothing here measures time: a
    /// run ends when the account reads again, not when a clock says so.
    /// </para>
    /// <para>
    /// Written from the fetch paths, which resume on ThreadPool
    /// continuations, and read from the main thread, so every member takes
    /// the one lock.
    /// </para>
    /// </summary>
    internal sealed class RefreshFailureRun
    {
        /// <summary>The id that stands for "no run is open".</summary>
        public const int None = 0;

        private readonly object _lock = new object();
        private int _openRunId;
        private int _lastRunId;

        /// <summary>
        /// Opens a run if none is open, and returns the open run's id. Ids
        /// start at 1, so the return is never <see cref="None"/>.
        /// </summary>
        public int RecordFailure()
        {
            lock (_lock)
            {
                if (_openRunId == None)
                {
                    _openRunId = ++_lastRunId;
                }

                return _openRunId;
            }
        }

        /// <summary>Ends the open run, if there is one.</summary>
        public void RecordSuccess()
        {
            lock (_lock)
            {
                _openRunId = None;
            }
        }

        /// <summary>The open run's id, or <see cref="None"/>.</summary>
        public int OpenRunId
        {
            get
            {
                lock (_lock)
                {
                    return _openRunId;
                }
            }
        }
    }

    /// <summary>
    /// Whether this Generate should raise the stale-account-data dialog.
    /// One dialog per <see cref="RefreshFailureRun"/>: the first Generate
    /// that meets a run raises it, and every later Generate inside the same
    /// run does not. Without this the player got a modal on every single
    /// Generate press for as long as the GW2 API was unwell, because a
    /// failed fetch commits nothing and so leaves the snapshot exactly as
    /// stale as the press before found it.
    /// <para>
    /// The status line and the Log tab still report every occurrence. Only
    /// the interruption is deduplicated.
    /// </para>
    /// </summary>
    internal sealed class StaleDataDialogGate
    {
        private int _raisedForRunId = RefreshFailureRun.None;

        /// <summary>
        /// True once per run id, on the first call carrying it. Called from
        /// the main thread only, after a generation finishes.
        /// </summary>
        public bool ShouldRaise(int failureRunId)
        {
            if (failureRunId == RefreshFailureRun.None || failureRunId == _raisedForRunId)
            {
                return false;
            }

            _raisedForRunId = failureRunId;
            return true;
        }
    }
}
