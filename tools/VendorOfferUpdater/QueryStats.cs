using System;
using System.Collections.Generic;

namespace VendorOfferUpdater
{
    public class QueryStats
    {
        public int TotalHttpRequests { get; set; }

        public int TotalRowsFetched { get; set; }

        public int DistinctResults { get; set; }

        public int DuplicatesDiscarded { get; set; }

        public int TruncatedPartitions { get; set; }

        public TimeSpan Elapsed { get; set; }

        public List<PartitionStats> Partitions { get; } = new List<PartitionStats>();

        public List<string> NonAlphaVendors { get; } = new List<string>();

        public bool WasInterrupted { get; set; }
    }

    public class PartitionStats
    {
        // Null for the root (unpartitioned) query - only set once a
        // partition has been split by vendor-name prefix.
        public string? Prefix { get; set; }

        public int Depth { get; set; }

        public int RowsAdded { get; set; }

        public int HttpRequests { get; set; }

        public bool WasTruncated { get; set; }
    }

    /// <summary>
    /// One part of the scrape that ended without an answer: the wiki refused
    /// it, or the transport failed, on every attempt it was given. It is not
    /// an empty result and must never be recorded as one.
    /// <para>
    /// <see cref="Condition"/> is the query that failed, so a follow-up run
    /// can re-target exactly these sections with --query instead of scraping
    /// the whole namespace again.
    /// </para>
    /// </summary>
    public class UnresolvedSection
    {
        // "partition", "title-batch" or "item-batch".
        public string Kind { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        // The vendor-name prefix this section covered, or null for a section
        // that is not prefix-partitioned (the root query, an item batch).
        public string? Prefix { get; set; }

        public string Condition { get; set; } = string.Empty;

        public string ErrorCode { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public int Attempts { get; set; }
    }

    public class SafetyLimitException : Exception
    {
        public SafetyLimitException(string message)
            : base(message)
        {
        }
    }
}
