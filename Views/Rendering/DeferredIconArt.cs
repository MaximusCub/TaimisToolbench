using System;
using Blish_HUD;
using Blish_HUD.Controls;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// An icon square whose picture has not been asked for yet, and the one
    /// call that asks for it. Built only by
    /// <see cref="IconControls.CreateItemIconDeferredArt"/>.
    /// <para>
    /// It exists because asking Blish for a picture is not free even when
    /// the picture is already on disk. Blish 1.3.0's
    /// <c>ContentService.GetRenderServiceTexture</c> forwards to
    /// <c>DatAssetCache.GetTextureFromAssetId</c>, which runs a
    /// <c>Directory.CreateDirectory</c> and a <c>File.Exists</c> on the
    /// CALLING thread before any of its work goes async, then decodes the
    /// image under one process-wide graphics device lock taken at low
    /// priority. A surface that builds a thousand icons in one pass pays
    /// both a thousand times, on the frame it builds them.
    /// </para>
    /// <para>
    /// The square is built either way, so nothing about the control tree or
    /// the icon's hover changes. Only the picture waits. Until it lands the
    /// square draws its rarity frame with nothing in it, which is the same
    /// thing it drew while a request was in flight.
    /// </para>
    /// </summary>
    internal sealed class DeferredIconArt
    {
        private Panel _square;
        private readonly string _iconUrl;

        /// <summary><paramref name="square"/> is null for an entry with no
        /// icon url at all, which draws the empty-slot placeholder and has
        /// no picture to wait for.</summary>
        internal DeferredIconArt(Panel square, string iconUrl)
        {
            _square = square;
            _iconUrl = iconUrl;
        }

        /// <summary>True until <see cref="Load"/> has run once.</summary>
        internal bool Pending
        {
            get { return _square != null; }
        }

        /// <summary>
        /// Asks Blish for the picture and hands it to the square. Does
        /// nothing on a second call, and nothing once the row has been
        /// disposed.
        /// </summary>
        internal void Load()
        {
            var square = _square;
            if (square == null)
            {
                return;
            }

            // Cleared before the request, not after: a url Blish cannot
            // parse throws, and a per-frame caller would otherwise ask again
            // every frame for the rest of the session.
            _square = null;

            if (square.Parent == null)
            {
                return;
            }

            try
            {
                square.BackgroundTexture = GameService.Content.GetRenderServiceTexture(_iconUrl);
            }
            catch (Exception ex)
            {
                // At most one line per icon, ever, because the square is
                // already cleared above. GetRenderServiceTexture throws on a
                // url with no signature/file-id pair in it; the frame simply
                // stays empty, which is what a missing picture looks like
                // anyway.
                ModuleLog.Shared.Write(
                    ModuleLogLevel.Debug, "ui",
                    $"Icon art request failed: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }
}
