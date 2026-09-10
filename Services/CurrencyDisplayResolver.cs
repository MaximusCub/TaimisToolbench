using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Resolves raw currency ids (CostLine/CurrencyCost) to display-ready
    /// name/icon (CurrencyAmountViewModel), preferring live
    /// CurrencyMetadataService data and falling back to the offline
    /// Gw2Constants table - the exact same chain PlanViewModelBuilder's
    /// Summary-section currency rows have always used, shared so
    /// shopping-row and recipe-tree currency costs never
    /// drift from it. Blish-free (plain C#, no Blish/Gw2Sharp types) so the
    /// mapping is directly unit-testable - see CurrencyDisplayResolverTests.
    /// The no-displayed-IDs invariant is enforced by construction here:
    /// CurrencyAmountViewModel has no id field at all, only Amount/Name/
    /// IconUrl, so a caller cannot accidentally surface a raw currency id.
    /// </summary>
    internal static class CurrencyDisplayResolver
    {
        /// <summary>
        /// Prefers the live-fetched currency name when currencyMetadata has
        /// resolved it, falling back to the offline Gw2Constants table when
        /// metadata is null/absent for this id or came back with an empty
        /// name.
        /// </summary>
        public static string ResolveName(
            int currencyId, IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata)
        {
            if (currencyMetadata != null &&
                currencyMetadata.TryGetValue(currencyId, out var meta) &&
                !string.IsNullOrEmpty(meta.Name))
            {
                return meta.Name;
            }

            return Gw2Constants.ResolveCurrencyName(currencyId);
        }

        /// <summary>
        /// Icon for a currency amount; null (never a placeholder guess)
        /// when metadata is absent for this id or has no icon URL.
        /// </summary>
        public static string ResolveIconUrl(
            int currencyId, IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata)
        {
            if (currencyMetadata != null &&
                currencyMetadata.TryGetValue(currencyId, out var meta) &&
                !string.IsNullOrEmpty(meta.IconUrl))
            {
                return meta.IconUrl;
            }

            return null;
        }

        /// <summary>
        /// The currency's own prose from /v2/currencies, or null when the
        /// session holds no metadata for this id. There is no offline
        /// fallback table for descriptions the way there is for names
        /// (Gw2Constants.ResolveCurrencyName): a description the module
        /// wrote itself would be invented data.
        /// </summary>
        public static string ResolveDescription(
            int currencyId, IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata)
        {
            if (currencyMetadata != null &&
                currencyMetadata.TryGetValue(currencyId, out var meta) &&
                !string.IsNullOrEmpty(meta.Description))
            {
                return meta.Description;
            }

            return null;
        }

        /// <summary>
        /// Resolves a full non-coin currency cost (CostLine list, already
        /// scaled to the caller's quantity) into display-ready amounts.
        /// Null/empty input yields null - never an empty-but-non-null list -
        /// so callers can use a null/Count==0 check for "does this row have a
        /// currency cost at all".
        /// <para>
        /// ownedCurrencyAmounts (optional, cosmetic only) sets each line's
        /// OwnedQuantity to min(line.Count, wallet amount) when the wallet
        /// holds any of that currency, and null - not 0 - when the caller has
        /// no wallet data or the currency is absent from the snapshot. The
        /// same pass sets RawOwnedQuantity to the real, UNCLAMPED wallet
        /// amount under the identical conditions; see that property's own doc
        /// comment for why both figures must survive to callers.
        /// Callers resolving a per-unit "Each" amount must not pass it -
        /// ownership is a total-quantity concept, and ResolveUnitAmounts
        /// never accepts it.
        /// </para>
        /// </summary>
        public static List<CurrencyAmountViewModel> ResolveAmounts(
            IReadOnlyList<CostLine> costLines,
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata,
            IReadOnlyDictionary<int, int> ownedCurrencyAmounts = null)
        {
            if (costLines == null || costLines.Count == 0)
            {
                return null;
            }

            var result = new List<CurrencyAmountViewModel>(costLines.Count);
            foreach (var line in costLines)
            {
                int? owned = null;
                int? rawOwned = null;
                if (ownedCurrencyAmounts != null &&
                    ownedCurrencyAmounts.TryGetValue(line.Id, out int ownedRaw))
                {
                    owned = Math.Min(ownedRaw, line.Count);
                    rawOwned = ownedRaw;
                }

                result.Add(new CurrencyAmountViewModel
                {
                    Amount = line.Count,
                    CurrencyId = line.Id,
                    Name = ResolveName(line.Id, currencyMetadata),
                    IconUrl = ResolveIconUrl(line.Id, currencyMetadata),
                    OwnedQuantity = owned,
                    RawOwnedQuantity = rawOwned,
                });
            }

            return result;
        }

        /// <summary>
        /// Per-unit ("Each") counterpart of ResolveAmounts for a
        /// vendor-priced currency cost: the WINNING OFFER's true per-unit
        /// rate - its own per-batch cost line divided by its own OutputCount
        /// - never a truncated average over the row's aggregated total.
        /// <para>
        /// perBatchCostLines/outputCount come from
        /// PlanStep.VendorOfferCurrencyCostLinesPerBatch/
        /// VendorOfferOutputCount, populated only when every tree occurrence
        /// merged into that step used the identical winning offer
        /// (VendorBatchSolver.FinalizeVendorBatches). Null/0 otherwise, in
        /// which case this returns null rather than guessing: gw2efficiency
        /// never shows a per-unit currency price at all. When a line's
        /// per-batch count does not divide evenly by outputCount the true
        /// rate is not a whole number, so rather than round, the amount
        /// carries a literal "N for M" bundle label (see
        /// CurrencyAmountViewModel.BundleLabel) for the caller to render as
        /// text. Derivation: docs/ARCHITECTURE.md section S1.7.
        /// </para>
        /// </summary>
        public static List<CurrencyAmountViewModel> ResolveUnitAmounts(
            int outputCount,
            IReadOnlyList<CostLine> perBatchCostLines,
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata)
        {
            return ResolveDividedAmounts(perBatchCostLines, outputCount, currencyMetadata);
        }

        /// <summary>
        /// Approximates a per-unit ("Each") currency amount for a single
        /// recipe-tree row (TreeSectionController's "Unit price:" tooltip
        /// line) from a node's own already-scaled-to-Quantity
        /// VendorCurrencyCosts. Unlike ResolveUnitAmounts this is NOT the
        /// winning offer's true per-batch rate: CraftingTreeNode carries no
        /// per-offer batch data, so this divides the node's own total by its
        /// own Quantity.
        /// <para>
        /// The two agree whenever the offer's purchase batches divided evenly
        /// into the node's Quantity (the common case). When they do not, this
        /// falls back to the same "N for M" bundle text as ResolveUnitAmounts
        /// rather than dividing into a misleading fractional number.
        /// Display-layer only: no solver change is needed to plumb the true
        /// batch rate down to this node.
        /// Derivation: docs/ARCHITECTURE.md section S1.7.
        /// </para>
        /// </summary>
        public static List<CurrencyAmountViewModel> ResolveTreeNodeUnitAmounts(
            IReadOnlyList<CostLine> totalCostLines,
            int quantity,
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata)
        {
            return ResolveDividedAmounts(totalCostLines, quantity, currencyMetadata);
        }

        /// <summary>
        /// Shared "N for M" divide-with-bundle-fallback arithmetic behind
        /// both ResolveUnitAmounts (true per-batch rate) and
        /// ResolveTreeNodeUnitAmounts (total/quantity approximation) - the
        /// two callers differ only in what costLines/divisor semantically
        /// represent, never in the math itself.
        /// </summary>
        private static List<CurrencyAmountViewModel> ResolveDividedAmounts(
            IReadOnlyList<CostLine> costLines,
            int divisor,
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata)
        {
            if (costLines == null || costLines.Count == 0 || divisor <= 0)
            {
                return null;
            }

            var result = new List<CurrencyAmountViewModel>(costLines.Count);
            foreach (var line in costLines)
            {
                bool evenly = line.Count % divisor == 0;
                result.Add(new CurrencyAmountViewModel
                {
                    Amount = evenly ? line.Count / divisor : 0,
                    BundleLabel = evenly ? null : $"{line.Count} for {divisor}",
                    UnitRate = (double)line.Count / divisor,
                    CurrencyId = line.Id,
                    Name = ResolveName(line.Id, currencyMetadata),
                    IconUrl = ResolveIconUrl(line.Id, currencyMetadata),
                });
            }

            return result;
        }
    }
}
