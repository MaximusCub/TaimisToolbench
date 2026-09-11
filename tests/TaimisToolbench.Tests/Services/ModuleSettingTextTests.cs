using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The two things about ModuleSettings that can be checked without Blish:
    /// which settings Blish's own Manage Modules panel draws, and how wide
    /// their names would be. ModuleSettings takes a SettingCollection so it
    /// cannot be constructed here; ModuleSettingText is the table it reads,
    /// so pinning the table pins what a player sees.
    /// See docs/blish-settings-panel.md for the panel's measured geometry.
    /// </summary>
    public class ModuleSettingTextTests
    {
        private static readonly Menomonia14Metrics Font = Menomonia14Metrics.Load();

        /// <summary>
        /// No descriptor is visible today, so this passes over an empty set.
        /// It is kept as the guard on turning one back on: the name has to
        /// fit the slider from the first build that shows it.
        /// TheNameThatCollidedMeasuresPastTheBudget below is what proves the
        /// measurement still has teeth while the set is empty.
        /// </summary>
        [Fact]
        public void EveryVisibleNameFitsBesideItsControl()
        {
            var tooWide = new List<string>();
            foreach (var descriptor in ModuleSettingText.All.Where(d => d.ShownInBlishPanel))
            {
                int width = Font.MeasureLabelWidth(descriptor.DisplayName);
                if (width > ModuleSettingText.MaxDisplayNameWidth)
                {
                    tooWide.Add(string.Format(
                        "{0}: \"{1}\" is {2}px", descriptor.Key, descriptor.DisplayName, width));
                }
            }

            Assert.True(
                tooWide.Count == 0,
                "Blish draws these names over their sliders. Shorten the name and move the detail "
                + "into the description, which is not width-constrained:"
                + Environment.NewLine + string.Join(Environment.NewLine, tooWide));
        }

        /// <summary>
        /// The witness for the reported defect. "Snapshot refresh interval
        /// (minutes)" is the name that shipped, and it is 29px past the
        /// budget, so the test above would have failed on it.
        /// </summary>
        [Fact]
        public void TheNameThatCollidedMeasuresPastTheBudget()
        {
            Assert.Equal(209, Font.MeasureLabelWidth("Snapshot refresh interval (minutes)"));
            Assert.True(209 > ModuleSettingText.MaxDisplayNameWidth);
        }

        [Fact]
        public void EveryNameAndDescriptionDrawsInTheShippedFont()
        {
            foreach (var descriptor in ModuleSettingText.All)
            {
                Assert.True(Font.CanDraw(descriptor.DisplayName), descriptor.Key + " display name");
                Assert.True(Font.CanDraw(descriptor.Description), descriptor.Key + " description");
            }
        }

        [Fact]
        public void EveryDescriptorCarriesBothStrings()
        {
            foreach (var descriptor in ModuleSettingText.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Key));
                Assert.False(string.IsNullOrWhiteSpace(descriptor.DisplayName), descriptor.Key);
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Description), descriptor.Key);
            }
        }

        /// <summary>
        /// A duplicate key would define one setting twice and silently drop
        /// the other, since SettingCollection matches on key.
        /// </summary>
        [Fact]
        public void EveryKeyIsDistinct()
        {
            var keys = ModuleSettingText.All.Select(d => d.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        /// <summary>
        /// The module draws its own Settings tab, so Blish's panel shows
        /// nothing for it. Turning a setting back on changes where its value
        /// is stored, which needs the migration in ModuleSettings.DefineHidden
        /// to run in reverse, so the decision is pinned rather than assumed.
        /// </summary>
        [Fact]
        public void NoSettingReachesBlishsPanel()
        {
            var visible = ModuleSettingText.All
                .Where(d => d.ShownInBlishPanel)
                .Select(d => d.Key)
                .ToArray();

            Assert.True(
                visible.Length == 0,
                "Blish's Manage Modules panel would draw these, and this module "
                + "puts every setting on its own Settings tab instead: "
                + string.Join(", ", visible));
        }

        /// <summary>
        /// A descriptor missing from All is a name no width check ever sees,
        /// which is exactly how the collided names got in.
        /// </summary>
        [Fact]
        public void AllListsEveryDescriptorThatExists()
        {
            var declared = typeof(ModuleSettingText)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(ModuleSettingText.Descriptor))
                .Select(f => ((ModuleSettingText.Descriptor)f.GetValue(null)).Key)
                .OrderBy(k => k, StringComparer.Ordinal);

            Assert.Equal(declared, ModuleSettingText.All.Select(d => d.Key).OrderBy(k => k, StringComparer.Ordinal));
        }

        /// <summary>
        /// ModuleSettings.Define reads the descriptor flag and picks the
        /// collection from it. DefineIn is the one seam that skips that read,
        /// and a constructor line calling it directly would put a setting in
        /// Blish's panel with no descriptor saying so.
        /// </summary>
        [Fact]
        public void TheConstructorRoutesEverySettingThroughTheDescriptor()
        {
            string path = RepoFileLocator.FindRepoFile(Path.Combine("Services", "ModuleSettings.cs"));
            Assert.True(path != null, "Cannot locate Services/ModuleSettings.cs from the test output directory.");
            string source = File.ReadAllText(path);

            int start = source.IndexOf("public ModuleSettings(SettingCollection settings)", StringComparison.Ordinal);
            Assert.True(start >= 0, "ModuleSettings no longer declares the constructor this test reads.");
            int end = source.IndexOf("private const string HiddenCollectionKey", start, StringComparison.Ordinal);
            Assert.True(end > start, "Cannot find the end of the constructor body.");

            string body = source.Substring(start, end - start);
            Assert.False(
                body.Contains("DefineIn("),
                "The constructor calls DefineIn directly, so that setting ignores its "
                + "ShownInBlishPanel flag. Call Define instead.");
        }

        /// <summary>
        /// The table only governs what a player sees if ModuleSettings reads
        /// every entry of it. Checked against the source text because
        /// ModuleSettings takes a SettingCollection and cannot be built here.
        /// </summary>
        [Fact]
        public void ModuleSettingsDefinesEveryDescriptor()
        {
            string path = RepoFileLocator.FindRepoFile(Path.Combine("Services", "ModuleSettings.cs"));
            Assert.True(path != null, "Cannot locate Services/ModuleSettings.cs from the test output directory.");
            string source = File.ReadAllText(path);

            var unused = typeof(ModuleSettingText)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(ModuleSettingText.Descriptor))
                .Select(f => f.Name)
                .Where(name => !source.Contains("ModuleSettingText." + name))
                .ToList();

            Assert.True(
                unused.Count == 0,
                "ModuleSettings never defines these settings, so nothing persists them: "
                + string.Join(", ", unused));
        }
    }
}
