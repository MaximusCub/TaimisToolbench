using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The line-count ratchet in docs/file-budgets.txt, applied to the whole
    /// tree rather than to the listed paths alone.
    /// <para>
    /// Every entry in that file was set by hand from the file's size on the
    /// day it was added, so a file added without one was gated by nothing.
    /// A file with no entry is now held to the default the budget file
    /// declares; the entries are the exceptions, for files allowed to be
    /// longer. An entry below the default still binds at the entry.
    /// </para>
    /// <para>
    /// This reads sources rather than calling anything, for the same reason
    /// IconStandardCallSiteTests does: the invariant is about the shape of
    /// the repository, and no production call can express it.
    /// </para>
    /// </summary>
    public class FileLineBudgetTests
    {
        private const string BudgetFile = "docs/file-budgets.txt";

        // Declared in the budget file so the file that explains the rule is
        // also the file that sets it. A comment line, so a reader who greps
        // the file for a path never trips over it.
        private static readonly Regex DefaultDirective =
            new Regex(@"^#\s*default\s+(\d+)\s*$");

        // Everything git tracks and nothing it does not. Verified equal to
        // `git ls-files '*.cs'` over all 734 sources on 2026-09-06.
        private static readonly string[] ExcludedDirectories =
        {
            ".git", ".claude", "bin", "obj", "packages",
        };

        [Fact]
        public void EveryTrackedSourceIsWithinItsBudget()
        {
            string root = RepoRoot();
            int fallback;
            var entries = ReadBudgets(Path.Combine(root, BudgetFile), out fallback);
            var sizes = TrackedSources(root);

            var failures = Evaluate(sizes, entries, fallback);

            Assert.True(
                failures.Count == 0,
                "The line-count ratchet failed. Every tracked .cs file is held to its"
                + " entry in " + BudgetFile + ", or to the " + fallback
                + " line default when it has none. Raise the budget in that same"
                + " commit and say why in the message, add an entry for a file that"
                + " has none, or put the growth somewhere it belongs."
                + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        [Fact]
        public void TheWalkFindsTheTreeAndTheBudgetFileDescribesIt()
        {
            string root = RepoRoot();
            int fallback;
            var entries = ReadBudgets(Path.Combine(root, BudgetFile), out fallback);
            var sizes = TrackedSources(root);

            // A walk that found nothing would pass the ratchet by finding no
            // violations, which is the one way this test could lie.
            Assert.True(sizes.Count > 500, "Found only " + sizes.Count + " sources.");
            Assert.True(entries.Count > 500, "Read only " + entries.Count + " entries.");

            // A default nothing approaches is decoration, and one half the tree
            // exceeds is useless. Both would be a mistake in the budget file
            // rather than in the code it gates.
            int over = sizes.Values.Count(lines => lines > fallback);
            Assert.InRange(over, 1, sizes.Count / 2);
        }

        [Fact]
        public void TheRatchetFailsOnAnOverLengthFileWithNoEntry()
        {
            var sizes = new Dictionary<string, int>
            {
                { "Short.cs", 10 },
                { "Long.cs", 400 },
                { "Excepted.cs", 400 },
                { "Tight.cs", 40 },
            };
            var entries = new Dictionary<string, int>
            {
                { "Excepted.cs", 400 },
                { "Tight.cs", 20 },
                { "Gone.cs", 100 },
            };

            var failures = Evaluate(sizes, entries, 300);

            // The case the entry-walking gate could not see: no entry, over
            // the default.
            Assert.Contains("Long.cs: 400 lines, default 300 (+100)", failures);

            // An entry below the default still binds, so nothing got looser
            // when the default arrived.
            Assert.Contains("Tight.cs: 40 lines, budget 20 (+20)", failures);

            // A rename has to move its budget with it, not drop it.
            Assert.Contains("budget entry for a path that no longer exists: Gone.cs", failures);

            // A file at its exception, and a file under the default, both pass.
            Assert.DoesNotContain(failures, line => line.StartsWith("Excepted.cs:"));
            Assert.DoesNotContain(failures, line => line.StartsWith("Short.cs:"));
            Assert.Equal(3, failures.Count);
        }

        private static List<string> Evaluate(
            IDictionary<string, int> sizes, IDictionary<string, int> entries, int fallback)
        {
            var failures = new List<string>();

            foreach (var path in entries.Keys.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!sizes.ContainsKey(path))
                {
                    failures.Add("budget entry for a path that no longer exists: " + path);
                }
            }

            foreach (var path in sizes.Keys.OrderBy(p => p, StringComparer.Ordinal))
            {
                int budget;
                bool excepted = entries.TryGetValue(path, out budget);
                if (!excepted)
                {
                    budget = fallback;
                }

                int actual = sizes[path];
                if (actual > budget)
                {
                    failures.Add(path + ": " + actual + " lines, "
                        + (excepted ? "budget " : "default ") + budget
                        + " (+" + (actual - budget) + ")");
                }
            }

            return failures;
        }

        private static Dictionary<string, int> ReadBudgets(string path, out int fallback)
        {
            Assert.True(File.Exists(path), "Cannot find " + BudgetFile + " at " + path + ".");

            int? declared = null;
            var entries = new Dictionary<string, int>(StringComparer.Ordinal);

            int number = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                number++;
                string line = raw.Trim();
                if (line.StartsWith("#"))
                {
                    var match = DefaultDirective.Match(line);
                    if (match.Success)
                    {
                        Assert.True(declared == null,
                            BudgetFile + ":" + number + ": a second default directive.");
                        declared = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    }

                    continue;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                var fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                Assert.True(fields.Length == 2,
                    BudgetFile + ":" + number + ": expected '<path> <max-lines>', got: " + line);
                Assert.False(entries.ContainsKey(fields[0]),
                    BudgetFile + ":" + number + ": duplicate entry for " + fields[0]
                    + ". A second entry would let a raise land as an appended line"
                    + " rather than an edited one.");

                int budget;
                Assert.True(
                    int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out budget),
                    BudgetFile + ":" + number + ": the budget must be an integer: " + line);
                entries.Add(fields[0], budget);
            }

            Assert.True(declared != null,
                BudgetFile + " has no '# default <lines>' directive. Without it a file"
                + " with no entry would be ungated.");
            fallback = declared.Value;
            return entries;
        }

        private static Dictionary<string, int> TrackedSources(string root)
        {
            var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
            Collect(root, root, sizes);
            return sizes;
        }

        private static void Collect(string root, string directory, Dictionary<string, int> sizes)
        {
            foreach (var path in Directory.GetFiles(directory, "*.cs"))
            {
                sizes[path.Substring(root.Length + 1).Replace('\\', '/')] = LineCount(path);
            }

            foreach (var child in Directory.GetDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if (!ExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)
                    && !IsNestedCheckout(child))
                {
                    Collect(root, child, sizes);
                }
            }
        }

        /// <summary>
        /// A checkout of this same repository nested inside the working tree.
        /// A `git worktree` writes a `.git` FILE there rather than a
        /// directory, and gitignore keeps the whole subtree out of
        /// `git ls-files`, so walking into one counts a second copy of every
        /// source against the budgets - each at whatever length it happens to
        /// have on that branch.
        /// </summary>
        private static bool IsNestedCheckout(string directory)
        {
            string marker = Path.Combine(directory, ".git");
            return File.Exists(marker) || Directory.Exists(marker);
        }

        /// <summary>Newlines, which is what `wc -l` counts and what every
        /// entry in the budget file was seeded from.</summary>
        private static int LineCount(string path)
        {
            int count = 0;
            using (var reader = new StreamReader(path))
            {
                int character;
                while ((character = reader.Read()) >= 0)
                {
                    if (character == '\n')
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static string RepoRoot()
        {
            string csproj = RepoFileLocator.FindRepoFile("TaimisToolbench.csproj");
            Assert.True(csproj != null, "Cannot locate the repo root from the test output directory.");
            return Path.GetDirectoryName(csproj);
        }
    }
}
