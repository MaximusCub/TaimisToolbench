using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Every icon site in the module, audited against the one entry point.
    /// A caller passes an id and a size bucket. It cannot pass a name, an
    /// icon url, a rarity, a border or a hover.
    /// <para>
    /// The renderers take Blish controls and tests reference no UI code, so
    /// no test can call one. This reads the sources instead. It proves
    /// nothing about what a control draws - CurrencyTooltipStandardTests
    /// and ItemTooltipStandardTests are the behaviour half - only that no
    /// call site is left outside.
    /// </para>
    /// </summary>
    public class IconStandardCallSiteTests
    {
        // The entry points that take an id and a bucket.
        private static readonly string[] ByIdCalls =
        {
            "IconControls.DrawItemIcon(",
            "IconControls.DrawItemIconDeferredArt(",
            "IconControls.CreateCurrencyIcon(",
            "IconControls.CreateCurrencyIconDeferredArt(",
            "IconNameRowHelpers.DrawIconAndName(",
        };

        // The one seam that still takes a hover the caller composed, for a
        // subject no id can answer for. Every entry names why.
        private static readonly string[] FromCaptureCalls =
        {
            "IconControls.CreateItemIconFromCapture(",
            "IconControls.CreateItemIconDeferredArtFromCapture(",
        };

        private static readonly Dictionary<string, string> CaptureCallers =
            new Dictionary<string, string>
            {
                {
                    "Views/Rendering/RichTooltipSurface.cs",
                    "draws inside a tooltip box, which has no id and takes no clicks"
                },
                {
                    "Views/SuggestionPanel.cs",
                    "draws a search result before the item store has ever seen the id"
                },
                {
                    "Views/Rendering/TreeSectionController.cs",
                    "draws a dimmed reference branch and synthesized rows the item store never held"
                },
            };

        private static string RepoRoot()
        {
            string csproj = RepoFileLocator.FindRepoFile("TaimisToolbench.csproj");
            Assert.True(csproj != null, "Cannot locate the repo root from the test output directory.");
            return Path.GetDirectoryName(csproj);
        }

        private static IReadOnlyList<string> ModuleSources()
        {
            string root = RepoRoot();
            var files = new List<string> { "Module.cs" };
            foreach (var dir in new[] { "Models", "Services", "Views", "Contracts", "Properties" })
            {
                string full = Path.Combine(root, dir);
                if (!Directory.Exists(full))
                {
                    continue;
                }

                foreach (var path in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories))
                {
                    files.Add(path.Substring(root.Length + 1).Replace('\\', '/'));
                }
            }

            Assert.True(files.Count > 100, "Found only " + files.Count + " module sources.");
            return files;
        }

        private static string Read(string relativePath)
        {
            return File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        }

        /// <summary>The text between a call's parentheses, or null when the
        /// walk does not balance - never the rest of the file, which would
        /// contain almost any token and turn the audit into a pass.</summary>
        private static string Arguments(string text, int openParen)
        {
            int depth = 0;
            for (int i = openParen; i < text.Length; i++)
            {
                if (text[i] == '(')
                {
                    depth++;
                }
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(openParen + 1, i - openParen - 1);
                    }
                }
            }

            return null;
        }

        private static IEnumerable<Tuple<string, int, string>> CallsTo(
            IReadOnlyList<string> files, IEnumerable<string> markers)
        {
            foreach (var file in files)
            {
                string text = Read(file);
                foreach (var marker in markers)
                {
                    int from = 0;
                    while (true)
                    {
                        int at = text.IndexOf(marker, from, StringComparison.Ordinal);
                        if (at < 0)
                        {
                            break;
                        }

                        from = at + marker.Length;
                        int line = text.Take(at).Count(c => c == '\n') + 1;
                        yield return Tuple.Create(
                            file, line, Arguments(text, at + marker.Length - 1));
                    }
                }
            }
        }

        /// <summary>
        /// Every icon in the module is drawn by one of five calls, each of
        /// which takes an id and a named bucket. None of them may be handed
        /// a composed hover or hand-built facts: that is what let the
        /// Settings tab show a currency's prose while the Recipe Tree
        /// showed a bare name.
        /// </summary>
        [Fact]
        public void EveryIconSiteNamesOnlyAnIdAndABucket()
        {
            var files = ModuleSources();
            var offenders = new List<string>();
            int sites = 0;

            foreach (var call in CallsTo(files, ByIdCalls))
            {
                sites++;
                if (call.Item3 == null)
                {
                    offenders.Add(call.Item1 + ":" + call.Item2 + " - unbalanced call");
                    continue;
                }

                // The facility's own overloads forward to each other.
                if (call.Item1 == "Views/Rendering/IconControls.cs"
                    || call.Item1 == "Views/Rendering/IconNameRowHelpers.cs")
                {
                    continue;
                }

                if (!call.Item3.Contains("ItemIconTier."))
                {
                    offenders.Add(call.Item1 + ":" + call.Item2 + " - names no ItemIconTier");
                }

                foreach (var banned in new[]
                {
                    "ItemIconTooltip.", "ItemIconFrame.", "IconWikiTarget.",
                    "CurrencyTooltipFacts.For", "ItemTooltipIdentity.",
                })
                {
                    if (call.Item3.Contains(banned))
                    {
                        offenders.Add(
                            call.Item1 + ":" + call.Item2 + " - passes " + banned);
                    }
                }
            }

            Assert.True(sites >= 15, "Found only " + sites + " icon sites.");
            Assert.Equal(new string[0], offenders.ToArray());
        }

        /// <summary>
        /// The capture seam is the one way to draw an icon for a subject no
        /// id can answer for. Its caller list is pinned, so a new one is a
        /// visible diff here rather than a quiet way back out.
        /// </summary>
        [Fact]
        public void OnlyThePinnedSurfacesDrawFromACapture()
        {
            var files = ModuleSources();
            var callers = new SortedSet<string>();

            foreach (var call in CallsTo(files, FromCaptureCalls))
            {
                if (call.Item1 == "Views/Rendering/IconControls.cs")
                {
                    continue;
                }

                callers.Add(call.Item1);
            }

            Assert.Equal(CaptureCallers.Keys.OrderBy(k => k).ToArray(), callers.ToArray());
        }

        /// <summary>
        /// The affordance sentence is written once. A composer that wrote
        /// its own copy is how the line came to sit in the middle of a box
        /// on one surface and at the end on another.
        /// </summary>
        [Fact]
        public void OnlyIconWikiTargetWordsTheAffordance()
        {
            var offenders = ModuleSources()
                .Where(f => f != "Services/IconWikiTarget.cs")
                .Where(f => Read(f).Contains("Right-click to open the wiki")
                    || Read(f).Contains("Right-click to see how to get this"))
                .ToArray();

            Assert.Equal(new string[0], offenders);
        }

        /// <summary>
        /// One place hangs a second box off a first, so the blank-line rule
        /// and the last-line rule are decided once.
        /// </summary>
        [Fact]
        public void OnlySecondTooltipBoxAttachesASecondBox()
        {
            var allowed = new[] { "Services/TooltipContent.cs", "Services/SecondTooltipBox.cs" };
            var offenders = ModuleSources()
                .Where(f => !allowed.Contains(f))
                .Where(f => Read(f).Contains(".WithExtra("))
                .ToArray();

            Assert.Equal(new string[0], offenders);
        }

        /// <summary>
        /// A new factory is a new thing an icon may say, or a new page it
        /// may open. Both sets are pinned.
        /// </summary>
        [Fact]
        public void TheFactorySetsStayPinned()
        {
            var hover = new SortedSet<string>(Regex
                .Matches(
                    Read("Views/Rendering/ItemIconTooltip.cs"),
                    @"internal static ItemIconTooltip (\w+)\(")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value));

            Assert.Equal(
                new[] { "Composed", "ForCurrency", "ForItem", "None" }, hover.ToArray());

            var page = new SortedSet<string>(Regex
                .Matches(
                    Read("Services/IconWikiTarget.cs"),
                    @"public static IconWikiTarget (\w+)\(")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value));

            Assert.Equal(
                new[] { "Acquisition", "ItemPage", "None", "RecipeSheet" }, page.ToArray());
        }

        /// <summary>
        /// A size bucket is the only way to ask for an icon size. Both
        /// overloads that took a raw pixel number are gone, so a call site
        /// cannot pick one at all.
        /// </summary>
        [Fact]
        public void NoIconEntryPointTakesARawPixelSize()
        {
            var offenders = new List<string>();
            foreach (var file in new[]
            {
                "Views/Rendering/IconControls.cs",
                "Views/Rendering/IconNameRowHelpers.cs",
            })
            {
                string text = Read(file);
                foreach (Match m in Regex.Matches(text, @"internal static [^\n]*\("))
                {
                    string args = Arguments(text, m.Index + m.Length - 1);
                    if (args != null && args.Contains("int iconSize"))
                    {
                        offenders.Add(file + ": " + m.Value.Trim());
                    }
                }
            }

            Assert.Equal(new string[0], offenders.ToArray());
        }

        /// <summary>
        /// No renderer carries a stat-block accessor any more. They take a
        /// facts resolver, which answers the whole tooltip rather than one
        /// field of it.
        /// </summary>
        [Fact]
        public void NoRendererTakesABareStatBlockAccessor()
        {
            // MainView and the Ranker still hold one: it is the session
            // store's own accessor, and it is what their facts resolvers
            // read. Nothing hands it to an icon.
            var allowed = new[]
            {
                "Views/MainView.cs",
                "Views/RankerTabContent.cs",
                "Views/SettingsTabContent.cs",
                "Views/PlanHistoryTabContent.cs",
                "Views/CraftingPlanView.cs",
                "Views/Rendering/TreeSectionController.cs",
                "Services/SocketedUpgradeView.cs",
                "Services/TreeRowTooltipComposer.cs",
                "Services/ItemStatWarmer.cs",
            };

            var offenders = ModuleSources()
                .Where(f => f.StartsWith("Views/Rendering/", StringComparison.Ordinal))
                .Where(f => !allowed.Contains(f))
                .Where(f => Read(f).Contains("Func<int, ItemStatBlock>"))
                .ToArray();

            Assert.Equal(new string[0], offenders);
        }
    }
}
