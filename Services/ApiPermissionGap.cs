using System;
using System.Collections.Generic;
using System.Linq;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Which API permissions the module declares that the account never
    /// approved.
    /// <para>
    /// Blish builds a module's Gw2ApiManager from the permission list
    /// stored against that module, not from its manifest, and it ticks a
    /// permission by default only when no list is stored yet. So a
    /// permission added to the manifest after a user first enabled the
    /// module arrives unticked and is never requested, and nothing tells
    /// them. The stored list survives module updates, so this does not
    /// resolve itself.
    /// </para>
    /// <para>
    /// Names are passed as strings, so this stays free of Gw2Sharp and can
    /// be tested.
    /// </para>
    /// </summary>
    internal static class ApiPermissionGap
    {
        /// <summary>
        /// Declared names absent from <paramref name="approved"/>, in
        /// declaration order and compared case-insensitively. Duplicates in
        /// either list are collapsed.
        /// </summary>
        public static IReadOnlyList<string> Unapproved(
            IEnumerable<string> declared, IEnumerable<string> approved)
        {
            if (declared == null)
            {
                return new string[0];
            }

            var held = new HashSet<string>(
                (approved ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)),
                StringComparer.OrdinalIgnoreCase);

            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in declared)
            {
                if (string.IsNullOrEmpty(name) || held.Contains(name) || !seen.Add(name))
                {
                    continue;
                }

                missing.Add(name);
            }

            return missing;
        }

        /// <summary>
        /// What the user has to do about it, or null when nothing is
        /// missing. Names the permissions, because the user has to find
        /// those exact boxes.
        /// </summary>
        public static string Compose(IReadOnlyList<string> unapproved)
        {
            if (unapproved == null || unapproved.Count == 0)
            {
                return null;
            }

            string subject = unapproved.Count == 1
                ? "1 API permission this module asks for is not approved: "
                : unapproved.Count + " API permissions this module asks for are not approved: ";

            return subject + string.Join(", ", unapproved.ToArray())
                + ". Disable and re-enable the module in Blish's Manage Modules with those boxes ticked. "
                + "Until then the features that need them stay off.";
        }

        /// <summary>
        /// The same fact at status-line length. The names live in the log
        /// line <see cref="Compose"/> writes.
        /// </summary>
        public static string ComposeStatus(IReadOnlyList<string> unapproved)
        {
            if (unapproved == null || unapproved.Count == 0)
            {
                return null;
            }

            return unapproved.Count == 1
                ? "1 API permission not approved - see the Log tab"
                : unapproved.Count + " API permissions not approved - see the Log tab";
        }
    }
}
