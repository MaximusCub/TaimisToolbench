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
    /// Every icon site in the module, audited against the hover standard.
    /// The renderers that build icons take Blish controls and tests
    /// reference no UI code (repo invariant), so no test can call one; this
    /// reads the sources instead and fails when a site does not go through
    /// the facility. It proves nothing about what a control draws - the
    /// behaviour tests for that are SecondTooltipBoxTests and
    /// IconWikiTargetTests - only that no call site is left outside.
    /// <para>
    /// The compiler already stops the arguments being omitted. What is left
    /// is the laundering: a `default` slid into the slot, or a file that
    /// names no source for the value it passes.
    /// </para>
    /// </summary>
    public class IconStandardCallSiteTests
    {
        // Every entry point that builds an icon a reader can hover.
        private static readonly string[] IconCalls =
        {
            "IconControls.CreateItemIcon(",
            "IconControls.CreateItemIconDeferredArt(",
            "IconControls.CreateCurrencyIcon(",
            "IconNameRowHelpers.CreateIconAndEllipsizedName(",
        };

        // The factories that produce a hover. None() is deliberately absent:
        // it names its own silence and carries no page.
        private static readonly string[] HoveringFactories =
        {
            "ItemIconTooltip.ForItem(",
            "ItemIconTooltip.Composed(",
            "ItemIconTooltip.ForCurrency(",
        };

        // The one file that may pass an intent it did not name: the shared
        // row builder, which forwards the one its own caller was required
        // to name.
        private static readonly string[] Forwarders =
        {
            "Views/Rendering/IconNameRowHelpers.cs",
        };

        // The files that still call a pre-tier overload, each owned by the
        // branch that will migrate it. Removing an entry is the act of the
        // commit that migrates its call.
        private static readonly string[] PreTier =
        {
            "Views/RankerTabContent.cs",
            "Views/Rendering/IconNameRowHelpers.cs",
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

            // The sweep is only evidence if it found the tree.
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

        private static string LastArgument(string arguments)
        {
            int depth = 0;
            int start = 0;
            for (int i = 0; i < arguments.Length; i++)
            {
                char c = arguments[i];
                if (c == '(' || c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    start = i + 1;
                }
            }

            return arguments.Substring(start).Trim();
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
        /// The standard is not a rule to remember, it is the only way
        /// through. Every icon in the module is built by one of four
        /// calls, each takes an intent the compiler will not let a caller
        /// drop, and each renders at a named tier.
        /// <para>
        /// The workflow's own "Every item icon names its tier and what it
        /// shows on hover" step audits the first three calls the same way.
        /// CreateCurrencyIcon is gated here only, because the branch that
        /// gave it an intent could not edit the workflow file.
        /// </para>
        /// </summary>
        [Fact]
        public void EveryIconSiteNamesTheIntentItPasses()
        {
            var files = ModuleSources();
            var offenders = new List<string>();
            int sites = 0;

            foreach (var call in CallsTo(files, IconCalls))
            {
                sites++;
                if (call.Item3 == null)
                {
                    offenders.Add(call.Item1 + ":" + call.Item2 + " - unbalanced call");
                    continue;
                }

                if (Forwarders.Contains(call.Item1))
                {
                    continue;
                }

                string tail = LastArgument(call.Item3);
                if (tail.StartsWith("default", StringComparison.Ordinal))
                {
                    offenders.Add(call.Item1 + ":" + call.Item2 + " - passes default");
                }

                if (!Read(call.Item1).Contains("ItemIconTooltip."))
                {
                    offenders.Add(call.Item1 + ":" + call.Item2 + " - names no ItemIconTooltip factory");
                }

                // A pixel size the call site chose is how eleven icons came
                // to draw at eleven sizes. Only the two pre-tier overloads
                // still take one, and only their one allow-listed caller.
                if (!call.Item3.Contains("ItemIconTier.") && !PreTier.Contains(call.Item1))
                {
                    offenders.Add(call.Item1 + ":" + call.Item2 + " - names no ItemIconTier");
                }
            }

            Assert.True(sites >= 15, "Found only " + sites + " icon sites.");
            Assert.Equal(new string[0], offenders.ToArray());
        }

        /// <summary>
        /// The half that used to be missing entirely. Every hover names the
        /// wiki page its icon opens, from a source in its own file - either
        /// an IconWikiTarget factory or the row's own WikiTarget.
        /// </summary>
        [Fact]
        public void EveryHoverNamesTheWikiPageItsIconOpens()
        {
            var files = ModuleSources();
            var offenders = new List<string>();
            int hovers = 0;

            foreach (var call in CallsTo(files, HoveringFactories))
            {
                hovers++;
                if (call.Item3 == null)
                {
                    offenders.Add(call.Item1 + ":" + call.Item2 + " - unbalanced call");
                    continue;
                }

                // The factory itself declares the parameter; only calls are
                // audited.
                if (call.Item1 == "Views/Rendering/ItemIconTooltip.cs")
                {
                    continue;
                }

                string tail = LastArgument(call.Item3);
                if (tail.StartsWith("default", StringComparison.Ordinal))
                {
                    offenders.Add(call.Item1 + ":" + call.Item2 + " - passes default");
                    continue;
                }

                // The value may reach the call through a local, so the
                // file rather than the argument is what has to show where
                // it came from - an IconWikiTarget factory, a row's own
                // WikiTarget, or WikiTargetFor on a tree node.
                if (!Read(call.Item1).Contains("WikiTarget"))
                {
                    offenders.Add(
                        call.Item1 + ":" + call.Item2 + " - names no wiki target source");
                }
            }

            Assert.True(hovers >= 12, "Found only " + hovers + " hovering factory calls.");
            Assert.Equal(new string[0], offenders.ToArray());
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
        /// and the last-line rule are decided once. TooltipContent declares
        /// WithExtra; SecondTooltipBox is its only caller.
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
        /// A new factory is a new thing an icon is allowed to say, or a new
        /// page it is allowed to open. Both sets are pinned so widening one
        /// is a visible diff here.
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
        /// Every factory that shows a hover takes a page as well, so the
        /// two halves of the standard cannot be separated. Only the named
        /// silence is exempt, and it derives its own.
        /// </summary>
        [Fact]
        public void EveryHoveringFactoryTakesAWikiTarget()
        {
            string source = Read("Views/Rendering/ItemIconTooltip.cs");
            var offenders = new List<string>();

            foreach (Match m in Regex.Matches(source, @"internal static ItemIconTooltip (\w+)\("))
            {
                string name = m.Groups[1].Value;
                if (name == "None")
                {
                    continue;
                }

                string args = Arguments(source, m.Index + m.Length - 1);
                if (args == null || !args.Contains("IconWikiTarget wiki"))
                {
                    offenders.Add(name);
                }
            }

            Assert.Equal(new string[0], offenders.ToArray());
        }
    }
}
