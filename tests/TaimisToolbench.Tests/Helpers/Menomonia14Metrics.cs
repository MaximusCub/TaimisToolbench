using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TaimisToolbench.Tests.Helpers
{
    /// <summary>
    /// Measures a string the way Blish HUD measures a label, from the real
    /// metrics of the shipped Menomonia 14 face.
    /// <para>
    /// Reads docs/menomonia-14-metrics.txt, which tools/dump-font-metrics.py
    /// writes straight out of the installed font. The arithmetic is
    /// MonoGame.Extended 3.8.0 BitmapFont.GetStringRectangle followed by
    /// Blish_HUD.Controls.LabelBase.RecalculateLayout, both spelled out in
    /// docs/blish-settings-panel.md. No graphics device is involved, so this
    /// stays Blish-free like every other test in this project.
    /// </para>
    /// </summary>
    internal sealed class Menomonia14Metrics
    {
        /// <summary>
        /// Blish_HUD.ContentService.GetFont sets this on every font it hands
        /// out, so it applies to every label the overlay draws.
        /// </summary>
        private const int LetterSpacing = -1;

        private readonly Dictionary<int, Glyph> _glyphs;

        private Menomonia14Metrics(Dictionary<int, Glyph> glyphs)
        {
            _glyphs = glyphs;
        }

        private readonly struct Glyph
        {
            public Glyph(int width, int xOffset, int xAdvance)
            {
                Width = width;
                XOffset = xOffset;
                XAdvance = xAdvance;
            }

            public int Width { get; }

            public int XOffset { get; }

            public int XAdvance { get; }
        }

        public static Menomonia14Metrics Load()
        {
            string path = RepoFileLocator.FindRepoFile(Path.Combine("docs", "menomonia-14-metrics.txt"));
            if (path == null)
            {
                throw new FileNotFoundException(
                    "docs/menomonia-14-metrics.txt was not found by walking up from the test output directory.");
            }

            var glyphs = new Dictionary<int, Glyph>();
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] fields = line.Split(' ');
                if (fields.Length != 4)
                {
                    throw new FormatException("Malformed metrics line: " + rawLine);
                }

                int codepoint = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                glyphs[codepoint] = new Glyph(
                    int.Parse(fields[1], CultureInfo.InvariantCulture),
                    int.Parse(fields[2], CultureInfo.InvariantCulture),
                    int.Parse(fields[3], CultureInfo.InvariantCulture));
            }

            if (glyphs.Count == 0)
            {
                throw new FormatException("docs/menomonia-14-metrics.txt carried no glyphs.");
            }

            return new Menomonia14Metrics(glyphs);
        }

        /// <summary>
        /// The width in pixels of a Blish Label with AutoSizeWidth set, which
        /// is what every setting name in Blish's own panel is.
        /// </summary>
        public int MeasureLabelWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int pen = 0;
            int right = 0;
            foreach (char character in text)
            {
                if (!_glyphs.TryGetValue(character, out Glyph glyph))
                {
                    continue;
                }

                right = Math.Max(right, pen + glyph.XOffset + glyph.Width);
                pen += glyph.XAdvance + LetterSpacing;
            }

            return right;
        }

        /// <summary>
        /// True when the face carries a region for every character, so the
        /// text both draws and advances. A missing one does neither, which
        /// is invisible on screen and to MeasureLabelWidth alike.
        /// </summary>
        public bool CanDraw(string text)
        {
            foreach (char character in text ?? string.Empty)
            {
                if (!_glyphs.ContainsKey(character))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
