using System.Collections.Generic;
using System.IO;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// opportunity-notes (RECIPE-SHEET SAVINGS): loads the
    /// API-verified recipe id -&gt; unlocking recipe-sheet item id seed
    /// (ref/recipe_sheet_items.json) into the plain
    /// IReadOnlyDictionary&lt;int, int&gt; RecipeSheetSavingsCalculator/
    /// CraftingPlanPipeline already accept. Byte-for-byte the same load
    /// shape as DailyCooldownItemService.Load (see that class's own doc
    /// comment) - never throws: null/empty/malformed input degrades to an
    /// empty dictionary so a bad or missing seed file never blocks module
    /// load, and (per RecipeSheetSavingsCalculator's own "empty map ->
    /// nothing" gate) just means the feature stays dormant rather than
    /// crashing.
    ///
    /// Without this loader, Module.cs had no way to populate
    /// recipeSheetItemIdByRecipeId at all - the calculator's own gate on a
    /// non-empty map meant the feature could never fire for a real plan
    /// (KNOWN-ISSUES #49).
    /// </summary>
    internal static class RecipeSheetItemSeedService
    {
        private class RecipeSheetItemEnvelope
        {
            public int SchemaVersion { get; set; }

            public string GeneratedAt { get; set; }

            public string Source { get; set; }

            public List<RecipeSheetItemEntry> Items { get; set; }
        }

        private class RecipeSheetItemEntry
        {
            public int RecipeId { get; set; }

            public int SheetItemId { get; set; }

            // Sheet/crafted item name, discipline, minRating, note,
            // sourceUrl, lastVerified are all present in the seed file for
            // maintainer provenance only - this loader's sole output is
            // the RecipeId -> SheetItemId map, so those fields are never
            // deserialized here.
        }

        public static IReadOnlyDictionary<int, int> Load(Stream stream)
        {
            var result = new Dictionary<int, int>();
            var envelope = JsonSeedReader.Deserialize<RecipeSheetItemEnvelope>(stream);
            if (envelope?.Items == null)
            {
                return result;
            }

            foreach (var entry in envelope.Items)
            {
                if (entry == null || entry.RecipeId <= 0 || entry.SheetItemId <= 0)
                {
                    // Malformed seed data (no real recipe/item ever carries
                    // id <= 0) - skip rather than let a bad row fabricate a
                    // lookup, same guard shape as
                    // DailyCooldownItemService.Load.
                    continue;
                }

                // Last-write-wins on duplicate recipe ids, matching
                // DailyCooldownItemService/AcquisitionHintService.
                result[entry.RecipeId] = entry.SheetItemId;
            }

            return result;
        }
    }
}
