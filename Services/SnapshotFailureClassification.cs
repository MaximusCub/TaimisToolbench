namespace TaimisToolbench.Services
{
    /// <summary>
    /// Result of SnapshotFailureClassifier.Classify: the coarse
    /// SnapshotFailureKind plus the raw source counts a caller needs to
    /// render a specific message (e.g. "2 of 5 sources") without re-parsing
    /// the original exception itself.
    /// </summary>
    internal class SnapshotFailureClassification
    {
        public SnapshotFailureKind Kind { get; }

        public int FailedSourceCount { get; }

        public int TotalSourceCount { get; }

        /// <summary>
        /// How many characters could not be read in full, which is a
        /// separate fault from a failed account-wide source.
        /// </summary>
        public int IncompleteCharacterCount { get; }

        public SnapshotFailureClassification(SnapshotFailureKind kind, int failedSourceCount, int totalSourceCount)
            : this(kind, failedSourceCount, totalSourceCount, 0)
        {
        }

        public SnapshotFailureClassification(
            SnapshotFailureKind kind, int failedSourceCount, int totalSourceCount, int incompleteCharacterCount)
        {
            Kind = kind;
            FailedSourceCount = failedSourceCount;
            TotalSourceCount = totalSourceCount;
            IncompleteCharacterCount = incompleteCharacterCount;
        }
    }
}
