using Blish_HUD.Controls;
using System.Runtime.CompilerServices;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// The right-click-to-wiki gesture, stamped onto an icon by
    /// <see cref="ItemIconTooltip.StampOnIconTree"/> as
    /// <see cref="IconControls"/> builds it, so no call site can forget.
    /// <para>
    /// EXACTLY ONE control per gesture, never an ancestor of another wired
    /// control. Blish's Container.TriggerMouseInput raises the container's
    /// OWN mouse event before walking its children, so a wired frame and
    /// its wired art square both fire on one click and open two tabs.
    /// Hovers resolve on the deepest control alone; clicks accumulate.
    /// </para>
    /// <para>
    /// GW2's own right-drag rotates the camera, so firing on button-DOWN
    /// yanks focus out of a fullscreen game with no way to abort. Release
    /// alone is no fix: Blish routes it to whatever the cursor is over
    /// then. Press arms a flag, this control's own release consumes it,
    /// and MouseLeft disarms it so a stale arm cannot be replayed.
    /// </para>
    /// </summary>
    internal static class IconWikiClick
    {
        /// <summary>
        /// The page one control opens, and whether a press has armed it.
        /// Held beside the control rather than captured in the handlers, so
        /// a re-stamp can retarget a control the tree reuses under a new
        /// subject - a Blish event handler cannot be removed once added.
        /// </summary>
        private sealed class Wiring
        {
            internal IconWikiTarget Target;

            internal bool Armed;
        }

        private static readonly ConditionalWeakTable<Control, Wiring> Wired =
            new ConditionalWeakTable<Control, Wiring>();

        internal static void ApplyToIcon(Control icon, IconWikiTarget target)
        {
            if (icon == null)
            {
                return;
            }

            if (Wired.TryGetValue(icon, out var wiring))
            {
                wiring.Target = target;
                wiring.Armed = false;
                return;
            }

            // Nothing is allocated for an icon with no page. The rich
            // tooltip surface rebuilds its own icons on every hover, so
            // wiring three handlers there would be garbage per hover for a
            // control the cursor can never click.
            if (!target.HasPage)
            {
                return;
            }

            wiring = new Wiring { Target = target };
            Wired.Add(icon, wiring);
            Wire(icon, wiring);
        }

        private static void Wire(Control control, Wiring wiring)
        {
            control.RightMouseButtonPressed += (_, __) => wiring.Armed = true;
            control.MouseLeft += (_, __) => wiring.Armed = false;
            control.RightMouseButtonReleased += (_, __) =>
            {
                if (!wiring.Armed)
                {
                    return;
                }

                wiring.Armed = false;

                // Built here rather than per icon: a plan tree draws
                // hundreds of icons and almost none are ever right-clicked,
                // and the launcher ignores a null url.
                WikiLinkLauncher.Open(wiring.Target.BuildUrl());
            };
        }
    }
}
