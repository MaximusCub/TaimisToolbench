using System;
using System.IO;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class StatusStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly StatusStore _store;

        public StatusStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TaimisToolbench_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _store = new StatusStore(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
            }
        }

        [Fact]
        public void Load_NoFile_ReturnsEmpty()
        {
            Assert.Equal("", _store.Load());
        }

        [Fact]
        public void Save_Null_Load_ReturnsEmpty()
        {
            _store.Save(null);
            Assert.Equal("", _store.Load());
        }

        [Fact]
        public void Save_Load_RoundTrips()
        {
            _store.Save("Updated - 1:00 PM");
            Assert.Equal("Updated - 1:00 PM", _store.Load());
        }

        [Fact]
        public void Save_Overwrite_ReturnsLatest()
        {
            _store.Save("First");
            _store.Save("Second");
            Assert.Equal("Second", _store.Load());
        }

        // --- One-store convention: atomic .tmp+Replace, matching
        // SnapshotStore/PlanStore/VendorOfferStore - previously the .tmp
        // was written and then File.Copy'd over the target, which rewrites
        // it in place and can leave a partial status.txt. Mirrors
        // SnapshotStoreTests' pair so both the create (File.Move) and the
        // overwrite (File.Replace) branch are covered. ---
        [Fact]
        public void Save_LeavesNoTmpFileBehind()
        {
            _store.Save("First");

            Assert.False(File.Exists(Path.Combine(_tempDir, "status.txt.tmp")));
            Assert.Equal("First", _store.Load());
        }

        [Fact]
        public void Save_Overwrite_LeavesNoTmpFileBehindEither()
        {
            _store.Save("First");
            _store.Save("Second");

            Assert.False(File.Exists(Path.Combine(_tempDir, "status.txt.tmp")));
            Assert.Equal("Second", _store.Load());
        }

        // --- onError callback: real IO failure. ---
        [Fact]
        public void Save_DirectoryCreationFails_InvokesOnErrorInsteadOfThrowing()
        {
            string blockingPath = Path.Combine(_tempDir, "blocked-data-dir");
            File.WriteAllText(blockingPath, "not a directory");

            string capturedMessage = null;
            Exception capturedException = null;
            var store = new StatusStore(blockingPath, (message, ex) =>
            {
                capturedMessage = message;
                capturedException = ex;
            });

            store.Save("some status");

            Assert.NotNull(capturedMessage);
            Assert.NotNull(capturedException);
        }

        [Fact]
        public void Save_NoOnErrorProvided_DoesNotThrowOnFailure()
        {
            string blockingPath = Path.Combine(_tempDir, "blocked-data-dir-2");
            File.WriteAllText(blockingPath, "not a directory");

            var store = new StatusStore(blockingPath);

            store.Save("some status"); // no-op onError default - must not throw
        }
    }
}
