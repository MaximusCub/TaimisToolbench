using Blish_HUD.Controls;
using System.Runtime.CompilerServices;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// The right-click-to-wiki gesture, wired onto an icon's whole control
    /// tree by <see cref="ItemIconTooltip.StampOnIconTree"/>. Building an
    /// icon through <see cref="IconControls"/> is what wires it, so no call
    /// site can draw one and forget.
    /// <para>
    /// GW2's own right-drag is the camera-rotate gesture, so firing on
    /// button-DOWN opens the browser and yanks focus out of a fullscreen
    /// game the instant a drag begun over the icon goes down, with no way
    /// to abort. Firing on release alone is not a fix either: Blish routes
    /// the release to whichever control is under the cursor at release
    /// time, so a drag started elsewhere would open THIS icon's page.
    /// Press arms a per-control flag, this control's own release consumes
    /// it, and MouseLeft disarms it so a stale arm cannot be replayed.
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

        internal static void ApplyToIconTree(Control control, IconWikiTarget target)
        {
            if (control == null)
            {
                return;
            }

            if (Wired.TryGetValue(control, out var wiring))
            {
                wiring.Target = target;
                wiring.Armed = false;
            }
            else if (target.HasPage)
            {
                // Nothing is allocated for an icon with no page. The rich
                // tooltip surface rebuilds its own icons on every hover, so
                // wiring three handlers there would be garbage per hover
                // for a control the cursor can never click.
                wiring = new Wiring { Target = target };
                Wired.Add(control, wiring);
                Wire(control, wiring);
            }

            if (control is Container container)
            {
                foreach (var child in container.Children)
                {
                    ApplyToIconTree(child, target);
                }
            }
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
