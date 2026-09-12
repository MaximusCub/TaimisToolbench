using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using VendorOfferUpdater.Models;

namespace VendorOfferUpdater
{
    /// <summary>
    /// Answers the one question a reviewer of a `data(vendor):` commit cannot
    /// otherwise answer: what actually changed. It re-pairs added and removed
    /// rows by (merchant, output item), which the offerId hash does not
    /// preserve but a human reads instantly, and reports them as repricings
    /// with the old and new cost side by side; only rows with no counterpart
    /// are reported as genuine additions or removals.
    /// <para>
    /// SeasonalFestival and the vendor requirement sit outside the hash, so a
    /// change to either keeps the offerId and is reported as a retag. A row whose id
    /// is shared but whose content is not - a hand-edit, or a row predating a
    /// hash-format change - is reported as a repricing rather than trusted on
    /// its id alone. The converse, a row whose content is unchanged but whose
    /// id is not, is counted as rehashed rather than listed: a
    /// VendorOfferHasher format change does that to every row at once. Why no
    /// cheaper answer works: docs/ARCHITECTURE.md section T.3.
    /// </para>
    /// </summary>
    internal static class VendorOfferDiff
    {
        /// <summary>Cap on rows listed per section. Counts are always exact.</summary>
        internal const int MaxListedPerSection = 50;

        internal sealed class Result
        {
            public int OldCount { get; set; }

            public int NewCount { get; set; }

            public List<VendorOffer> Added { get; } = new List<VendorOffer>();

            public List<VendorOffer> Removed { get; } = new List<VendorOffer>();

            public List<OfferChange> Repriced { get; } = new List<OfferChange>();

            public List<OfferChange> Retagged { get; } = new List<OfferChange>();

            /// <summary>
            /// Rows that kept their content but changed OfferId. Not an offer
            /// change, so deliberately outside <see cref="IsEmpty"/>: it is the
            /// count that explains why a 14.8MB file moved when nothing a
            /// reviewer cares about did.
            /// </summary>
            public int Rehashed { get; set; }

            public bool IsEmpty =>
                Added.Count == 0
                && Removed.Count == 0
                && Repriced.Count == 0
                && Retagged.Count == 0;
        }

        internal sealed class OfferChange
        {
            public OfferChange(VendorOffer before, VendorOffer after)
            {
                Before = before;
                After = after;
            }

            public VendorOffer Before { get; }

            public VendorOffer After { get; }
        }

        private readonly struct PairKey : IEquatable<PairKey>
        {
            public PairKey(VendorOffer offer)
            {
                Merchant = offer.MerchantName ?? string.Empty;
                OutputItemId = offer.OutputItemId;
            }

            public string Merchant { get; }

            public int OutputItemId { get; }

            public bool Equals(PairKey other) =>
                OutputItemId == other.OutputItemId
                && string.Equals(Merchant, other.Merchant, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is PairKey other && Equals(other);

            public override int GetHashCode() =>
                (StringComparer.Ordinal.GetHashCode(Merchant) * 397) ^ OutputItemId;
        }

        internal static Result Compute(
            IReadOnlyList<VendorOffer>? before,
            IReadOnlyList<VendorOffer>? after)
        {
            before ??= Array.Empty<VendorOffer>();
            after ??= Array.Empty<VendorOffer>();

            var result = new Result
            {
                OldCount = before.Count,
                NewCount = after.Count,
            };

            var beforeById = IndexById(before);
            var afterById = IndexById(after);

            // A shared offerId is only EVIDENCE that the content matched, not
            // proof: the id is a hash the tool computes, and a hand-edited row
            // (or one predating a hash-format change) can carry an id that no
            // longer describes it. Compare the content anyway - reporting such
            // a row as unchanged would make this report lie in exactly the case
            // a reviewer most needs it not to.
            foreach (var kvp in beforeById.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!afterById.TryGetValue(kvp.Key, out var afterOffer))
                {
                    continue;
                }

                if (!string.Equals(
                        Program.ComputeContentKey(kvp.Value),
                        Program.ComputeContentKey(afterOffer),
                        StringComparison.Ordinal))
                {
                    result.Repriced.Add(new OfferChange(kvp.Value, afterOffer));
                }
                else if (!string.Equals(TagKey(kvp.Value), TagKey(afterOffer), StringComparison.Ordinal))
                {
                    result.Retagged.Add(new OfferChange(kvp.Value, afterOffer));
                }
            }

            var vanished = before
                .Where(o => o.OfferId == null || !afterById.ContainsKey(o.OfferId))
                .ToList();
            var appeared = after
                .Where(o => o.OfferId == null || !beforeById.ContainsKey(o.OfferId))
                .ToList();

            var vanishedByPair = GroupByPair(vanished);
            var appearedByPair = GroupByPair(appeared);

            foreach (var pair in AllPairs(vanishedByPair, appearedByPair))
            {
                vanishedByPair.TryGetValue(pair, out var gone);
                appearedByPair.TryGetValue(pair, out var came);
                gone ??= new List<VendorOffer>();
                came ??= new List<VendorOffer>();

                // Match content-identical rows off first. A VendorOfferHasher
                // hash-format change gives every row in the dataset a new
                // OfferId at once, so without this every row lands here and the
                // positional pairing below reports the whole dataset as
                // repriced, printing tens of thousands of "10x item 36041 ->
                // 10x item 36041" lines. It also stops rows of one
                // (merchant, item) pair that differ only in OutputCount from
                // being cross-paired into price moves that never happened.
                MatchUnchangedByContent(gone, came, result);

                // Whatever is left is a genuine change, but where a merchant
                // sells the same item on several such rows the pairing between
                // them is arbitrary. Pair positionally and let the surplus fall
                // through as real additions/removals: the counts stay exact
                // either way, and the reviewer still sees every changed row's
                // before and after.
                int paired = Math.Min(gone.Count, came.Count);
                for (int i = 0; i < paired; i++)
                {
                    result.Repriced.Add(new OfferChange(gone[i], came[i]));
                }

                result.Removed.AddRange(gone.Skip(paired));
                result.Added.AddRange(came.Skip(paired));
            }

            return result;
        }

