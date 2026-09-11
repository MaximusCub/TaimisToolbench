using Blish_HUD.Content;
using Microsoft.Xna.Framework;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// The GW2 window art every window of this module is drawn from, and
    /// the two texture-space regions Blish reads it through.
    /// <para>
    /// Blish scales the background so the window region maps onto the
    /// control's own bounds, so one pair of regions dresses a 1378px module
    /// window and a small popout alike. Both are named in
    /// <see cref="WindowSizing"/>, which is where the chrome they imply is
    /// already accounted for.
    /// </para>
    /// </summary>
    internal static class ModuleWindowArt
    {
        internal static AsyncTexture2D Background()
        {
            return AsyncTexture2D.FromAssetId(WindowSizing.WindowBackgroundAssetId);
        }

        internal static Rectangle WindowRegion()
        {
            return new Rectangle(
                WindowSizing.WindowRegionLeft,
                WindowSizing.WindowRegionTop,
                WindowSizing.WindowRegionWidth,
                WindowSizing.WindowRegionHeight);
        }

        internal static Rectangle ContentRegion()
        {
            return new Rectangle(
                WindowSizing.WindowContentRegionLeft,
                WindowSizing.WindowContentRegionTop,
                WindowSizing.WindowContentRegionWidth,
                WindowSizing.WindowContentRegionHeight);
        }
    }
}
