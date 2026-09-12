using System;
using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// User-provided coin valuations for the two kinds of non-coin thing a
    /// vendor takes - non-coin wallet CURRENCIES and untradeable barter
    /// ITEMS - plus which of each the user has explicitly CLEARED of the
    /// curated defaults. Precedence per id: a user-set value wins; else a
    /// cleared id has no value at all (a deliberate, persisted
    /// suppression); else the curated default applies.
    /// <para>
    /// TryGetEffectiveCopperValue/TryGetEffectiveItemCopperValue express that
    /// precedence but are NOT called by the solver at runtime - WithDefaults
    /// materializes it into plain dictionaries first. TryGetCopperValue and
    /// TryGetItemCopperValue stay raw user-override lookups.
    /// </para>
    /// <para>
    /// Two tables, not one: a GW2 currency id and a GW2 item id are
    /// different id spaces that collide numerically. The GW2 API defines no
    /// exchange rate for either kind and the solver never invents one, so an
    /// id with no effective value stays fallback-tier only.
    /// </para>
    /// </summary>
    internal class CurrencyValuation
    {
        /// <summary>
        /// No user-provided valuations or clears. "None" describes zero
        /// PERSISTED overrides/clears, not zero effective values -
        /// TryGetEffectiveCopperValue on this instance still falls through
        /// to the curated defaults; use the raw TryGetCopperValue for
        /// "truly nothing valued".
        /// </summary>
        public static readonly CurrencyValuation None = new CurrencyValuation(new Dictionary<int, long>());

        private readonly IReadOnlyDictionary<int, long> _copperPerUnit;
        private readonly HashSet<int> _clearedCurrencyIds;
        private readonly IReadOnlyDictionary<int, long> _itemCopperPerUnit;
        private readonly HashSet<int> _clearedItemIds;

        public CurrencyValuation(
            IReadOnlyDictionary<int, long> copperPerUnit,
            IEnumerable<int> clearedCurrencyIds = null,
            IReadOnlyDictionary<int, long> itemCopperPerUnit = null,
            IEnumerable<int> clearedItemIds = null)
        {
            var validated = new Dictionary<int, long>();
            if (copperPerUnit != null)
            {
                // Defensively copied: instances are stored long-term on
                // PlanSolveContext, so a caller mutating the dictionary it
                // passed in must never retroactively change an already-built
                // valuation. Validated while copying: an invalid entry here
                // would either be inert (<=0 copper never beats a coin
                // option) or nonsensical (coin priced in terms of itself),
                // so callers must fix the input rather than have it
                // silently accepted.
                foreach (var kvp in copperPerUnit)
                {
                    if (kvp.Key == Gw2Constants.CoinCurrencyId)
                    {
                        throw new ArgumentException(
                            "Currency valuation cannot be keyed on the coin currency id.",
                            nameof(copperPerUnit));
                    }

                    if (kvp.Value <= 0)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(copperPerUnit),
                            kvp.Value,
                            $"Currency {kvp.Key} must have a positive copper-per-unit valuation.");
                    }

                    validated[kvp.Key] = kvp.Value;
                }
            }

            _copperPerUnit = validated;

            var validatedCleared = new HashSet<int>();
            if (clearedCurrencyIds != null)
            {
                // Same fail-loud-for-direct-construction posture as the
                // values dictionary above (mirrors HomesteadEfficiencyTiers'
                // own constructor/ModuleSettings-clamp split - see that
                // class's doc comment): a caller passing a currency id that
                // is BOTH explicitly valued and marked cleared is
                // self-contradictory input, not something this constructor
                // should silently resolve one way or the other. Callers
                // that build both sets from a shared, possibly-overlapping
                // source (CurrencyValuationSerializer.Deserialize,
                // SettingsTabContent.SaveValuations) resolve the conflict
                // themselves (explicit value always wins) before ever
                // reaching here.
                foreach (int currencyId in clearedCurrencyIds)
                {
                    if (currencyId == Gw2Constants.CoinCurrencyId)
                    {
                        throw new ArgumentException(
                            "Currency valuation cannot clear the coin currency id.",
                            nameof(clearedCurrencyIds));
                    }

                    if (validated.ContainsKey(currencyId))
                    {
                        throw new ArgumentException(
                            $"Currency {currencyId} cannot be both explicitly valued and cleared.",
                            nameof(clearedCurrencyIds));
                    }

                    validatedCleared.Add(currencyId);
                }
            }

            _clearedCurrencyIds = validatedCleared;

            // Same defensive-copy and fail-loud posture as the two currency
            // sets above, minus the coin guard: coin is a wallet currency
            // id, and no item id names it, so there is nothing here for
            // that guard to catch.
            var validatedItems = new Dictionary<int, long>();
            if (itemCopperPerUnit != null)
            {
                foreach (var kvp in itemCopperPerUnit)
                {
                    if (kvp.Value <= 0)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(itemCopperPerUnit),
                            kvp.Value,
                            $"Item {kvp.Key} must have a positive copper-per-unit valuation.");
                    }

                    validatedItems[kvp.Key] = kvp.Value;
                }
            }

            _itemCopperPerUnit = validatedItems;

            var validatedClearedItems = new HashSet<int>();
            if (clearedItemIds != null)
            {
                foreach (int itemId in clearedItemIds)
                {
                    if (validatedItems.ContainsKey(itemId))
                    {
                        throw new ArgumentException(
                            $"Item {itemId} cannot be both explicitly valued and cleared.",
                            nameof(clearedItemIds));
                    }

                    validatedClearedItems.Add(itemId);
                }
            }

            _clearedItemIds = validatedClearedItems;
        }

        /// <summary>CurrencyId -> copper value of a single unit of that currency (user overrides only).</summary>
        public IReadOnlyDictionary<int, long> CopperPerUnit => _copperPerUnit;

        /// <summary>
        /// Currency ids the user has explicitly cleared of
        /// CurrencyDecisionDefaults' curated default - deliberately
        /// suppressed, not merely "no override set" (see class doc comment).
        /// </summary>
        public IReadOnlyCollection<int> ClearedCurrencyIds => _clearedCurrencyIds;

        /// <summary>ItemId -> copper value of a single unit of that barter item (user overrides only).</summary>
        public IReadOnlyDictionary<int, long> ItemCopperPerUnit => _itemCopperPerUnit;

        /// <summary>
        /// Item ids the user has explicitly cleared of
        /// BarterItemDecisionDefaults' curated default - the item-side twin
        /// of ClearedCurrencyIds.
        /// </summary>
        public IReadOnlyCollection<int> ClearedItemIds => _clearedItemIds;

        /// <summary>
        /// Looks up the user-provided copper value of one unit of
        /// <paramref name="currencyId"/>. Returns false when the user has
        /// not set an explicit override for that currency - this is a RAW
        /// lookup that never consults CurrencyDecisionDefaults; see
        /// TryGetEffectiveCopperValue for the solver's own value, which
        /// does.
        /// </summary>
        public bool TryGetCopperValue(int currencyId, out long copperPerUnit)
        {
            return _copperPerUnit.TryGetValue(currencyId, out copperPerUnit);
        }

        /// <summary>
        /// True when the user has explicitly cleared
        /// <paramref name="currencyId"/> of its curated default.
        /// </summary>
        public bool IsCleared(int currencyId)
        {
            return _clearedCurrencyIds.Contains(currencyId);
        }

        /// <summary>
        /// The item-side twin of TryGetCopperValue: a RAW lookup of the
        /// user-provided copper value of one unit of barter item
        /// <paramref name="itemId"/>, never consulting
        /// BarterItemDecisionDefaults.
        /// </summary>
        public bool TryGetItemCopperValue(int itemId, out long copperPerUnit)
        {
            return _itemCopperPerUnit.TryGetValue(itemId, out copperPerUnit);
        }

        /// <summary>
        /// True when the user has explicitly cleared
        /// <paramref name="itemId"/> of its curated default.
        /// </summary>
        public bool IsItemCleared(int itemId)
        {
            return _clearedItemIds.Contains(itemId);
        }

        /// <summary>
        /// Resolves <paramref name="currencyId"/>'s EFFECTIVE
        /// decision-only copper value per the three-state precedence
        /// documented on this class. No solver comparison calls it at
        /// runtime: WithDefaults materializes the precedence into a plain
        /// dictionary before the solver runs. Trap for a future caller:
        /// handing Solve a raw, non-materialized CurrencyValuation
        /// silently yields zero curated defaults.
        /// RankerReadinessCalculator.ScoreMaterials is the other production
        /// caller, and goes through here rather than reading CopperPerUnit
        /// so it gets the curated defaults whichever instance it is given.
        /// Strictly DECISION-ONLY.
        /// </summary>
        public bool TryGetEffectiveCopperValue(int currencyId, out long copperPerUnit)
        {
            if (TryGetCopperValue(currencyId, out copperPerUnit))
            {
                return true;
            }

            if (_clearedCurrencyIds.Contains(currencyId))
            {
                copperPerUnit = 0;
                return false;
            }

            return CurrencyDecisionDefaults.TryGetDefault(currencyId, out copperPerUnit);
        }

        /// <summary>
        /// The item-side twin of TryGetEffectiveCopperValue, with the same
        /// three-state precedence and the same "materialized by WithDefaults
        /// before the solver runs" contract. Strictly DECISION-ONLY.
        /// </summary>
        public bool TryGetEffectiveItemCopperValue(int itemId, out long copperPerUnit)
        {
            if (TryGetItemCopperValue(itemId, out copperPerUnit))
            {
                return true;
            }

            if (_clearedItemIds.Contains(itemId))
            {
                copperPerUnit = 0;
                return false;
            }

            return BarterItemDecisionDefaults.TryGetDefault(itemId, out copperPerUnit);
        }

        /// <summary>
        /// Blish-free merge that turns persisted state + curated defaults
        /// into the CurrencyValuation the solver actually receives, for
        /// currencies and barter items alike. No id can land in both a
        /// merged value set and its own cleared set (the constructor would
        /// throw); that holds by construction because neither
        /// TryGetEffective* method returns true for a cleared id. Returns
        /// user overrides plus every non-overridden, non-cleared curated
        /// default; both cleared sets pass through.
        /// </summary>
        public static CurrencyValuation WithDefaults(CurrencyValuation persisted)
        {
            persisted = persisted ?? None;

            var candidateIds = new HashSet<int>(persisted.CopperPerUnit.Keys);
            foreach (int currencyId in CurrencyDecisionDefaults.DefaultCopperPerUnit.Keys)
            {
                candidateIds.Add(currencyId);
            }

            var merged = new Dictionary<int, long>();
            foreach (int currencyId in candidateIds)
            {
                if (persisted.TryGetEffectiveCopperValue(currencyId, out long copperPerUnit))
                {
                    merged[currencyId] = copperPerUnit;
                }
            }

            var candidateItemIds = new HashSet<int>(persisted.ItemCopperPerUnit.Keys);
            foreach (int itemId in BarterItemDecisionDefaults.Defaults.Keys)
            {
                candidateItemIds.Add(itemId);
            }

            var mergedItems = new Dictionary<int, long>();
            foreach (int itemId in candidateItemIds)
            {
                if (persisted.TryGetEffectiveItemCopperValue(itemId, out long copperPerUnit))
                {
                    mergedItems[itemId] = copperPerUnit;
                }
            }

            return new CurrencyValuation(
                merged, persisted.ClearedCurrencyIds, mergedItems, persisted.ClearedItemIds);
        }
    }
}