        internal static string Format(Result result, string beforeLabel, string afterLabel)
        {
            var sb = new StringBuilder();
            int delta = result.NewCount - result.OldCount;

            sb.Append("=== Vendor offer diff: ").Append(beforeLabel)
              .Append(" -> ").Append(afterLabel).AppendLine(" ===");
            sb.Append("  offers:   ").Append(result.OldCount.ToString("N0", CultureInfo.InvariantCulture))
              .Append(" -> ").Append(result.NewCount.ToString("N0", CultureInfo.InvariantCulture))
              .Append(" (").Append(delta >= 0 ? "+" : "-")
              .Append(Math.Abs(delta).ToString("N0", CultureInfo.InvariantCulture))
              .AppendLine(")");
            sb.Append("  added:    ").AppendLine(result.Added.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append("  removed:  ").AppendLine(result.Removed.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append("  repriced: ").AppendLine(result.Repriced.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append("  retagged: ").AppendLine(result.Retagged.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append("  rehashed: ").AppendLine(result.Rehashed.ToString(CultureInfo.InvariantCulture));

            if (result.IsEmpty)
            {
                sb.AppendLine();
                sb.AppendLine(result.Rehashed > 0
                    ? "  No offer changed. The rehashed rows kept their content and took a new"
                      + " offerId, so the file moved without a data change."
                    : "  No offer changed. The datasets are equivalent.");
                return sb.ToString();
            }

            AppendSection(sb, "Added", result.Added, Describe);
            AppendSection(sb, "Removed", result.Removed, Describe);
            AppendSection(sb, "Repriced", result.Repriced, DescribeReprice);
            AppendSection(sb, "Retagged", result.Retagged, DescribeRetag);

            return sb.ToString();
        }

        private static void AppendSection<T>(
            StringBuilder sb, string title, List<T> rows, Func<T, string> describe)
        {
            if (rows.Count == 0)
            {
                return;
            }

            sb.AppendLine();
            sb.Append("--- ").Append(title).Append(" (")
              .Append(rows.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(") ---");

            foreach (var row in rows.Take(MaxListedPerSection))
            {
                sb.Append("  ").AppendLine(describe(row));
            }

            if (rows.Count > MaxListedPerSection)
            {
                sb.Append("  ... and ")
                  .Append((rows.Count - MaxListedPerSection).ToString(CultureInfo.InvariantCulture))
                  .AppendLine(" more");
            }
        }

        internal static string Describe(VendorOffer offer)
        {
            return $"{offer.MerchantName ?? "(no merchant)"} | item {offer.OutputItemId}"
                 + $" x{offer.OutputCount} | {DescribeCost(offer)}";
        }

        private static string DescribeReprice(OfferChange change)
        {
            return $"{change.After.MerchantName ?? "(no merchant)"} | item {change.After.OutputItemId}"
                 + $" x{change.After.OutputCount} | {DescribeCost(change.Before)}"
                 + $" -> {DescribeCost(change.After)}";
        }

        private static string DescribeRetag(OfferChange change)
        {
            return $"{change.After.MerchantName ?? "(no merchant)"} | item {change.After.OutputItemId}"
                 + $" | {TagKey(change.Before)} -> {TagKey(change.After)}";
        }

        /// <summary>
        /// The fields outside both the OfferId hash and the content key, as
        /// one string. A change to any of them keeps the OfferId, so without
        /// this the diff would report nothing at all.
        /// </summary>
        private static string TagKey(VendorOffer offer)
        {
            var requirement = offer.Requirement;
            return $"festival {offer.SeasonalFestival ?? "(none)"}"
                 + $", requirement {requirement?.Text ?? "(none)"}"
                 + $", achievement {Optional(requirement?.AchievementId)}"
                 + $", mastery {Optional(requirement?.MasteryId)}"
                 + $"/{Optional(requirement?.MasteryLevel)}"
                 + $", expansion {requirement?.Expansion ?? "(none)"}";
        }

        private static string Optional(int? value)
        {
            return value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "(none)";
        }

        private static string DescribeCost(VendorOffer offer)
        {
            var parts = new List<string>();

            foreach (var line in (offer.CostLines ?? new List<CostLine>())
                .OrderBy(c => c.Type, StringComparer.Ordinal)
                .ThenBy(c => c.Id))
            {
                parts.Add($"{line.Count}x {line.Type?.ToLowerInvariant() ?? "?"} {line.Id}");
            }

            if (offer.DailyCap.HasValue)
            {
                parts.Add($"daily cap {offer.DailyCap.Value}");
            }

            if (offer.WeeklyCap.HasValue)
            {
                parts.Add($"weekly cap {offer.WeeklyCap.Value}");
            }

            if (offer.SeasonalCap.HasValue)
            {
                parts.Add($"seasonal cap {offer.SeasonalCap.Value}");
            }

            if (offer.HomesteadTier.HasValue)
            {
                parts.Add($"homestead tier {offer.HomesteadTier.Value}");
            }

            return parts.Count == 0 ? "free" : string.Join(", ", parts);
        }

        private static Dictionary<string, VendorOffer> IndexById(IReadOnlyList<VendorOffer> offers)
        {
            var byId = new Dictionary<string, VendorOffer>(StringComparer.Ordinal);
            foreach (var offer in offers)
            {
                if (offer.OfferId != null)
                {
                    byId[offer.OfferId] = offer;
                }
            }

            return byId;
        }

        /// <summary>
        /// Removes from <paramref name="gone"/> and <paramref name="came"/>
        /// every row that has a counterpart with the identical content key,
        /// counting each as rehashed and recording a retag where only a field
        /// outside both the hash and the content key differs (see TagKey).
        /// Matching is first-come within a content key,
        /// which is unambiguous because rows sharing one are interchangeable.
        /// </summary>
        private static void MatchUnchangedByContent(
            List<VendorOffer> gone, List<VendorOffer> came, Result result)
        {
            if (gone.Count == 0 || came.Count == 0)
            {
                return;
            }

            var cameByContent = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
            for (int i = 0; i < came.Count; i++)
            {
                string key = Program.ComputeContentKey(came[i]);
                if (!cameByContent.TryGetValue(key, out var queue))
                {
                    queue = new Queue<int>();
                    cameByContent[key] = queue;
                }

                queue.Enqueue(i);
            }

            var goneMatched = new bool[gone.Count];
            var cameMatched = new bool[came.Count];

            for (int i = 0; i < gone.Count; i++)
            {
                string key = Program.ComputeContentKey(gone[i]);
                if (!cameByContent.TryGetValue(key, out var queue) || queue.Count == 0)
                {
                    continue;
                }

                int j = queue.Dequeue();
                goneMatched[i] = true;
                cameMatched[j] = true;
                result.Rehashed++;

                if (!string.Equals(TagKey(gone[i]), TagKey(came[j]), StringComparison.Ordinal))
                {
                    result.Retagged.Add(new OfferChange(gone[i], came[j]));
                }
            }

            RemoveMatched(gone, goneMatched);
            RemoveMatched(came, cameMatched);
        }

        private static void RemoveMatched(List<VendorOffer> rows, bool[] matched)
        {
            int write = 0;
            for (int read = 0; read < rows.Count; read++)
            {
                if (!matched[read])
                {
                    rows[write++] = rows[read];
                }
            }

            rows.RemoveRange(write, rows.Count - write);
        }

        private static Dictionary<PairKey, List<VendorOffer>> GroupByPair(
            IEnumerable<VendorOffer> offers)
        {
            var grouped = new Dictionary<PairKey, List<VendorOffer>>();
            foreach (var offer in offers)
            {
                var key = new PairKey(offer);
                if (!grouped.TryGetValue(key, out var list))
                {
                    list = new List<VendorOffer>();
                    grouped[key] = list;
                }

                list.Add(offer);
            }

            return grouped;
        }

        // Sorted so the same two datasets always produce the same report,
        // whatever order Dictionary happens to enumerate in.
        private static IEnumerable<PairKey> AllPairs(
            Dictionary<PairKey, List<VendorOffer>> a,
            Dictionary<PairKey, List<VendorOffer>> b)
        {
            return a.Keys.Concat(b.Keys)
                .Distinct()
                .OrderBy(k => k.Merchant, StringComparer.Ordinal)
                .ThenBy(k => k.OutputItemId);
        }
    }
}
