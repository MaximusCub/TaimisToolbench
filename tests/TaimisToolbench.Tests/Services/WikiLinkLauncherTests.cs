using System;
using System.Collections.Generic;
using System.Threading;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Drives the real WikiLinkLauncher with its Opener seam pointed at a
    /// recorder, so the launcher's own branches run without starting a
    /// browser. The Win32 grant inside Open is not stubbed and not asserted
    /// on: what AllowSetForegroundWindow returns depends on the test
    /// runner's foreground state, which is why Launch takes the result as a
    /// parameter instead of reading it.
    /// </summary>
    public class WikiLinkLauncherTests : IDisposable
    {
        private readonly Func<string, IDisposable> _originalOpener;
        private readonly Action<WikiLaunchOutcome> _originalReporter;
        private readonly List<string> _opened = new List<string>();
        private readonly List<WikiLaunchOutcome> _reported = new List<WikiLaunchOutcome>();

        public WikiLinkLauncherTests()
        {
            _originalOpener = WikiLinkLauncher.Opener;
            _originalReporter = WikiLinkLauncher.OutcomeReported;

            WikiLinkLauncher.Opener = url =>
            {
                lock (_opened)
                {
                    _opened.Add(url);
                }

                return null;
            };
            WikiLinkLauncher.OutcomeReported = outcome =>
            {
                lock (_reported)
                {
                    _reported.Add(outcome);
                }
            };
        }

        public void Dispose()
        {
            WikiLinkLauncher.Opener = _originalOpener;
            WikiLinkLauncher.OutcomeReported = _originalReporter;
        }

        private int OpenedCount
        {
            get
            {
                lock (_opened)
                {
                    return _opened.Count;
                }
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("http://wiki.guildwars2.com/wiki/Bolt_of_Damask")]
        [InlineData("file:///C:/Windows/System32/calc.exe")]
        [InlineData("C:\\Windows\\System32\\calc.exe")]
        [InlineData("\\\\server\\share\\thing.exe")]
        [InlineData("steam://run/1284210")]
        public void Open_NotAnHttpsUrl_NeverReachesTheLaunch(string url)
        {
            WikiLinkLauncher.Open(url);

            // Synchronous: Open returns before starting its task when the
            // guard rejects, so there is nothing to wait for.
            Assert.Equal(0, OpenedCount);
            Assert.Empty(_reported);
        }

        [Fact]
        public void Open_HttpsUrl_LaunchesIt()
        {
            WikiLinkLauncher.Open("https://wiki.guildwars2.com/wiki/Bolt_of_Damask");

            Assert.True(WaitForOpen(), "the launch task did not run within the timeout");
            lock (_opened)
            {
                Assert.Equal("https://wiki.guildwars2.com/wiki/Bolt_of_Damask", Assert.Single(_opened));
            }
        }

        [Fact]
        public void Launch_WithoutTheForegroundGrant_ReportsForegroundRefused()
        {
            WikiLinkLauncher.Launch("https://wiki.guildwars2.com/wiki/Mystic_Coin", granted: false);

            Assert.Equal(WikiLaunchOutcome.ForegroundRefused, Assert.Single(_reported));
        }

        [Fact]
        public void Launch_WithTheForegroundGrant_ReportsForegroundGranted()
        {
            WikiLinkLauncher.Launch("https://wiki.guildwars2.com/wiki/Mystic_Coin", granted: true);

            Assert.Equal(WikiLaunchOutcome.ForegroundGranted, Assert.Single(_reported));
        }

        [Fact]
        public void Launch_OpenerThrows_ReportsFailedAndSwallowsTheException()
        {
            WikiLinkLauncher.Opener = _ => throw new System.ComponentModel.Win32Exception(1155, "No application is associated with this file.");

            WikiLinkLauncher.Launch("https://wiki.guildwars2.com/wiki/Mystic_Coin", granted: false);

            Assert.Equal(WikiLaunchOutcome.Failed, Assert.Single(_reported));
        }

        [Fact]
        public void Launch_ReporterThrows_DoesNotEscapeIntoTheLaunchTask()
        {
            WikiLinkLauncher.OutcomeReported = _ => throw new InvalidOperationException("notification surface is gone");

            // The real Report path runs on a ThreadPool thread with nothing
            // above it to catch: an escape here is an unobserved exception,
            // not a test failure the caller would ever see.
            WikiLinkLauncher.Launch("https://wiki.guildwars2.com/wiki/Mystic_Coin", granted: false);
        }

        [Fact]
        public void Launch_NoReporterAttached_StillLaunches()
        {
            WikiLinkLauncher.OutcomeReported = null;

            WikiLinkLauncher.Launch("https://wiki.guildwars2.com/wiki/Mystic_Coin", granted: false);

            Assert.Equal(1, OpenedCount);
        }

        private bool WaitForOpen()
        {
            for (int i = 0; i < 200; i++)
            {
                if (OpenedCount > 0)
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }
    }
}
