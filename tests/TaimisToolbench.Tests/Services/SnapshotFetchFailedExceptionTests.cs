using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // KNOWN-ISSUES #31/api-degradation F1: Gw2AccountSnapshotService itself is
    // Blish/Gw2Sharp-coupled (constructed from Blish_HUD.Modules.Managers.
    // Gw2ApiManager) and cannot be exercised here per the repo's "tests
    // must never reference Blish HUD/Gw2Sharp" invariant - there is no fake
    // seam to build without violating that rule. This exercises the pure,
    // Blish-free piece that carries the fix's actual decision logic: the
    // exception FetchSnapshotAsync now throws on ANY source failure
    // (partial or total) instead of returning a holed/empty snapshot.
    //
    // The resulting non-persistence behavior itself is verified by
    // construction, not by a test double: Module.FetchAndSaveSnapshotAsync
    // only reaches its _currentSnapshot/_snapshotStore.Save commit lines
    // AFTER `await _snapshotService.FetchSnapshotAsync(ct)` returns
    // successfully - since FetchSnapshotAsync now throws before its own
    // `return snapshot;` on any failure, those commit lines are
    // structurally unreachable on a failed fetch, with no seam needed to
    // prove it.
    public class SnapshotFetchFailedExceptionTests
    {
        [Fact]
        public void PartialFailure_MessageReportsFailedAndTotalCount()
        {
            var ex = new SnapshotFetchFailedException(failedSourceCount: 2, totalSourceCount: 5);

            Assert.Equal(2, ex.FailedSourceCount);
            Assert.Equal(5, ex.TotalSourceCount);
            Assert.Equal("2 of 5 account data sources failed.", ex.Message);
        }

        [Fact]
        public void TotalFailure_MessageReportsAllSourcesFailed()
        {
            var ex = new SnapshotFetchFailedException(failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal("All account data sources failed.", ex.Message);
        }

        // ---- FailedSourceExceptionTypeNames (SnapshotFailureClassifier's
        // input - pain seen in game) ----
        [Fact]
        public void TwoArgConstructor_FailedSourceExceptionTypeNames_IsEmptyNotNull()
        {
            var ex = new SnapshotFetchFailedException(failedSourceCount: 2, totalSourceCount: 5);

            Assert.NotNull(ex.FailedSourceExceptionTypeNames);
            Assert.Empty(ex.FailedSourceExceptionTypeNames);
        }

        [Fact]
        public void ThreeArgConstructor_CapturesFailedSourceExceptionTypeNames()
        {
            var ex = new SnapshotFetchFailedException(
                failedSourceCount: 2,
                totalSourceCount: 5,
                failedSourceExceptionTypeNames: new[] { "InvalidAccessTokenException", "TimeoutException" });

            Assert.Equal(new[] { "InvalidAccessTokenException", "TimeoutException" }, ex.FailedSourceExceptionTypeNames);
        }

        [Fact]
        public void ThreeArgConstructor_NullTypeNames_IsEmptyNotNull()
        {
            var ex = new SnapshotFetchFailedException(failedSourceCount: 2, totalSourceCount: 5, failedSourceExceptionTypeNames: null);

            Assert.NotNull(ex.FailedSourceExceptionTypeNames);
            Assert.Empty(ex.FailedSourceExceptionTypeNames);
        }

        // ---- Characters the fetch could not read in full. Pain seen in
        // game on 2026-09-07: one character's equipment fetch failed and
        // the snapshot committed 1020 items instead of 1039. A character is
        // not an account-wide source, so the message must not report it as
        // one. Names are safe to show; ids are not.
        [Fact]
        public void IncompleteCharactersOnly_MessageNamesThemAndNoSources()
        {
            var ex = new SnapshotFetchFailedException(
                failedSourceCount: 0,
                totalSourceCount: 6,
                failedSourceExceptionTypeNames: null,
                incompleteCharacterNames: new[] { "Taimi" });

            Assert.Equal("1 character could not be read in full: Taimi.", ex.Message);
            Assert.Equal(new[] { "Taimi" }, ex.IncompleteCharacterNames);
        }

        [Fact]
        public void SeveralIncompleteCharacters_ArePluralisedAndListed()
        {
            var ex = new SnapshotFetchFailedException(
                failedSourceCount: 0,
                totalSourceCount: 6,
                failedSourceExceptionTypeNames: null,
                incompleteCharacterNames: new[] { "Taimi", "Braham" });

            Assert.Equal("2 characters could not be read in full: Taimi, Braham.", ex.Message);
        }

        [Fact]
        public void FailedSourcesAndIncompleteCharacters_AreReportedAsTwoSeparateFacts()
        {
            var ex = new SnapshotFetchFailedException(
                failedSourceCount: 2,
                totalSourceCount: 6,
                failedSourceExceptionTypeNames: new[] { "ServerErrorException" },
                incompleteCharacterNames: new[] { "Taimi" });

            Assert.Equal(
                "2 of 6 account data sources failed. 1 character could not be read in full: Taimi.",
                ex.Message);
        }

        [Fact]
        public void EarlierConstructors_ReportNoIncompleteCharacters()
        {
            var ex = new SnapshotFetchFailedException(failedSourceCount: 2, totalSourceCount: 5);

            Assert.NotNull(ex.IncompleteCharacterNames);
            Assert.Empty(ex.IncompleteCharacterNames);
        }
    }
}
