using System;
using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // Pain seen in game: at CHARACTER SELECT, Blish has not yet
    // resolved the game's Mumble identity, so every account data source
    // call fails with an invalid/missing API key - the Snapshot tab's
    // Refresh Now used to show only "Refresh Failed - {time}" with no hint
    // why. These tests exercise the classification decision logic directly
    // (see SnapshotFailureClassifier's own class doc comment for why it
    // matches by plain exception TYPE NAME strings rather than real
    // Gw2Sharp "is" checks: this keeps the classifier itself, and these
    // tests, completely free of the Gw2Sharp/Blish HUD references the repo
    // invariant forbids in tests).
    public class SnapshotFailureClassifierTests
    {
        // ---- ApiAccessNotReady (highest priority) ----
        [Fact]
        public void Classify_AllSourcesInvalidAccessToken_ReturnsApiAccessNotReady()
        {
            var typeNames = new List<string> { "InvalidAccessTokenException", "InvalidAccessTokenException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.ApiAccessNotReady, result.Kind);
            Assert.Equal(5, result.FailedSourceCount);
            Assert.Equal(5, result.TotalSourceCount);
        }

        [Fact]
        public void Classify_AuthorizationRequired_ReturnsApiAccessNotReady()
        {
            var typeNames = new List<string> { "AuthorizationRequiredException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 1, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.ApiAccessNotReady, result.Kind);
        }

        [Fact]
        public void Classify_MissingScopes_ReturnsApiAccessNotReady()
        {
            var typeNames = new List<string> { "MissingScopesException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 1, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.ApiAccessNotReady, result.Kind);
        }

        [Fact]
        public void Classify_OneTokenFailureAmongOtherwiseSuccessfulSources_StillReturnsApiAccessNotReady()
        {
            // A broken token affects every call made with it, so even a
            // single token-classified failure among partial results takes
            // priority over PartialFailure - the actionable fix (the three
            // checks) is the same regardless of what else also failed.
            var typeNames = new List<string> { "InvalidAccessTokenException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 1, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.ApiAccessNotReady, result.Kind);
        }

        [Fact]
        public void Classify_TokenFailureMixedWithNetworkFailure_ReturnsApiAccessNotReady()
        {
            var typeNames = new List<string> { "InvalidAccessTokenException", "TimeoutException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 2, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.ApiAccessNotReady, result.Kind);
        }

        // ---- NetworkOrApiDown ----
        [Fact]
        public void Classify_AllSourcesTimeout_ReturnsNetworkOrApiDown()
        {
            var typeNames = new List<string> { "TimeoutException", "TimeoutException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.NetworkOrApiDown, result.Kind);
        }

        [Fact]
        public void Classify_ServiceUnavailable_ReturnsNetworkOrApiDown()
        {
            var typeNames = new List<string> { "ServiceUnavailableException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.NetworkOrApiDown, result.Kind);
        }

        [Fact]
        public void Classify_ServerError_ReturnsNetworkOrApiDown()
        {
            var typeNames = new List<string> { "ServerErrorException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.NetworkOrApiDown, result.Kind);
        }

        [Fact]
        public void Classify_TooManyRequests_ReturnsNetworkOrApiDown()
        {
            var typeNames = new List<string> { "TooManyRequestsException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.NetworkOrApiDown, result.Kind);
        }

        [Fact]
        public void Classify_HttpRequestException_ReturnsNetworkOrApiDown()
        {
            var typeNames = new List<string> { "HttpRequestException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.NetworkOrApiDown, result.Kind);
        }

        [Fact]
        public void Classify_RequestCanceled_ReturnsNetworkOrApiDown()
        {
            var typeNames = new List<string> { "RequestCanceledException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.NetworkOrApiDown, result.Kind);
        }

        // ---- PartialFailure ----
        [Fact]
        public void Classify_SomeSourcesFailedWithNonTokenCause_ReturnsPartialFailure()
        {
            var typeNames = new List<string> { "TimeoutException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 1, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.PartialFailure, result.Kind);
            Assert.Equal(1, result.FailedSourceCount);
            Assert.Equal(5, result.TotalSourceCount);
        }

        [Fact]
        public void Classify_SomeSourcesFailedWithUnknownCause_ReturnsPartialFailure()
        {
            var typeNames = new List<string> { "SomeOtherException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 2, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.PartialFailure, result.Kind);
        }

        // ---- Unknown ----
        [Fact]
        public void Classify_TotalFailureWithUnrecognizedType_ReturnsUnknown()
        {
            var typeNames = new List<string> { "SomeOtherException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.Unknown, result.Kind);
        }

        [Fact]
        public void Classify_EmptyTypeNames_ReturnsUnknown()
        {
            var result = SnapshotFailureClassifier.Classify(new List<string>(), failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.Unknown, result.Kind);
        }

        [Fact]
        public void Classify_NullTypeNames_ReturnsUnknown()
        {
            var result = SnapshotFailureClassifier.Classify((IReadOnlyList<string>)null, failedSourceCount: 5, totalSourceCount: 5);

            Assert.Equal(SnapshotFailureKind.Unknown, result.Kind);
        }

        [Fact]
        public void Classify_ZeroTotalSourceCount_NeverReturnsPartialFailure()
        {
            // Guards the totalSourceCount > 0 condition in the priority
            // check - a caller passing 0/0 (no known counts) must not spuriously
            // divide into a "some failed" state.
            var typeNames = new List<string> { "SomeOtherException" };

            var result = SnapshotFailureClassifier.Classify(typeNames, failedSourceCount: 0, totalSourceCount: 0);

            Assert.Equal(SnapshotFailureKind.Unknown, result.Kind);
        }

        // ---- Characters the fetch could not read in full. They are not
        // account-wide sources, so they classify on their own only when
        // every source answered. A failed source outranks them: it is the
        // bigger fact and usually the same cause.
        [Fact]
        public void EverySourceReadButACharacterNot_ClassifiesAsIncompleteCharacters()
        {
            var result = SnapshotFailureClassifier.Classify(
                new List<string>(), failedSourceCount: 0, totalSourceCount: 6, incompleteCharacterCount: 2);

            Assert.Equal(SnapshotFailureKind.IncompleteCharacters, result.Kind);
            Assert.Equal(2, result.IncompleteCharacterCount);
        }

        [Fact]
        public void AFailedSourceOutranksAnIncompleteCharacter()
        {
            var typeNames = new List<string> { "ServerErrorException" };

            var result = SnapshotFailureClassifier.Classify(
                typeNames, failedSourceCount: 2, totalSourceCount: 6, incompleteCharacterCount: 1);

            Assert.Equal(SnapshotFailureKind.PartialFailure, result.Kind);
            Assert.Equal(1, result.IncompleteCharacterCount);
        }

        [Fact]
        public void ClassifyException_ReadsTheIncompleteCharactersOffTheFetchFailure()
        {
            var ex = new SnapshotFetchFailedException(
                failedSourceCount: 0,
                totalSourceCount: 6,
                failedSourceExceptionTypeNames: null,
                incompleteCharacterNames: new[] { "Taimi" });

            var result = SnapshotFailureClassifier.Classify((Exception)ex);

            Assert.Equal(SnapshotFailureKind.IncompleteCharacters, result.Kind);
            Assert.Equal(1, result.IncompleteCharacterCount);
        }

        // ---- Classify(Exception) overload ----
        [Fact]
        public void ClassifyException_SnapshotFetchFailedException_UsesItsPerSourceDetail()
        {
            var ex = new SnapshotFetchFailedException(
                failedSourceCount: 5,
                totalSourceCount: 5,
                failedSourceExceptionTypeNames: new[] { "InvalidAccessTokenException" });

            var result = SnapshotFailureClassifier.Classify((Exception)ex);

            Assert.Equal(SnapshotFailureKind.ApiAccessNotReady, result.Kind);
            Assert.Equal(5, result.FailedSourceCount);
            Assert.Equal(5, result.TotalSourceCount);
        }

        [Fact]
        public void ClassifyException_SnapshotFetchFailedException_PartialFailureNoTokenCause_ReturnsPartialFailure()
        {
            var ex = new SnapshotFetchFailedException(
                failedSourceCount: 2,
                totalSourceCount: 5,
                failedSourceExceptionTypeNames: new[] { "TimeoutException", "ServerErrorException" });

            var result = SnapshotFailureClassifier.Classify((Exception)ex);

            Assert.Equal(SnapshotFailureKind.PartialFailure, result.Kind);
            Assert.Equal(2, result.FailedSourceCount);
            Assert.Equal(5, result.TotalSourceCount);
        }

        [Fact]
        public void ClassifyException_BareTimeoutException_ReturnsNetworkOrApiDown()
        {
            // Mirrors Module.FetchAndSaveSnapshotAsync's own internal fetch
            // timeout, which throws a plain TimeoutException that never
            // passes through Gw2AccountSnapshotService/
            // SnapshotFetchFailedException at all.
            var result = SnapshotFailureClassifier.Classify((Exception)new TimeoutException("timed out"));

            Assert.Equal(SnapshotFailureKind.NetworkOrApiDown, result.Kind);
            Assert.Equal(0, result.FailedSourceCount);
            Assert.Equal(0, result.TotalSourceCount);
        }

        [Fact]
        public void ClassifyException_UnrelatedException_ReturnsUnknown()
        {
            var result = SnapshotFailureClassifier.Classify((Exception)new InvalidOperationException("boom"));

            Assert.Equal(SnapshotFailureKind.Unknown, result.Kind);
        }

        [Fact]
        public void ClassifyException_Null_ReturnsUnknown()
        {
            var result = SnapshotFailureClassifier.Classify((Exception)null);

            Assert.Equal(SnapshotFailureKind.Unknown, result.Kind);
        }
    }
}
