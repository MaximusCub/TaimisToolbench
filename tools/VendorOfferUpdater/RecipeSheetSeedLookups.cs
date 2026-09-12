using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VendorOfferUpdater
{
    /// <summary>
    /// What ref/recipes_seed.json says about one recipe, for the fields
    /// ref/recipe_sheet_items.json repeats so a maintainer can read an
    /// entry without opening the 12MB seed.
    /// </summary>
    internal sealed class RecipeSeedFact
    {
        internal int OutputItemId { get; set; }

        internal List<string>? Disciplines { get; set; }

        internal int MinRating { get; set; }
    }

    /// <summary>
    /// Reads ref/recipes_seed.json into a recipe-id lookup. A file that is
    /// absent or unreadable yields an empty lookup: those fields are
    /// provenance, and RecipeSheetMapBuilder omits them rather than
    /// failing the run that resolved the ids.
    /// </summary>
    internal static class RecipeSeedFacts
    {
        internal static IReadOnlyDictionary<int, RecipeSeedFact> Load(string path)
        {
            var facts = new Dictionary<int, RecipeSeedFact>();
            if (!File.Exists(path))
            {
                Console.WriteLine($"  WARNING: {path} is not there; entries omit their recipe fields.");
                return facts;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("recipes", out var recipes) ||
                recipes.ValueKind != JsonValueKind.Array)
            {
                return facts;
            }

            foreach (var recipe in recipes.EnumerateArray())
            {
                if (!recipe.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var fact = new RecipeSeedFact();
                if (recipe.TryGetProperty("outputItemId", out var output) &&
                    output.ValueKind == JsonValueKind.Number)
                {
                    fact.OutputItemId = output.GetInt32();
                }

                if (recipe.TryGetProperty("minRating", out var rating) &&
                    rating.ValueKind == JsonValueKind.Number)
                {
                    fact.MinRating = rating.GetInt32();
                }

                if (recipe.TryGetProperty("disciplines", out var disciplines) &&
                    disciplines.ValueKind == JsonValueKind.Array)
                {
                    var names = new List<string>();
                    foreach (var discipline in disciplines.EnumerateArray())
                    {
                        if (discipline.ValueKind == JsonValueKind.String)
                        {
                            names.Add(discipline.GetString()!);
                        }
                    }

                    fact.Disciplines = names.Count > 0 ? names : null;
                }

                facts[id.GetInt32()] = fact;
            }

            return facts;
        }
    }

    /// <summary>
    /// Reads ref/item_name_seed.json into an item-id to name lookup. The
    /// seed repeats an id where two rows share it; the first row wins,
    /// which is the same rule the module's own seed reader applies.
    /// </summary>
    internal static class ItemNameSeed
    {
        internal static IReadOnlyDictionary<int, string> Load(string path)
        {
            var names = new Dictionary<int, string>();
            if (!File.Exists(path))
            {
                Console.WriteLine($"  WARNING: {path} is not there; entries omit craftedItemName.");
                return names;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return names;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.Number ||
                    !item.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                int itemId = id.GetInt32();
                if (!names.ContainsKey(itemId))
                {
                    names[itemId] = name.GetString()!;
                }
            }

            return names;
        }
    }
}
