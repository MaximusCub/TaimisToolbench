using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// A tooltip's content as structure rather than as one flat string: a
    /// list of lines, each a run of spans that are either prose or a coin
    /// amount. The coin span is the whole reason this type exists - a coin
    /// amount rendered as "1g 23s 45c" text is the audit H6 complaint, and
    /// only a span that still knows its copper value can be drawn with the
    /// gold/silver/copper icons (icons RIGHT of their numbers, repo
    /// invariant) by the rich tooltip surface.
    ///
    /// Every span carries its own plain text as well as its structure. The
    /// rich surface draws that text; a coin span additionally keeps the
    /// copper value, so the surface can replace "1g 23s 45c" with icons.
    /// There is deliberately no plain-string projection on this type - the
    /// composers have one output shape, and the tests that want to assert on
    /// wording flatten it themselves (Tests/Helpers/TooltipContentPlainText).
    /// </summary>
    internal sealed class TooltipContent
    {
        public static readonly TooltipContent Empty = new TooltipContent(new List<TooltipLine>());

        private readonly IReadOnlyList<TooltipLine> _lines;

        private readonly TooltipContent _extra;

        internal TooltipContent(IReadOnlyList<TooltipLine> lines)
            : this(lines, null)
        {
        }

        private TooltipContent(IReadOnlyList<TooltipLine> lines, TooltipContent extra)
        {
            _lines = lines ?? new List<TooltipLine>();
            _extra = extra;
        }

        public IReadOnlyList<TooltipLine> Lines => _lines;

        public bool IsEmpty => _lines.Count == 0;

        /// <summary>
        /// What the module adds on top of the game's own content, drawn as
        /// a SECOND box under the first one rather than as more lines
        /// inside it. Null when this content is all one box.
        /// <para>
        /// A reader has to be able to tell what the item itself says from
        /// what this module is telling them, and a blank line inside one
        /// box does not carry that. The second box is a box, and the game
        /// itself stacks boxes this way for its equipped-item comparison.
        /// </para>
        /// </summary>
        public TooltipContent Extra => _extra;

        public bool HasExtra => _extra != null && !_extra.IsEmpty;

        /// <summary>
        /// The same first box with <paramref name="extra"/> as its second.
        /// Only content that has something in the first box may carry a
        /// second: an empty first box would render as an empty frame above
        /// the only lines there are.
        /// </summary>
        public TooltipContent WithExtra(TooltipContent extra)
        {
            if (IsEmpty || extra == null || extra.IsEmpty)
            {
                return this;
            }

            return new TooltipContent(_lines, extra);
        }

        /// <summary>
        /// Wraps a finished plain string (the shape most call sites still
        /// have) into single-text-span lines. Hard breaks become line
        /// boundaries; nothing is re-wrapped here.
        /// </summary>
        public static TooltipContent FromText(string text)
        {
            var builder = new TooltipContentBuilder();
            builder.Text(text);
            return builder.Build();
        }

        /// <summary>
        /// <paramref name="content"/>, or the plain
        /// <paramref name="fallbackText"/> when it has nothing to say.
        /// <para>
        /// The deferred rich path needs this because it cannot look before
        /// it leaps: registering a builder clears whatever plain tooltip
        /// the control already carried, and only when the builder finally
        /// runs is it known to produce nothing - by which time the note it
        /// replaced is gone. A control whose note is worth more than
        /// silence hands it in here.
        /// </para>
        /// </summary>
        public static TooltipContent OrText(TooltipContent content, string fallbackText)
        {
            if (content != null && !content.IsEmpty)
            {
                return content;
            }

            return string.IsNullOrEmpty(fallbackText) ? Empty : FromText(fallbackText);
        }

        /// <summary>
        /// For a composer that assembles its lines as a list it still needs
        /// to reorder (<c>TreeRowTooltipComposer</c> inserts the caption at
        /// the front after the fact) rather than streaming them into a
        /// <see cref="TooltipContentBuilder"/>.
        /// </summary>
        public static TooltipContent FromLines(IReadOnlyList<TooltipLine> lines)
        {
            return lines == null || lines.Count == 0 ? Empty : new TooltipContent(lines);
        }

        public static TooltipLine TextLine(string text)
        {
            return new TooltipLine(new List<TooltipSpan> { TooltipSpan.FromText(text ?? "") });
        }

        /// <summary>
        /// The icon+name row every in-game item tooltip opens with: a
        /// framed icon at <see cref="ItemIconTier.TooltipHeader"/> with the
        /// name set to its right and vertically centred on it
        /// (KNOWN-ISSUES #42, gap G11). A taller
        /// row than a prose one, and the only line kind that carries an
        /// icon - which is why it is a KIND rather than another span role.
        /// <para>
        /// A null url is normalised to empty here, so a header row ALWAYS
        /// has an icon to draw. The row reserves the name's indent whether
        /// or not one arrives; leaving the url null drew nothing into that
        /// reserved column and left the name floating over empty black
        /// while the body below it started at x=0.
        /// </para>
        /// </summary>
        public static TooltipLine HeaderLine(string iconUrl, string name, TooltipHeaderSubject subject)
        {
            // A currency has no rarity to colour by and does not fall back
            // to the unknown-rarity grey: the game gives it a colour of its
            // own (see TooltipSpanRole.CurrencyName).
            TooltipSpan nameSpan = subject.IsCurrency
                ? TooltipSpan.Styled(name ?? "", TooltipSpanRole.CurrencyName)
                : TooltipSpan.RarityText(name ?? "", subject.RarityKey);

            return new TooltipLine(
                new List<TooltipSpan> { nameSpan },
                TooltipLineKind.Header,
                iconUrl ?? "",
                subject);
        }

        public static TooltipLine Line(params TooltipSpan[] spans)
        {
            return new TooltipLine(spans ?? new TooltipSpan[0]);
        }
    }

    /// <summary>What a line IS, structurally. Prose unless stated.</summary>
    internal enum TooltipLineKind
    {
        /// <summary>An ordinary prose row, one line height tall.</summary>
        Text,

        /// <summary>The icon+name header row - see
        /// <see cref="TooltipContent.HeaderLine"/>.</summary>
        Header,

        /// <summary>
        /// A consumable effect-block row: one line pitch tall like prose,
        /// but indented past the inline effect icon the game draws beside
        /// the block ("Nourishment (45 m): ..." with the food icon at its
        /// left - measured on live3 soul-pastries/candy-corn/omnomberry,
        /// 2026-08-26). The icon rides the first row of the block only,
        /// via <see cref="TooltipLine.IconUrl"/>, and spans into the rows
        /// under it; every row of the block carries this kind so the
        /// indent covers the icon's full height.
        /// </summary>
        Effect,

        /// <summary>
        /// An equipment slot row ("Unused Upgrade Slot"), one line pitch
        /// tall, indented past the game's own slot glyph. The glyph is
        /// game UI art rather than a render-service icon, so it arrives as
        /// <see cref="TooltipLine.IconAssetId"/>, not
        /// <see cref="TooltipLine.IconUrl"/>. Its indent is its own, and
        /// narrower than an effect block's: measured on a live
        /// ascended-staff tooltip, where a slot glyph is 16px at the
        /// content edge with its text 21px in.
        /// </summary>
        Slot,
    }

    /// <summary>
    /// WHO a header row is about - what
    /// <see cref="TooltipContentBuilder.Header"/> takes in place of a bare
    /// rarity string, and what the rich surface reads to frame the header
    /// icon.
    /// <para>
    /// A rarity string cannot tell a CURRENCY, which has no rarity to look
    /// up, apart from an item nobody looked up: both arrive as null. The
    /// two want different frames - currency art is mostly transparent and
    /// a filled frame behind it shows through as a grey background, the
    /// defect on the Snapshot tab - so the call site
    /// has to say which it means, and a factory name is what a diff shows.
    /// </para>
    /// </summary>
    internal readonly struct TooltipHeaderSubject
    {
        private readonly string _rarityKey;
        private readonly bool _currency;

        private TooltipHeaderSubject(string rarityKey, bool currency)
        {
            _rarityKey = rarityKey;
            _currency = currency;
        }

        /// <summary>The rarity colouring the name, null for a currency and
        /// for an unknown rarity alike - both neutral, and neither is a
        /// guess.</summary>
        public string RarityKey => _rarityKey;

        /// <summary>Whether the subject has no rarity to have, rather than
        /// an unresolved one. Only <see cref="Currency"/> sets it.</summary>
        public bool IsCurrency => _currency;

        /// <summary>
        /// An item whose rarity has been RESOLVED - what
        /// <c>ItemRarityResolution.Resolve</c> returned after looking
        /// everywhere this surface has. Null from that policy is a
        /// legitimately unknown rarity; a caller that has not looked wants
        /// <see cref="ItemOfUnknownRarity"/> instead.
        /// </summary>
        public static TooltipHeaderSubject ItemOfRarity(string resolvedRarity)
        {
            return new TooltipHeaderSubject(resolvedRarity, false);
        }

        /// <summary>
        /// An item whose rarity this surface structurally cannot know,
        /// because its data source carries none. Renders the same neutral
        /// name as an unresolved rarity, and the call site is on record
        /// that the gap is in the DATA.
        /// </summary>
        public static TooltipHeaderSubject ItemOfUnknownRarity()
        {
            return new TooltipHeaderSubject(null, false);
        }

        /// <summary>
        /// A currency: there is no rarity to resolve, and the header icon
        /// takes the ring frame its transparent art needs.
        /// </summary>
        public static TooltipHeaderSubject Currency()
        {
            return new TooltipHeaderSubject(null, true);
        }
    }

    internal sealed class TooltipLine
    {
        private readonly IReadOnlyList<TooltipSpan> _spans;

        internal TooltipLine(
            IReadOnlyList<TooltipSpan> spans,
            TooltipLineKind kind = TooltipLineKind.Text,
            string iconUrl = null,
            TooltipHeaderSubject subject = default(TooltipHeaderSubject),
            int iconAssetId = 0)
        {
            _spans = spans ?? new List<TooltipSpan>();
            Kind = kind;
            IconUrl = iconUrl;
            HeaderSubject = subject;
            IconAssetId = iconAssetId;
        }

        public IReadOnlyList<TooltipSpan> Spans => _spans;

        public TooltipLineKind Kind { get; }

        /// <summary>
        /// Who a <see cref="TooltipLineKind.Header"/> row is about - what
        /// decides the frame drawn around <see cref="IconUrl"/>. Meaningless
        /// on every other kind, which draw no framed icon.
        /// </summary>
        public TooltipHeaderSubject HeaderSubject { get; }

        /// <summary>
        /// The item icon drawn at the head of a
        /// <see cref="TooltipLineKind.Header"/> row. Empty renders the
        /// module's neutral empty-slot square, never an error texture - a
        /// missing icon is a data gap, not a failure - and
        /// <see cref="TooltipContent.HeaderLine"/> normalises null to empty
        /// so the two can never diverge. Null means "this row draws no
        /// icon": the continuation rows of a wrapped header name, and every
        /// prose row.
        /// <para>
        /// On a <see cref="TooltipLineKind.Effect"/> row it is instead the
        /// effect's own inline icon, present on the block's first line
        /// only and never normalised: an effect block whose API details
        /// carry no icon is emitted as plain rows by the composer, so an
        /// Effect row always has a real URL here or null.
        /// </para>
        /// </summary>
        public string IconUrl { get; }

        /// <summary>
        /// The game's own UI art for a <see cref="TooltipLineKind.Slot"/>
        /// row, as a GW2 asset id; 0 on every other kind and on a wrapped
        /// slot line's continuation rows. Separate from
        /// <see cref="IconUrl"/> because the two resolve through different
        /// caches - a render-service URL against the item icon service, an
        /// asset id against the game's own UI art, the way the coin
        /// denominations already do.
        /// </summary>
        public int IconAssetId { get; }
    }

    /// <summary>
    /// What a span MEANS, not what colour it is. The rich surface resolves
    /// a role to a colour (<c>RichTooltipSurface.RenderRow</c>); this file
    /// - and every composer that builds content - stays XNA-free, which is
    /// what keeps composer tests Blish-free (repo invariant). Only
    /// <c>Views/Rendering/RarityColors</c> knows a
    /// <c>Microsoft.Xna.Framework.Color</c>.
    /// </summary>
    internal enum TooltipSpanRole
    {
        /// <summary>Ordinary tooltip prose.</summary>
        Default,

        /// <summary>An item name, coloured by the rarity carried on the
        /// span itself (<see cref="TooltipSpan.RarityKey"/>).</summary>
        Rarity,

        /// <summary>
        /// A wallet currency's name in a tooltip header. The game's warm
        /// tan rather than any rarity colour, measured (255,204,119) on a
        /// currency tooltip. NOT a generic heading colour: an item's name
        /// is measured at its rarity colour, white included for Basic, so
        /// only a subject with no rarity to have reaches this.
        /// </summary>
        CurrencyName,

        /// <summary>An upgrade's granted bonus - a rune bonus line, a
        /// sigil or infusion buff. NOT a food's nourishment line, which
        /// the one capture of one measures white (KNOWN-ISSUES #42).
        /// </summary>
        Bonus,

        /// <summary>
        /// A bonus tier the wearer has not reached, and every tier of a
        /// rune socketed in a stack whose wearer cannot be named - see
        /// <see cref="EquippedRuneSetIndex"/>. The grey is the game's own,
        /// confirmed in the client against a partly equipped rune set.
        /// </summary>
        BonusInactive,

        /// <summary>The item's <c>&lt;c=@flavor&gt;</c> prose.</summary>
        Flavor,

        /// <summary>The item's <c>&lt;c=@abilitytype&gt;</c> lead-in
        /// ("Element: ").</summary>
        AbilityType,

        /// <summary>The item's <c>&lt;c=@warning&gt;</c> run.</summary>
        Warning,

        /// <summary>
        /// The item's <c>&lt;c=@reminder&gt;</c> run. Its own role rather
        /// than a second user of <see cref="Muted"/>: the two greys have
        /// different sources and differ by 25 levels per channel (spec
        /// section 1.4 - reminder `#afafaf`, inferred from gw2efficiency;
        /// the annotation grey `#939496`, measured on xyaren.png).
        /// </summary>
        Reminder,

        /// <summary>
        /// A genuine secondary annotation - the game's own grey, e.g.
        /// "0/500 in Material Storage". NOT the identity block, which the
        /// game renders white (KNOWN-ISSUES #42, gap G4).
        /// </summary>
        Muted,
    }

    /// <summary>
    /// Prose, or a coin amount that still knows its copper value.
    /// <see cref="Text"/> is populated in both cases: for a coin span it is
    /// the caller's own plain rendering, used by the plain tooltip path and
    /// as the width fallback nowhere else.
    /// </summary>
    internal readonly struct TooltipSpan
    {
        private TooltipSpan(string text, long coinCopper, bool isCoin, TooltipSpanRole role, string rarityKey)
        {
            Text = text ?? "";
            CoinCopper = coinCopper;
            IsCoin = isCoin;
            Role = role;
            RarityKey = rarityKey;
        }

        public string Text { get; }

        public long CoinCopper { get; }

        public bool IsCoin { get; }

        public TooltipSpanRole Role { get; }

        /// <summary>
        /// GW2 API rarity string, meaningful only when
        /// <see cref="Role"/> is <see cref="TooltipSpanRole.Rarity"/>. A
        /// rarity STRING rather than a colour, for the reason on
        /// <see cref="TooltipSpanRole"/>; null/unknown renders the same
        /// neutral grey RarityColors already falls back to.
        /// </summary>
        public string RarityKey { get; }

        public static TooltipSpan FromText(string text)
        {
            return new TooltipSpan(text, 0, false, TooltipSpanRole.Default, null);
        }

        public static TooltipSpan Styled(string text, TooltipSpanRole role)
        {
            return new TooltipSpan(text, 0, false, role, null);
        }

        public static TooltipSpan RarityText(string text, string rarity)
        {
            return new TooltipSpan(text, 0, false, TooltipSpanRole.Rarity, rarity);
        }

        public static TooltipSpan FromCoin(long copper, string plainText)
        {
            return new TooltipSpan(plainText, copper, true, TooltipSpanRole.Default, null);
        }

        /// <summary>
        /// The same span carrying different text - how the wrapper splits a
        /// prose span into rows without losing its role. Re-creating the
        /// piece with <see cref="FromText"/> instead would silently reset
        /// every wrapped line to <see cref="TooltipSpanRole.Default"/>,
        /// i.e. a long item name would lose its rarity colour the moment it
        /// wrapped.
        /// </summary>
        internal TooltipSpan WithText(string text)
        {
            return new TooltipSpan(text, CoinCopper, IsCoin, Role, RarityKey);
        }
    }

    /// <summary>
    /// Accumulates spans into lines. Composers build with this; the
    /// tooltip facility composes several composers' results with
    /// <see cref="Append"/> and <see cref="Separator"/> instead of the
    /// "\n\n" string concatenation the pill tooltip used to do.
    /// </summary>
    internal sealed class TooltipContentBuilder
    {
        private readonly List<TooltipLine> _lines = new List<TooltipLine>();
        private List<TooltipSpan> _current;

        public bool IsEmpty => _lines.Count == 0 && _current == null;

        /// <summary>
        /// Appends prose to the current line. An embedded hard break ends
        /// the line, so a composer can keep handing over the multi-line
        /// strings it already builds.
        /// </summary>
        public TooltipContentBuilder Text(string text)
        {
            return AppendText(text, TooltipSpan.FromText(""));
        }

        /// <summary>Prose that means something (see <see cref="TooltipSpanRole"/>).</summary>
        public TooltipContentBuilder Styled(string text, TooltipSpanRole role)
        {
            return AppendText(text, TooltipSpan.Styled("", role));
        }

        /// <summary>An item name coloured by its GW2 rarity string.</summary>
        public TooltipContentBuilder RarityText(string text, string rarity)
        {
            return AppendText(text, TooltipSpan.RarityText("", rarity));
        }

        /// <summary>
        /// The icon+name header row - see
        /// <see cref="TooltipContent.HeaderLine"/>. Commits whatever line
        /// was open first: a header is a whole row, never a run inside one.
        /// </summary>
        public TooltipContentBuilder Header(string iconUrl, string name, TooltipHeaderSubject subject)
        {
            if (_current != null)
            {
                EndLine();
            }

            _lines.Add(TooltipContent.HeaderLine(iconUrl, name, subject));
            return this;
        }

        /// <summary>
        /// A consumable's effect block: <paramref name="text"/> split on
        /// hard breaks into <see cref="TooltipLineKind.Effect"/> lines,
        /// every span carrying <paramref name="role"/>, with
        /// <paramref name="iconUrl"/> on the FIRST line only - the shape
        /// the game draws for "Nourishment (45 m): ..." (live3, 2026-08-26).
        /// Callers with no icon URL emit ordinary text instead; see
        /// <see cref="TooltipLine.IconUrl"/>.
        /// </summary>
        public TooltipContentBuilder EffectBlock(string iconUrl, string text, TooltipSpanRole role)
        {
            if (string.IsNullOrEmpty(text))
            {
                return this;
            }

            if (_current != null)
            {
                EndLine();
            }

            string normalized = text.IndexOf('\r') >= 0
                ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;
            bool first = true;
            foreach (var piece in normalized.Split('\n'))
            {
                if (piece.Length == 0)
                {
                    continue;
                }

                _lines.Add(new TooltipLine(
                    new List<TooltipSpan> { TooltipSpan.Styled(piece, role) },
                    TooltipLineKind.Effect,
                    first ? iconUrl : null));
                first = false;
            }

            return this;
        }

        /// <summary>
        /// One equipment slot row: <paramref name="text"/> beside the
        /// game's own slot glyph, named by
        /// <paramref name="iconAssetId"/>. See
        /// <see cref="TooltipLineKind.Slot"/>.
        /// </summary>
        public TooltipContentBuilder SlotLine(int iconAssetId, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return this;
            }

            if (_current != null)
            {
                EndLine();
            }

            _lines.Add(new TooltipLine(
                new List<TooltipSpan> { TooltipSpan.FromText(text) },
                TooltipLineKind.Slot,
                null,
                default(TooltipHeaderSubject),
                iconAssetId));
            return this;
        }

        // template carries the role/rarity every piece of this text
        // inherits; only its text differs per hard break.
        private TooltipContentBuilder AppendText(string text, TooltipSpan template)
        {
            if (string.IsNullOrEmpty(text))
            {
                return this;
            }

            string normalized = text.IndexOf('\r') >= 0 ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;
            int start = 0;
            while (true)
            {
                int brk = normalized.IndexOf('\n', start);
                string piece = brk < 0 ? normalized.Substring(start) : normalized.Substring(start, brk - start);
                if (piece.Length > 0)
                {
                    Current().Add(template.WithText(piece));
                }

                if (brk < 0)
                {
                    return this;
                }

                EndLine();
                start = brk + 1;
            }
        }

        public TooltipContentBuilder Coin(long copper, string plainText)
        {
            Current().Add(TooltipSpan.FromCoin(copper, plainText));
            return this;
        }

        /// <summary>
        /// Commits the current line, blank line included - a builder with a
        /// started-but-empty line still owes that blank row, which is how
        /// the composers' deliberate separator lines survive.
        /// </summary>
        public TooltipContentBuilder EndLine()
        {
            _lines.Add(new TooltipLine(_current ?? new List<TooltipSpan>()));
            _current = null;
            return this;
        }

        /// <summary>
        /// The blank line between two composed blocks - the structural
        /// replacement for the "\n\n" the pill tooltip concatenated. A no-op
        /// on an empty builder, so a block that turns out to be first never
        /// opens with a stray blank row.
        /// </summary>
        public TooltipContentBuilder Separator()
        {
            if (IsEmpty)
            {
                return this;
            }

            if (_current != null)
            {
                EndLine();
            }

            _lines.Add(new TooltipLine(new List<TooltipSpan>()));
            return this;
        }

        /// <summary>
        /// Appends another block's lines into the box being built. A
        /// builder has one box, so content carrying a
        /// <see cref="TooltipContent.Extra"/> has nowhere to put its
        /// second one and is rejected rather than silently flattened. Only
        /// Services.ItemRowTooltipComposer builds two-box content, and it
        /// does so as its last step; a caller that needs to append such
        /// content must append the two boxes separately and re-attach the
        /// second with <see cref="TooltipContent.WithExtra"/>.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="other"/> carries a second box.
        /// </exception>
        public TooltipContentBuilder Append(TooltipContent other)
        {
            if (other == null || other.IsEmpty)
            {
                return this;
            }

            if (other.HasExtra)
            {
                throw new ArgumentException(
                    "Content with a second box cannot be appended into one box.",
                    nameof(other));
            }

            if (_current != null)
            {
                EndLine();
            }

            _lines.AddRange(other.Lines);
            return this;
        }

        public TooltipContent Build()
        {
            if (_current != null)
            {
                EndLine();
            }

            return _lines.Count == 0 ? TooltipContent.Empty : new TooltipContent(_lines);
        }

        private List<TooltipSpan> Current()
        {
            return _current ?? (_current = new List<TooltipSpan>());
        }
    }
}
