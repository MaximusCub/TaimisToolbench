#!/usr/bin/env python3
"""Emit the per-glyph metrics Blish HUD lays a Menomonia 14 label out with.

tools/dump-font-codepoints.py answers "can this codepoint draw at all".
This answers "how wide is this string", which is the other half of the same
question and the one a settings display name has to satisfy: Blish's own
Manage Modules panel gives a name a fixed pixel budget beside its control,
and a name past it is drawn over the control (docs/blish-settings-panel.md).

Menomonia 14 regular is Blish's Content.DefaultFont14, the face every
Blish_HUD.Controls.Label uses unless a caller overrides it, and the face
every setting row's name label therefore uses.

Usage:
    python3 tools/dump-font-metrics.py [FONT_DIR] > docs/menomonia-14-metrics.txt

FONT_DIR defaults to the standard Windows install path seen from WSL. Pass
the directory again after a Blish upgrade and commit the diff.

The XNB layout consumed below is documented in tools/dump-font-codepoints.py;
this script reads six more fields per region rather than just the character.
"""

import os
import struct
import sys

DEFAULT_FONT_DIR = "/mnt/c/Blish.HUD/Content/fonts/menomonia"
FONT_FILE = "menomonia-14-regular.xnb"


def read_7bit(data, index):
    value = 0
    shift = 0
    while True:
        byte = data[index]
        index += 1
        value |= (byte & 0x7F) << shift
        shift += 7
        if not byte & 0x80:
            return value, index


def read_string(data, index):
    length, index = read_7bit(data, index)
    return data[index:index + length].decode("utf-8"), index + length


def regions(path):
    data = open(path, "rb").read()
    if data[:4] != b"XNBw":
        raise ValueError("%s is not a Windows XNB" % path)

    index = 10
    readers, index = read_7bit(data, index)
    for _ in range(readers):
        name, index = read_string(data, index)
        index += 4
        if "BitmapFontReader" not in name:
            raise ValueError("%s is not a bitmap font" % path)

    _, index = read_7bit(data, index)  # shared-resource count
    _, index = read_7bit(data, index)  # type id of the root object

    pages, = struct.unpack_from("<i", data, index)
    index += 4
    for _ in range(pages):
        _, index = read_string(data, index)

    line_height, = struct.unpack_from("<i", data, index)
    index += 4
    count, = struct.unpack_from("<i", data, index)
    index += 4

    found = []
    for _ in range(count):
        fields = struct.unpack_from("<9i", data, index)
        index += 36
        character, _page, _x, _y, width, _height, x_offset, _y_offset, x_advance = fields
        found.append((character, width, x_offset, x_advance))
    return line_height, found


def main(argv):
    font_dir = argv[1] if len(argv) > 1 else DEFAULT_FONT_DIR
    path = os.path.join(font_dir, FONT_FILE)
    line_height, found = regions(path)

    sys.stdout.write(HEADER % (path, line_height, len(found)))
    for character, width, x_offset, x_advance in sorted(found):
        sys.stdout.write("%04X %d %d %d\n" % (character, width, x_offset, x_advance))
    return 0


HEADER = """\
# Menomonia 14 regular, per glyph, as Blish HUD measures it.
#
# GENERATED - do not hand-edit. Regenerate after a Blish upgrade with:
#   python3 tools/dump-font-metrics.py > docs/menomonia-14-metrics.txt
#
# Read from %s
#
# Line height %d. One glyph per line, space separated:
#   codepoint(hex) width xOffset xAdvance
#
# How a width is built from these, which is what
# MonoGame.Extended 3.8.0 BitmapFont.GetStringRectangle does and what
# Blish_HUD.Controls.LabelBase.RecalculateLayout then ceilings:
#
#   pen = 0; right = 0
#   for each glyph g in the string:
#       right = max(right, pen + g.xOffset + g.width)
#       pen  += g.xAdvance + letterSpacing
#   width = (int)right
#
# letterSpacing is -1 for every font Blish hands out - see
# Blish_HUD.ContentService.GetFont, which sets it on load. A codepoint with
# no line here has no region, so it draws nothing and advances nothing.
#
# %d glyphs.
"""


if __name__ == "__main__":
    sys.exit(main(sys.argv))
