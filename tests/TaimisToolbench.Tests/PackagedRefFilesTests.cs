using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests
{
    /// <summary>
    /// TaimisToolbench.csproj packs ref/** into the .bhm a player installs,
    /// and one Exclude on that copy is the only thing that keeps a
    /// developer-side cache out of the download. .gitignore decides
    /// something else entirely: what git stores. The two lists drifted
    /// apart three times, and a cache shipped in a locally built module
    /// each time.
    /// <para>
    /// This asserts the rule rather than the artefact: every path
    /// .gitignore names under ref/ is named in that Exclude. A build is not
    /// unpacked and no .bhm is needed, so the check runs in the same suite
    /// as everything else.
    /// </para>
    /// </summary>
    public class PackagedRefFilesTests
    {
        [Fact]
        public void EveryGitignoredRefFile_IsExcludedFromTheShippedPackage()
        {
            var ignored = IgnoredRefFiles(ReadRepoFile(".gitignore"));
            var excluded = ExcludedRefFiles(ReadRepoFile("TaimisToolbench.csproj"));

            // Both parsers read hand-maintained text. An empty list means the
            // shape they look for moved, not that the rule is satisfied.
            Assert.NotEmpty(ignored);
            Assert.NotEmpty(excluded);

            var missing = ignored.Except(excluded).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.True(
                missing.Count == 0,
                ".gitignore keeps these ref/ files out of git, but TaimisToolbench.csproj's " +
                "RefFiles Exclude does not keep them out of the shipped .bhm: " +
                string.Join(", ", missing) + ". Add each to that Exclude.");
        }

        /// <summary>
        /// The other direction. An Exclude naming a file that is neither
        /// gitignored nor on disk is either a typo or a leftover from a
        /// deleted cache, and it protects nothing.
        /// </summary>
        [Fact]
        public void EveryExcludedRefFile_IsGitignoredOrTracked()
        {
            var ignored = IgnoredRefFiles(ReadRepoFile(".gitignore"));
            string refDirectory = Path.GetDirectoryName(RepoFile("ref/vendor_offers.json"));

            var stale = ExcludedRefFiles(ReadRepoFile("TaimisToolbench.csproj"))
                .Where(name => !ignored.Contains(name))
                .Where(name => !File.Exists(Path.Combine(refDirectory, name)))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.True(
                stale.Count == 0,
                "TaimisToolbench.csproj excludes ref/ files that are neither gitignored nor " +
                "present in ref/: " + string.Join(", ", stale) + ". Drop them from the Exclude.");
        }

        /// <summary>
        /// Proves the comparison bites. The real .gitignore gains one line
        /// naming a cache the real Exclude does not carry, and the same
        /// parsers report exactly that one.
        /// </summary>
        [Fact]
        public void AGitignoredRefFileMissingFromTheExclude_IsReported()
        {
            string broken = ReadRepoFile(".gitignore") + Environment.NewLine + "ref/scraper_run_cache.json";

            var missing = IgnoredRefFiles(broken)
                .Except(ExcludedRefFiles(ReadRepoFile("TaimisToolbench.csproj")))
                .ToList();

            Assert.Contains("scraper_run_cache.json", missing);
        }

        /// <summary>
        /// A wildcard under ref/ cannot be compared name for name against
        /// the Exclude, so the parser refuses it rather than skipping it
        /// and leaving the rule unchecked.
        /// </summary>
        [Fact]
        public void AWildcardRefIgnoreRule_IsRefusedRatherThanSkipped()
        {
            string broken = ReadRepoFile(".gitignore") + Environment.NewLine + "ref/*_cache.json";

            var error = Assert.Throws<InvalidOperationException>(() => IgnoredRefFiles(broken));
            Assert.Contains("ref/*_cache.json", error.Message);
        }

        /// <summary>
        /// Every ref/ path .gitignore names, as a bare file name. Comments,
        /// blanks and negations are not rules about what ships; anything
        /// else under ref/ is.
        /// </summary>
        private static HashSet<string> IgnoredRefFiles(string gitignore)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in gitignore.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == '!')
                {
                    continue;
                }

                string path = line.TrimStart('/');
                if (!path.StartsWith("ref/", StringComparison.Ordinal))
                {
                    continue;
                }

                string name = path.Substring("ref/".Length);
                if (name.Length == 0 || name.IndexOfAny(new[] { '*', '?', '[', '/' }) >= 0)
                {
                    throw new InvalidOperationException(
                        "This test compares ref/ ignore rules to the packaging Exclude name for " +
                        "name, and cannot do that for '" + line + "'. Name the file, or teach " +
                        "this test how to match the pattern.");
                }

                names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// Every file name the RefFiles Exclude keeps out of the package.
        /// Each entry has to sit directly under ref/: an entry that does
        /// not is excluding something other than what this rule is about.
        /// </summary>
        private static HashSet<string> ExcludedRefFiles(string csproj)
        {
            var element = Regex.Match(csproj, @"<RefFiles\b[^>]*/>");
            Assert.True(
                element.Success,
                "TaimisToolbench.csproj has no RefFiles item. The packaging Exclude this test " +
                "reads was renamed or removed; re-point the test at whatever now decides what " +
                "ships in ref/.");

            var attribute = Regex.Match(element.Value, "Exclude=\"([^\"]*)\"");
            Assert.True(
                attribute.Success,
                "TaimisToolbench.csproj's RefFiles item carries no Exclude, so every ref/ file " +
                "on disk ships, developer caches included.");

            const string Prefix = @"$(ProjectDir)ref\";
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string entry in attribute.Groups[1].Value.Split(';'))
            {
                string trimmed = entry.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                Assert.StartsWith(Prefix, trimmed, StringComparison.Ordinal);
                names.Add(trimmed.Substring(Prefix.Length));
            }

            return names;
        }

        private static string ReadRepoFile(string relativePath)
        {
            return File.ReadAllText(RepoFile(relativePath));
        }

        private static string RepoFile(string relativePath)
        {
            string path = RepoFileLocator.FindRepoFile(relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate " + relativePath + " by walking up from the test assembly's directory.");
            return path;
        }
    }
}
