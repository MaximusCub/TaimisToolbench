using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Why the pair exists: a failed account refresh commits nothing, so the
    /// snapshot stays exactly as old as the Generate press before found it,
    /// and the freshness guard never suppressed the retry. Every press
    /// during one outage therefore raised the same modal again.
    /// <para>
    /// The label and the dialog are view code and are not covered here.
    /// These are the two decisions behind them.
    /// </para>
    /// </summary>
    public class RefreshFailureRunTests
    {
        [Fact]
        public void NoFailureYet_NoRunIsOpen()
        {
            Assert.Equal(RefreshFailureRun.None, new RefreshFailureRun().OpenRunId);
        }

        [Fact]
        public void FailuresInOneOutage_ShareOneRunId()
        {
            var runs = new RefreshFailureRun();

            int first = runs.RecordFailure();
            int second = runs.RecordFailure();
            int third = runs.RecordFailure();

            Assert.NotEqual(RefreshFailureRun.None, first);
            Assert.Equal(first, second);
            Assert.Equal(first, third);
            Assert.Equal(first, runs.OpenRunId);
        }

        [Fact]
        public void ASuccessfulReadEndsTheRun_AndTheNextFailureIsANewOne()
        {
            var runs = new RefreshFailureRun();

            int outage = runs.RecordFailure();
            runs.RecordSuccess();

            Assert.Equal(RefreshFailureRun.None, runs.OpenRunId);
            Assert.NotEqual(outage, runs.RecordFailure());
        }

        [Fact]
        public void SuccessWithNoRunOpen_ChangesNothing()
        {
            var runs = new RefreshFailureRun();

            runs.RecordSuccess();
            runs.RecordSuccess();

            Assert.Equal(RefreshFailureRun.None, runs.OpenRunId);
        }

        [Fact]
        public void SecondGenerateInTheSameOutage_RaisesNoSecondDialog()
        {
            var runs = new RefreshFailureRun();
            var gate = new StaleDataDialogGate();

            Assert.True(gate.ShouldRaise(runs.RecordFailure()));
            Assert.False(gate.ShouldRaise(runs.RecordFailure()));
            Assert.False(gate.ShouldRaise(runs.RecordFailure()));
        }

        [Fact]
        public void AGenuinelyNewFailure_RaisesADialogAgain()
        {
            var runs = new RefreshFailureRun();
            var gate = new StaleDataDialogGate();

            Assert.True(gate.ShouldRaise(runs.RecordFailure()));
            Assert.False(gate.ShouldRaise(runs.RecordFailure()));

            // The account read again, then broke again.
            runs.RecordSuccess();

            Assert.True(gate.ShouldRaise(runs.RecordFailure()));
        }

        [Fact]
        public void ARunNobodyHasBeenToldAbout_StillRaises()
        {
            // A background refresh opens the run without any dialog, so the
            // first Generate to meet it is the first time the user could
            // have heard about it.
            var runs = new RefreshFailureRun();
            var gate = new StaleDataDialogGate();

            runs.RecordFailure();
            runs.RecordFailure();

            Assert.True(gate.ShouldRaise(runs.OpenRunId));
        }

        [Fact]
        public void ASuccessfulGenerate_RaisesNothing()
        {
            Assert.False(new StaleDataDialogGate().ShouldRaise(RefreshFailureRun.None));
        }

        [Fact]
        public void RunIdsAreNeverReused()
        {
            var runs = new RefreshFailureRun();
            var seen = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < 5; i++)
            {
                Assert.True(seen.Add(runs.RecordFailure()));
                runs.RecordSuccess();
            }
        }
    }
}
