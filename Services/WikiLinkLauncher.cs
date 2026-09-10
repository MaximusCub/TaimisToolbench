using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What is known about a wiki link launch once it has been handed to the
    /// shell. Named for what was measured, not for what the browser did:
    /// Windows can still refuse a browser the foreground after the grant
    /// succeeds. See docs/ARCHITECTURE.md, S2.10.
    /// </summary>
    internal enum WikiLaunchOutcome
    {
        /// <summary>The page opened and the foreground grant succeeded.</summary>
        ForegroundGranted,

        /// <summary>The page opened and the foreground grant was refused.</summary>
        ForegroundRefused,

        /// <summary>Nothing opened.</summary>
        Failed,
    }

    /// <summary>
    /// A thin Process.Start wrapper for opening a wiki page, kept separate from
    /// the pure, unit-tested WikiLinkBuilder: this class is side-effecting and
    /// carries no logic beyond "launch, report what happened, and do not let a
    /// launch failure propagate into the caller's UI event handler".
    /// <para>
    /// net48/Process.Start(string) already resolves through ShellExecute
    /// (UseShellExecute defaults to true on this target framework), so a bare
    /// http(s) URL opens the OS's default browser directly. Wrapped in
    /// try/catch because ShellExecute can throw (Win32Exception); a wiki-link
    /// click must never crash the Blish HUD overlay it was clicked from.
    /// </para>
    /// <para>
    /// ShellExecuteEx BLOCKS the calling thread until the shell hands the URL
    /// off, and its one call site is a mouse-event handler dispatched from the
    /// game update loop, so the call is offloaded to Task.Run. The try/catch
    /// stays INSIDE the task - not around Task.Run itself - so a launch failure
    /// is still caught and logged. See docs/ARCHITECTURE.md, S2.10.
    /// </para>
    /// </summary>
    internal static class WikiLinkLauncher
    {
        // ASFW_ANY, the wildcard process id user32 accepts here. The module
        // cannot know which process the shell will hand the URL to, so the
        // grant has to go to every process.
        private const int AsfwAny = -1;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        /// <summary>
        /// Set once by Module.Initialize to put the outcome on screen, and
        /// cleared on Unload. Null until then, and null in tests, so every
        /// read has to tolerate it.
        /// </summary>
        internal static Action<WikiLaunchOutcome> OutcomeReported;

        /// <summary>
        /// The launch itself, swapped in tests so they can exercise Launch's
        /// real branches without starting a browser. Returns the process
        /// handle ShellExecute hands back, which may be null.
        /// </summary>
        internal static Func<string, IDisposable> Opener = url => Process.Start(url);

        public static void Open(string url)
        {
            if (!IsLaunchable(url))
            {
                return;
            }

            // The right belongs to the process, not to a thread, so the
            // Task.Run below still carries it. The call stays on this thread
            // because the grant depends on this process's foreground state at
            // the moment of the call, and this thread is running a mouse
            // handler.
            bool granted = TryGrantForegroundRight();

            Task.Run(() => Launch(url, granted));
        }

        /// <summary>
        /// Rejects everything but an https URL. Every current caller only
        /// passes a WikiLinkBuilder result (always BaseUrl-prefixed), but
        /// Process.Start(string) on net48 resolves through ShellExecute, which
        /// will happily launch a local executable, a UNC path, or a
        /// file:/custom-scheme handler. Guarding at the launch site keeps the
        /// safety property here rather than in every present and future caller.
        /// </summary>
        private static bool IsLaunchable(string url)
        {
            return !string.IsNullOrEmpty(url)
                && url.StartsWith("https://", StringComparison.Ordinal);
        }

        internal static void Launch(string url, bool granted)
        {
            try
            {
                // dispose the handle ShellExecute hands back on a successful
                // launch - discarding it undisposed leaks a process handle per
                // click in a long-running overlay. The ShellExecute path can
                // return null here; `using` tolerates that.
                using (Opener(url))
                {
                }
            }
            catch (Exception ex)
            {
                // Services/ convention (see grep across this directory): no
                // Blish_HUD.Logger dependency here - ModuleLog.Shared is
                // this module's own Blish-free logging sink, already used
                // for the same "warn and keep going" shape elsewhere (e.g.
                // MainView.RefreshNowAsync's failure branch).
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "wiki", $"Failed to open wiki link: {ex.GetType().Name} - {ex.Message}");
                Report(WikiLaunchOutcome.Failed);
                return;
            }

            Report(granted ? WikiLaunchOutcome.ForegroundGranted : WikiLaunchOutcome.ForegroundRefused);
        }

        private static void Report(WikiLaunchOutcome outcome)
        {
            // Debug, because the player can do nothing about a refusal: it
            // separates "Windows said no" from "the launch never ran".
            ModuleLog.Shared.Write(ModuleLogLevel.Debug, "wiki", $"Wiki link launch: {outcome}.");

            // Read once: Unload can null the field between a test for null
            // and the invoke. The handler runs on a thread pool thread and
            // its own failure must not escape into an unobserved task.
            var handler = OutcomeReported;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(outcome);
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "wiki", $"Wiki launch notice failed: {ex.GetType().Name} - {ex.Message}");
            }
        }

        private static bool TryGrantForegroundRight()
        {
            try
            {
                return AllowSetForegroundWindow(AsfwAny);
            }
            catch (Exception ex)
            {
                // The caller runs on the game update loop and has no catch of
                // its own, and this class must never crash the overlay a wiki
                // link was clicked from. Losing the grant only costs the
                // browser its jump to the front.
                ModuleLog.Shared.Write(ModuleLogLevel.Debug, "wiki", $"AllowSetForegroundWindow failed: {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }
    }
}
