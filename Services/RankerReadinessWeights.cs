using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The Crafting Ranker's headline is a weighted mean of six gate
    /// completions, renormalised over the gates that apply to the item. These
    /// are the weights, kept as named constants rather than buried in the
    /// formula so they can be argued with.
    ///
    /// They are NOT derived from each gate's magnitude. Deriving them that way
    /// needs an exchange rate between a day and a pile of gold first, and
    /// neither the GW2 API nor this repo will supply that number. They are
    /// judgement calls about SUBSTITUTABILITY instead, which is a property the
    /// game itself decides.
    ///
    /// Deliberately not a user setting: a user who retunes the weights cannot
    /// compare their own numbers with anyone else's, and the model's
    /// legibility is the feature.
    ///
    /// The argument for each gate's own share: docs/ARCHITECTURE.md,
    /// "Services Q-Z: relocated design narrative".
    /// </summary>
    public static class RankerReadinessWeights
    {
        public const double TimeGates = 0.35;
        public const double Materials = 0.35;
        public const double Currencies = 0.20;
        public const double Disciplines = 0.10;
        public const double Recipes = 0.10;

        /// <summary>
        /// The same share as <see cref="Currencies"/>, on the same
        /// substitutability argument: a barter item is an account-bound
        /// token a vendor takes in place of coin, so like a wallet currency
        /// it cannot be bought and can only be accumulated. The two gates
        /// are one barrier class split across two id spaces
        /// (Models/BarterItemCost.cs), and a plan that pays both therefore
        /// weights that class at 0.40 against the 0.35 its coin bill
        /// carries. Deliberate: a bill nothing can be substituted into is
        /// the harder half of such a plan.
        /// </summary>
        public const double BarterItems = 0.20;

        public static double For(RankerGate gate)
        {
            switch (gate)
            {
                case RankerGate.TimeGates: return TimeGates;
                case RankerGate.Materials: return Materials;
                case RankerGate.Currencies: return Currencies;
                case RankerGate.Disciplines: return Disciplines;
                case RankerGate.Recipes: return Recipes;
                case RankerGate.BarterItems: return BarterItems;
                default: return 0;
            }
        }
    }
}
