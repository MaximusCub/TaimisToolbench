using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Modules;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// The About tab: static, mostly-derived information about the module
    /// itself - name, version, author/contributors, source URL, the Blish
    /// HUD version it targets, this repo's own license, and the module's
    /// data directory (which a user needs when attaching
    /// snapshot.json/status.json to a bug report).
    /// <para>
    /// Manifest fields are read live from ModuleParameters.Manifest, with a
    /// hand-parse of the packaged manifest.json as the fallback. Version
    /// and a dependency's VersionRange are typed SemVer.Version/SemVer.Range
    /// from a package Blish embeds via Costura and this project has no
    /// compile-time reference to, so those two are read by reflection
    /// (ToString() only) - a direct property access will not compile.
    /// </para>
    /// Why the fallback exists and why the tab rebuilds per visit:
    /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
    /// </summary>
    internal class AboutTabContent
    {
        private static readonly Logger Logger = Logger.GetLogger<AboutTabContent>();

        private const string ModuleDisplayName = "Taimi's Toolbench";
        // The ONE phrasing for a value the module could not resolve.
        // Three lived here - "unknown" for a version, "Not set in
        // manifest.json" for the source URL, "Not listed in manifest.json"
        // for the author - so one screen answered the same question three
        // ways, and two of the three named an implementation detail the
        // reader has no way to act on.
        private const string NotAvailableText = "Not available";
        private const string BlishHudDependencyNamespace = "bh.blishhud";

        // The GW2/ArenaNet fan-content disclaimer. This exact wording is
        // approved - ship the literal string as-is, do not derive or
        // reword it.
        private const string ArenaNetDisclaimerText =
            "Taimi's Toolbench is a fan-made tool and is not affiliated with, endorsed by, or supported by ArenaNet or NCSOFT. Guild Wars 2 and all associated trademarks are the property of NCSOFT Corporation. All game data comes from the official Guild Wars 2 API.";

        // Manual fallback for the "Built with Blish HUD" note, only ever
        // shown if BOTH the live Dependencies read (ReadBlishHudDependencyRange)
        // and the manifest.json fallback read fail to produce a value -
        // mirrors manifest.json's own currently-declared
        // dependencies.bh.blishhud value (d1 Feature 2, option (a): a
        // doc-only literal, same maintenance cost as any other
        // rarely-changing constant).
        private const string FallbackBlishHudVersionRange = ">=1.3.0";

        private static readonly Color InfoTextColor = new Color(170, 170, 170);

        // Every horizontal constant on this tab comes from AboutLayoutMath,
        // which derives them from the plan tables' own pinned-right-edge
        // rule and from the reading measure it declares.
        private const int Inset = AboutLayoutMath.AboutInset;
        private const int RowHeight = 30;
        private const int RowLabelY = 7;
        private const int RowInputY = 3;
        private const int InputHeight = 26;

        // 22, not 20: a wrapped line sits at y=2 and its lowest Font16 ink
        // is y=23.
        private const int ProseLineHeight = 22;

        // The distance a Label puts between successive lines of one
        // wrapped paragraph. ProseLineHeight above is the BOX such a
        // paragraph is given, which is two pixels looser - so a block drawn
        // line by line has to use this, or its leading would not match the
        // paragraph beside it.
        private static readonly int ProseLinePitch = TypeRampMetrics.BodyInk.LineHeight;

        /// <summary>Gap between one paragraph of a block and the next -
        /// half a line, so the break reads without opening a hole.</summary>
        private static readonly int ProseParagraphGap = ProseLinePitch / 2;

        /// <summary>Gap between one block on the board and the next.</summary>
        private const int BlockGap = AboutLayoutMath.SectionGap;

        // The ramp's section-title band, named once in PlanContentHeightMath
        // and aliased here rather than re-derived.
        private const int SectionHeaderRowHeight = PlanContentHeightMath.SectionHeaderRowHeight;
        private const int SectionHeaderTitleY = PlanContentHeightMath.SectionHeaderTitleY;

        // The identity row is a section-title band, not a Display one:
        // UiFonts.Display resolves to Blish's DefaultFont32, the exact face
        // and size WindowBase2.PaintTitleText prints the window title in
        // some 50px above this row, and the string is the same module name.
        private const int HeaderRowHeight = SectionHeaderRowHeight;
        private const int HeaderTitleY = SectionHeaderTitleY;
        private const int IconSize = 32;
        private const int IconToNameGap = 10;
        private const int NameToVersionGap = 8;

        private static readonly Color SectionDividerColor = new Color(130, 130, 130);

        private readonly ModuleParameters _moduleParameters;
        private readonly string _dataDirectoryPath;
        private readonly Texture2D _moduleIconTexture;

        private FlowPanel _rootPanel;

        // One absolutely-placed panel inside the scroller: two columns
        // cannot be expressed by a top-to-bottom FlowPanel, and every block
        // on it has to be re-placed when the width changes.
        private Panel _documentPanel;

        private Panel _headerPanel;
        private Image _iconImage;
        private Label _nameLabel;
        private Label _versionLabel;
        private Panel _headerRule;

        /// <summary>One label/value row of the identity card.</summary>
        private sealed class FactRow
        {
            public Panel Panel;
            public Label LabelControl;
            public string LabelText;
            public Label ValueLabel;
            public string ValueText;
            public TextBox ValueBox;

            /// <summary>The runs a linked value draws, and the panel they
            /// are rebuilt into when the column width moves.</summary>
            public IReadOnlyList<PlanNoteSegment> ValueSegments;
            public Panel ValueRuns;
        }

        /// <summary>A titled block of prose: the 38px band every other
        /// SectionTitle in the module draws, its 2px rule, and one wrapped
        /// paragraph.</summary>
        private sealed class ProseBlock
        {
            public Panel Panel;
            public Label TitleLabel;
            public Panel Rule;
            public Label Body;
            public string BodyText;

            /// <summary>A block that belongs to the section above it: a
            /// 20pt heading, no rule, and the narrower gap above.</summary>
            public bool IsSubsection;

            /// <summary>A block whose words carry links is drawn as runs
            /// rather than as one Label - see CreateLinkedProseBlock.</summary>
            public IReadOnlyList<IReadOnlyList<PlanNoteSegment>> Paragraphs;
            public Panel BodyHost;
        }

        private readonly List<FactRow> _factRows = new List<FactRow>();
        private readonly List<ProseBlock> _proseBlocks = new List<ProseBlock>();
        private ProseBlock _factsBlock;
        private Label _descriptionLabel;
        private string _descriptionText = "";

        // Width the blocks below are currently placed at. Survives Build,
        // which is why Build lays out through ApplyLayout rather than the
        // guarded Relayout - see Relayout.
        private int _panelWidth;

        // Holds the wrap/ellipsize half of a resize until the drag stops -
        // see Relayout.
        private readonly ResizeSettleDebounce _resizeSettle;

        // False while Build is midway through replacing the blocks below.
        // Module keeps ONE AboutTabContent and Blish re-runs Build on it at
        // every tab open, off the UI thread, while the settle callback is
        // marshalled onto the main thread - so without this, opening the
        // tab inside a settle window would run ApplyLayout against blocks
        // Build has just nulled. Volatile so the reader that sees true also
        // sees the finished blocks; same gate SettingsTabContent uses, for
        // the same reason.
        private volatile bool _buildComplete;

        /// <summary>
        /// Clears the built flag on the MAIN thread, before Blish queues
        /// the off-thread Build. Clearing it inside Build leaves the flag
        /// reading true for the whole interval between the tab switch and
        /// Build's first statement, and a settle callback landing in that
        /// window dereferences the blocks Build is about to replace.
        /// Mirrors SettingsTabContent.BeginRebuild.
        /// </summary>
        public void BeginRebuild()
        {
            _buildComplete = false;
        }

        public AboutTabContent(ModuleParameters moduleParameters, string dataDirectoryPath, Texture2D moduleIconTexture)
        {
            _moduleParameters = moduleParameters ?? throw new ArgumentNullException(nameof(moduleParameters));
            _dataDirectoryPath = dataDirectoryPath ?? "";
            _moduleIconTexture = moduleIconTexture;

            _resizeSettle = new ResizeSettleDebounce(
                RefitTextAfterResizeSettle,
                MainThreadMarshal.Run,
                ResizeSettleDebounce.DefaultSettleMs,
                ex =>
                {
                    Logger.Warn(ex, "About text re-fit wait failed");
                    ModuleLog.Shared.Write(ModuleLogLevel.Warn, "about",
                        $"About text re-fit wait failed: {ex.GetType().Name} - {ex.Message}");
                });
        }

        public void Build(Container container)
        {
            _buildComplete = false;

            var info = LoadAboutInfo();

            _factRows.Clear();
            _proseBlocks.Clear();
            _descriptionLabel = null;
            _factsBlock = null;

            _rootPanel = new FlowPanel()
            {
                Size = new Point(container.ContentRegion.Width, container.ContentRegion.Height),
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                CanScroll = true,
                Parent = container,
            };

            _documentPanel = new Panel()
            {
                Size = new Point(ContentWidth(container), 0),
                Parent = _rootPanel,
            };

            BuildHeader(info);

            _descriptionText = info.Description ?? "";
            if (!string.IsNullOrWhiteSpace(_descriptionText))
            {
                _descriptionLabel = CreateProseLabel(_documentPanel);
            }

            _factsBlock = CreateProseBlock("Module", null);

            // Trailing colons dropped from all five: inside a two-column
            // table with a rule, a colon on every label is punctuation doing
            // a column's job.
            AddLinkedFactRow(
                AboutLayoutMath.SourceLabel,
                string.IsNullOrWhiteSpace(info.Url) ? NotAvailableText : info.Url,
                AboutTabText.SourceValue(info.Url, NotAvailableText));
            AddFactRow(AboutLayoutMath.AuthorLabel, info.AuthorDisplay ?? NotAvailableText);

            string blishRange = info.BlishVersionRange ?? FallbackBlishHudVersionRange;
            AddLinkedFactRow(
                AboutLayoutMath.BuiltWithLabel,
                $"Blish HUD {blishRange} ({AboutTabText.SourceLinkWord})",
                AboutTabText.BuiltWithValue(blishRange));

            AddLinkedFactRow(
                AboutLayoutMath.LicenseLabel,
                AboutTabText.LicenseName,
                AboutTabText.LicenseValue());
            AddCopyableFactRow(
                AboutLayoutMath.DataDirectoryLabel,
                string.IsNullOrWhiteSpace(_dataDirectoryPath) ? NotAvailableText : _dataDirectoryPath);

            CreateProseBlock("Disclaimer", ArenaNetDisclaimerText);

            // Credits is a heading over the three subsections below it and
            // carries no body of its own, so it is placed in the flow here
            // rather than by CreateProseBlock's body branch.
            _proseBlocks.Add(CreateProseBlock(AboutTabText.CreditsSectionTitle, null));
            CreateLinkedProseBlock(
                AboutTabText.Gw2EfficiencySubheading, AboutTabText.CreditParagraphs(),
                subsection: true);
            CreateLinkedProseBlock(
                AboutTabText.BlishHudSubheading, AboutTabText.BlishHudParagraphs(),
                subsection: true);
            CreateLinkedProseBlock(
                AboutTabText.OpenSourceSubheading, AboutTabText.OpenSourceParagraphs(),
                subsection: true);

            ApplyLayout(ContentWidth(container), measureText: true);

            // The tab used to resize its root panel and nothing else, so a
            // window widened after the tab was opened left the prose wrapped
            // at whatever width it opened at, permanently. Both paths go
            // through Relayout so they cannot drift.
            container.Resized += (_, __) =>
            {
                _rootPanel.Size = new Point(
                    container.ContentRegion.Width,
                    container.ContentRegion.Height);
                Relayout(ContentWidth(container));
            };

            _buildComplete = true;
        }

        /// <summary>
        /// Releases what outlives this tab's control tree. Called from
        /// Module.Unload; safe when the tab was never opened, and safe
        /// twice. Mirrors SettingsTabContent.Teardown.
        /// </summary>
        public void Teardown()
        {
            _resizeSettle.Cancel();
        }

        private static int ContentWidth(Container container)
        {
            int width = container.ContentRegion.Width - WindowSizing.ScrollbarAllowance;
            return width > 0 ? width : 0;
        }

        /// <summary>
        /// The plan header's own pair, reused verbatim: the module name at
        /// Display 32 with its version at SmallHeading 20 regular beside it,
        /// baseline-aligned. That pair is what SmallHeading exists for; the
        /// tab's title was sitting one tier low at SectionTitle.
        /// </summary>
        private void BuildHeader(AboutInfo info)
        {
            _headerPanel = new Panel()
            {
                Size = new Point(AboutLayoutMath.FactsMinWidth, HeaderRowHeight),
                Parent = _documentPanel,
            };

            if (_moduleIconTexture != null)
            {
                // Unframed on purpose: this is a logo, not an item, and the
                // framed item-icon path is for items.
                _iconImage = new Image()
                {
                    Texture = new AsyncTexture2D(_moduleIconTexture),
                    Size = new Point(IconSize, IconSize),
                    Location = new Point(Inset, (HeaderRowHeight - IconSize) / 2),
                    Parent = _headerPanel,
                };
            }

            _nameLabel = new Label()
            {
                Text = info.Name,
                Font = UiFonts.SectionTitle,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(Inset, HeaderTitleY),
                Parent = _headerPanel,
            };

            _versionLabel = new Label()
            {
                Text = "v" + info.Version,
                Font = UiFonts.SmallHeading,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                TextColor = InfoTextColor,
                Location = new Point(Inset, HeaderTitleY),
                Parent = _headerPanel,
            };

            _headerRule = new Panel()
            {
                Size = new Point(AboutLayoutMath.FactsMinWidth, 2),
                Location = new Point(0, HeaderRowHeight - 3),
                BackgroundColor = SectionDividerColor,
                Parent = _headerPanel,
            };
        }

        private static int BandHeight(bool subsection)
        {
            return subsection ? AboutLayoutMath.SubheadingBandHeight : SectionHeaderRowHeight;
        }

        private ProseBlock CreateProseBlock(string title, string body, bool subsection = false)
        {
            int band = BandHeight(subsection);
            var block = new ProseBlock
            {
                BodyText = body,
                IsSubsection = subsection,
                Panel = new Panel()
                {
                    Size = new Point(AboutLayoutMath.FactsMinWidth, band),
                    Parent = _documentPanel,
                },
            };

            block.TitleLabel = new Label()
            {
                Text = title,
                Font = subsection ? UiFonts.ColumnHeader : UiFonts.SectionTitle,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(
                    Inset,
                    subsection ? AboutLayoutMath.SubheadingTitleY : SectionHeaderTitleY),
                Parent = block.Panel,
            };

            // These two headings were the only SectionTitle bands in the
            // module drawing no rule. A subheading draws none either way:
            // the rule is what marks a top-level section, so one under each
            // subheading would make three peers of what has to read as one.
            if (!subsection)
            {
                block.Rule = new Panel()
                {
                    Size = new Point(AboutLayoutMath.FactsMinWidth, 2),
                    Location = new Point(0, SectionHeaderRowHeight - 3),
                    BackgroundColor = SectionDividerColor,
                    Parent = block.Panel,
                };
            }

            if (body != null)
            {
                block.Body = CreateProseLabel(block.Panel);
                _proseBlocks.Add(block);
            }

            return block;
        }

        /// <summary>
        /// A prose block whose words carry links. Its body is a panel of
        /// per-run labels rather than one Label, because Blish's Label
        /// draws one string in one colour with no underline.
        /// </summary>
        private ProseBlock CreateLinkedProseBlock(
            string title, IReadOnlyList<IReadOnlyList<PlanNoteSegment>> paragraphs,
            bool subsection = false)
        {
            var block = CreateProseBlock(title, null, subsection);
            block.Paragraphs = paragraphs;
            block.BodyHost = new Panel()
            {
                Size = new Point(AboutLayoutMath.FactsMinWidth, 0),
                Parent = block.Panel,
            };

            _proseBlocks.Add(block);
            return block;
        }

        private static Label CreateProseLabel(Panel parent)
        {
            return new Label()
            {
                Font = UiFonts.Body,
                Text = "",
                AutoSizeWidth = false,
                AutoSizeHeight = false,
                TextColor = InfoTextColor,
                Parent = parent,
            };
        }

        /// <summary>
        /// The shell of one fact row - its panel and its label - with the
        /// value left to the caller, which is the only part that differs
        /// between a plain, a copyable and a linked value.
        /// </summary>
        private FactRow CreateFactRow(string label, string value)
        {
            var row = new FactRow
            {
                LabelText = label,
                ValueText = value,
                Panel = new Panel()
                {
                    Size = new Point(AboutLayoutMath.FactsMinWidth, RowHeight),
                    Parent = _documentPanel,
                },
            };

            row.LabelControl = new Label()
            {
                Font = UiFonts.Body,
                Text = label,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(Inset, RowLabelY),
                Parent = row.Panel,
            };

            _factRows.Add(row);
            return row;
        }

        private void AddFactRow(string label, string value)
        {
            var row = CreateFactRow(label, value);
            row.ValueLabel = new Label()
            {
                Font = UiFonts.Body,
                Text = value,
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                TextColor = InfoTextColor,
                Location = new Point(Inset, RowLabelY),
                Parent = row.Panel,
            };
        }

        /// <summary>
        /// A fact the user has to be able to select and copy - the data
        /// directory, which goes into a bug report. A plain TextBox, since
        /// TextInputBase.HandleCopy already gives select-all/copy; the
        /// field is never read back, so an in-place edit is harmless and
        /// resets on the next tab visit.
        /// </summary>
        private void AddCopyableFactRow(string label, string value)
        {
            var row = CreateFactRow(label, value);
            row.ValueBox = new TextBox()
            {
                Text = value,
                Size = new Point(AboutLayoutMath.ValueFloor, InputHeight),
                Location = new Point(Inset, RowInputY),
                Parent = row.Panel,
            }.ReleaseOnDispose().ReleaseOnEnter();
        }

        /// <summary>
        /// A fact whose value carries a link. The runs are rebuilt from
        /// <paramref name="segments"/> every time the column is measured,
        /// since a link's underline spans the run's own advance and both
        /// move with the width.
        /// </summary>
        private void AddLinkedFactRow(
            string label, string value, IReadOnlyList<PlanNoteSegment> segments)
        {
            var row = CreateFactRow(label, value);
            row.ValueSegments = segments;
            row.ValueRuns = new Panel()
            {
                Size = new Point(AboutLayoutMath.ValueFloor, RowHeight),
                Location = new Point(Inset, 0),
                Parent = row.Panel,
            };
        }

        /// <summary>
        /// The RESIZE entry point: a no-op when the width has not actually
        /// moved (Resized fires on height-only changes too, and repeatedly
        /// while the window is dragged). Positions track the drag; the
        /// wrapping and ellipsizing wait for it to stop, the module's
        /// standing split for text measurement on a resize path.
        /// <para>
        /// Build must NOT come through here. Module reuses one
        /// AboutTabContent instance across every tab open and Blish re-runs
        /// Build on each of them, so the freshly-created blocks of the
        /// second open would be measured against the width the first open
        /// left in <see cref="_panelWidth"/>, this guard would short-circuit,
        /// and every block would stay stacked at (0, 0) inside a
        /// zero-height panel - a blank tab. Build calls
        /// <see cref="ApplyLayout"/> directly instead, which is also what
        /// SettingsTabContent does.
        /// </para>
        /// </summary>
        private void Relayout(int panelWidth)
        {
            if (panelWidth == _panelWidth)
            {
                return;
            }

            ApplyLayout(panelWidth, measureText: false);
            _resizeSettle.Schedule();
        }

        /// <summary>
        /// The trailing half of a resize. Skipped while a rebuild is in
        /// flight, whose own Build pass measures everything anyway - see
        /// <see cref="_buildComplete"/>.
        /// </summary>
        private void RefitTextAfterResizeSettle()
        {
            if (!_buildComplete || _panelWidth <= 0)
            {
                return;
            }

            ApplyLayout(_panelWidth, measureText: true);
        }

        /// <summary>
        /// Places every block at the given panel width: the header band
        /// across the top, the identity card in the left column, the two
        /// prose blocks in the right one - stacked into one column below
        /// AboutLayoutMath.TwoColumnThreshold, with the text still capped at
        /// the reading measure either way.
        /// </summary>
        private void ApplyLayout(int panelWidth, bool measureText)
        {
            if (_documentPanel == null || panelWidth <= 0)
            {
                return;
            }

            _panelWidth = panelWidth;
            _documentPanel.Width = panelWidth;

            int columnCount = AboutLayoutMath.ColumnCount(panelWidth);
            int columnWidth = AboutLayoutMath.ColumnWidth(panelWidth);
            int rightX = columnCount == 1 ? 0 : AboutLayoutMath.SecondColumnX(panelWidth);

            int y = LayoutHeader(panelWidth);

            int leftY = y;
            int rightY = columnCount == 1 ? 0 : y;

            if (_descriptionLabel != null)
            {
                leftY += LayoutProse(
                    _descriptionLabel, _descriptionText, 0, leftY, columnWidth, measureText) + BlockGap;
            }

            leftY = LayoutFactsCard(0, leftY, columnWidth, measureText);

            if (columnCount == 1)
            {
                rightY = leftY + BlockGap;
            }

            for (int i = 0; i < _proseBlocks.Count; i++)
            {
                if (i > 0)
                {
                    rightY += _proseBlocks[i].IsSubsection
                        ? AboutLayoutMath.SubsectionGap
                        : BlockGap;
                }

                rightY = LayoutProseBlock(_proseBlocks[i], rightX, rightY, columnWidth, measureText);
            }

            _documentPanel.Height = (leftY > rightY ? leftY : rightY) + BlockGap;
        }

        private int LayoutHeader(int panelWidth)
        {
            _headerPanel.Location = new Point(0, 0);
            _headerPanel.Size = new Point(panelWidth, HeaderRowHeight);
            _headerRule.Size = new Point(panelWidth, 2);

            int nameX = _iconImage == null ? Inset : Inset + IconSize + IconToNameGap;
            _nameLabel.Location = new Point(nameX, HeaderTitleY);

            int nameWidth = (int)Math.Ceiling(
                UiFonts.SectionTitle.MeasureString(_nameLabel.Text ?? "").Width);

            // Same baseline as the title beside it, not the same top: the
            // two tiers have different line boxes.
            int baseline = HeaderTitleY + TypeRampMetrics.SectionTitleInk.BaselineY;
            _versionLabel.Location = new Point(
                nameX + nameWidth + NameToVersionGap,
                TypeRampMetrics.BaselineAlignedY(TypeRampMetrics.Regular20, baseline));

            return HeaderRowHeight + BlockGap;
        }

        private int LayoutFactsCard(int x, int y, int columnWidth, bool measureText)
        {
            y = LayoutProseBlock(_factsBlock, x, y, columnWidth, measureText) + TitleToContentGap;

            int labelBand = AboutLayoutMath.LabelFloor;
            var font = UiFonts.Body;
            foreach (var row in _factRows)
            {
                int width = (int)Math.Ceiling(font.MeasureString(row.LabelText).Width);
                if (width > labelBand)
                {
                    labelBand = width;
                }
            }

            int valueX = AboutLayoutMath.ValueX(labelBand);
            foreach (var row in _factRows)
            {
                row.Panel.Location = new Point(x, y);
                row.Panel.Size = new Point(columnWidth, RowHeight);

                if (row.ValueBox != null)
                {
                    row.ValueBox.Location = new Point(valueX, RowInputY);
                    row.ValueBox.Width = AboutLayoutMath.CopyBoxWidth(columnWidth, labelBand);
                }
                else if (row.ValueRuns != null)
                {
                    int runBudget = AboutLayoutMath.ValueMaxWidth(columnWidth, labelBand);
                    row.ValueRuns.Location = new Point(valueX, 0);
                    row.ValueRuns.Size = new Point(runBudget, RowHeight);
                    if (measureText)
                    {
                        DrawLinkedValue(row, runBudget);
                    }
                }
                else
                {
                    int budget = AboutLayoutMath.ValueMaxWidth(columnWidth, labelBand);
                    row.ValueLabel.Location = new Point(valueX, RowLabelY);
                    row.ValueLabel.Width = budget;
                    if (!measureText)
                    {
                        y += RowHeight;
                        continue;
                    }

                    string shown = LabelHelpers.EllipsizeToWidth(font, row.ValueText, budget);
                    if (!string.Equals(row.ValueLabel.Text, shown, StringComparison.Ordinal))
                    {
                        row.ValueLabel.Text = shown;
                    }

                    string full = string.Equals(shown, row.ValueText, StringComparison.Ordinal)
                        ? null
                        : row.ValueText;
                    TooltipFacility.ApplyPlain(row.ValueLabel, full);
                    TooltipFacility.ApplyPlain(row.Panel, full);
                }

                y += RowHeight;
            }

            return y;
        }

        private int LayoutProseBlock(ProseBlock block, int x, int y, int columnWidth, bool measureText)
        {
            block.Panel.Location = new Point(x, y);
            if (block.Rule != null)
            {
                block.Rule.Size = new Point(columnWidth, 2);
            }

            int band = BandHeight(block.IsSubsection);
            int height = band;
            if (block.Body != null)
            {
                height += TitleToContentGap
                    + LayoutProse(
                        block.Body, block.BodyText, 0, band + TitleToContentGap,
                        columnWidth, measureText);
            }
            else if (block.BodyHost != null)
            {
                height += TitleToContentGap
                    + LayoutLinkedProse(
                        block, band + TitleToContentGap, columnWidth, measureText);
            }

            block.Panel.Size = new Point(columnWidth, height);
            return y + height;
        }

        /// <summary>
        /// One fact's linked value, rebuilt at the width it now has. Capped
        /// at one line: a fact row is a fixed-height row, so an over-long
        /// value ellipsizes and the row's hover carries the whole of it.
        /// </summary>
        private static void DrawLinkedValue(FactRow row, int budget)
        {
            row.ValueRuns.ClearChildren();
            bool truncated = LinkedTextRenderer.DrawEllipsizedLine(
                row.ValueRuns, row.ValueSegments, 0, RowLabelY, budget, UiFonts.Body,
                InfoTextColor, row.ValueText);

            TooltipFacility.ApplyPlain(row.Panel, truncated ? row.ValueText : null);
        }

        /// <summary>
        /// A linked block's paragraphs, wrapped and drawn as runs. At
        /// measureText false the runs keep the wrap they already have and
        /// only the host moves, the same split every other block on this
        /// tab uses.
        /// </summary>
        private static int LayoutLinkedProse(
            ProseBlock block, int y, int columnWidth, bool measureText)
        {
            int budget = AboutLayoutMath.TextBudget(columnWidth);
            block.BodyHost.Location = new Point(Inset, y);

            if (!measureText)
            {
                block.BodyHost.Width = budget;
                return block.BodyHost.Height;
            }

            block.BodyHost.ClearChildren();
            int height = LinkedTextRenderer.DrawParagraphs(
                block.BodyHost, block.Paragraphs, budget, ProseLinePitch, ProseParagraphGap,
                UiFonts.Body, InfoTextColor);

            block.BodyHost.Size = new Point(budget, height);
            return height;
        }

        // Gap between a section's title band and its first content row -
        // the same 6 the Settings board uses, aliased so the two tabs'
        // section rhythm cannot drift.
        private const int TitleToContentGap = SettingsFormLayout.TitleToContentGap;

        /// <summary>
        /// Wraps one paragraph into an already-created label and returns the
        /// height it took: one <see cref="ProseLineHeight"/> row per physical
        /// line, capped at the reading measure however wide the column is.
        /// </summary>
        private static int LayoutProse(
            Label label, string text, int x, int y, int columnWidth, bool measureText)
        {
            int budget = AboutLayoutMath.TextBudget(columnWidth);

            // At measureText false the paragraph keeps the wrap it already
            // has and only its box moves; the label's own Height is the
            // cache, written explicitly here and never auto-sized.
            if (measureText)
            {
                var wrapped = TextWrapMath.Wrap(
                    text, budget, budget, LabelHelpers.MeasureWith(UiFonts.Body));
                string joined = string.Join("\n", wrapped.Lines);

                if (!string.Equals(label.Text, joined, StringComparison.Ordinal))
                {
                    label.Text = joined;
                }

                label.Size = new Point(budget, wrapped.Lines.Count * ProseLineHeight);

                TooltipFacility.ApplyPlain(label, wrapped.Truncated ? text : null);
            }
            else
            {
                label.Width = budget;
            }

            label.Location = new Point(x + Inset, y);
            return label.Height;
        }

        private class AboutInfo
        {
            public string Name;
            public string Version;
            public string Description;
            public string Url;
            public string AuthorDisplay;
            public string BlishVersionRange;
        }

        // CS0649 (never assigned) is wrong about the two DTOs below: every field
        // is written by reflection, by the JsonConvert.DeserializeObject call in
        // ReadFromManifestJsonFallback, which the compiler cannot see.
#pragma warning disable CS0649
        private class ManifestFallbackContributorDto
        {
            [JsonProperty("name")]
            public string Name;
        }

        private class ManifestFallbackDto
        {
            [JsonProperty("name")]
            public string Name;

            [JsonProperty("version")]
            public string Version;

            [JsonProperty("description")]
            public string Description;

            [JsonProperty("url")]
            public string Url;

            [JsonProperty("author")]
            public ManifestFallbackContributorDto Author;

            [JsonProperty("contributors")]
            public List<ManifestFallbackContributorDto> Contributors;

            [JsonProperty("dependencies")]
            public Dictionary<string, string> Dependencies;
        }
#pragma warning restore CS0649

        private AboutInfo LoadAboutInfo()
        {
            return TryReadFromLiveManifest() ?? ReadFromManifestJsonFallback();
        }

        private AboutInfo TryReadFromLiveManifest()
        {
            try
            {
                var manifest = _moduleParameters.Manifest;
                if (manifest == null)
                {
                    return null;
                }

                string name = manifest.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                return new AboutInfo
                {
                    Name = name,
                    Version = ReadVersionText(manifest) ?? NotAvailableText,
                    Description = manifest.Description ?? "",
                    Url = manifest.Url ?? "",
                    AuthorDisplay = ResolveAuthorDisplay(manifest.Author, manifest.Contributors),
                    BlishVersionRange = ReadBlishHudDependencyRange(manifest.Dependencies),
                };
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to read the live module manifest for the About tab, falling back to manifest.json");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "about", $"Failed to read the live module manifest for the About tab, falling back to manifest.json: {ex.GetType().Name} - {ex.Message}");
                return null;
            }
        }

        // manifest.Version is a SemVer.Version (external package, see class
        // doc comment) - read via reflection so this project never needs a
        // compile-time reference to it. ToString() on that type is what
        // Blish HUD's own Manifest.GetDetailedName() uses to render a
        // version, so this matches Blish's own display convention.
        private static string ReadVersionText(Manifest manifest)
        {
            try
            {
                var versionProperty = manifest.GetType().GetProperty("Version");
                object versionValue = versionProperty?.GetValue(manifest);
                string text = versionValue?.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception ex)
            {
                Logger.Debug("Could not read the live manifest's Version via reflection: {0}", ex.Message);
                return null;
            }
        }

        private static string ResolveAuthorDisplay(ModuleContributor author, List<ModuleContributor> contributors)
        {
            if (author != null && !string.IsNullOrWhiteSpace(author.Name))
            {
                return author.Name;
            }

            if (contributors == null || contributors.Count == 0)
            {
                return null;
            }

            var names = contributors
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => c.Name)
                .ToList();

            return names.Count == 0 ? null : string.Join(", ", names);
        }

        // A dependency's VersionRange is a SemVer.Range (external package,
        // see class doc comment) - read via reflection for the same reason
        // as ReadVersionText above. ModuleDependency.IsBlishHud itself is a
        // plain bool and safe to call directly.
        private static string ReadBlishHudDependencyRange(List<ModuleDependency> dependencies)
        {
            if (dependencies == null)
            {
                return null;
            }

            foreach (var dependency in dependencies)
            {
                if (dependency == null || !dependency.IsBlishHud)
                {
                    continue;
                }

                try
                {
                    var rangeProperty = dependency.GetType().GetProperty("VersionRange");
                    object rangeValue = rangeProperty?.GetValue(dependency);
                    string text = rangeValue?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug("Could not read a live manifest dependency's VersionRange via reflection: {0}", ex.Message);
                }
            }

            return null;
        }

        private AboutInfo ReadFromManifestJsonFallback()
        {
            try
            {
                using (var stream = TryOpenManifestJsonFallbackStream())
                {
                    if (stream == null)
                    {
                        return FallbackDefaultInfo();
                    }

                    using (var reader = new StreamReader(stream))
                    {
                        string json = reader.ReadToEnd();
                        var dto = JsonConvert.DeserializeObject<ManifestFallbackDto>(json);
                        if (dto == null)
                        {
                            return FallbackDefaultInfo();
                        }

                        string authorDisplay = null;
                        if (dto.Author != null && !string.IsNullOrWhiteSpace(dto.Author.Name))
                        {
                            authorDisplay = dto.Author.Name;
                        }
                        else if (dto.Contributors != null && dto.Contributors.Count > 0)
                        {
                            var names = dto.Contributors
                                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name))
                                .Select(c => c.Name)
                                .ToList();
                            if (names.Count > 0)
                            {
                                authorDisplay = string.Join(", ", names);
                            }
                        }

                        string blishRange = null;
                        if (dto.Dependencies != null &&
                            dto.Dependencies.TryGetValue(BlishHudDependencyNamespace, out string range) &&
                            !string.IsNullOrWhiteSpace(range))
                        {
                            blishRange = range;
                        }

                        return new AboutInfo
                        {
                            Name = string.IsNullOrWhiteSpace(dto.Name) ? ModuleDisplayName : dto.Name,
                            Version = string.IsNullOrWhiteSpace(dto.Version) ? NotAvailableText : dto.Version,
                            Description = dto.Description ?? "",
                            Url = dto.Url ?? "",
                            AuthorDisplay = authorDisplay,
                            BlishVersionRange = blishRange,
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to read the manifest.json fallback for the About tab");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "about", $"Failed to read the manifest.json fallback for the About tab: {ex.GetType().Name} - {ex.Message}");
                return FallbackDefaultInfo();
            }
        }

        /// <summary>
        /// Locates the packaged manifest.json next to this module's own
        /// loaded assembly. Deliberately NOT
        /// ContentsManager.GetFileStream("manifest.json") - ContentsManager
        /// is rooted at the module package's "ref" subfolder (Blish HUD's
        /// own ContentsManager.GetModuleInstance calls
        /// module.DataReader.GetSubPath("ref")), but BlishHUD.targets'
        /// BuildBlishHUDModule target copies manifest.json to the package
        /// ROOT alongside the compiled module DLL, never into ref/ - so
        /// ContentsManager can never see it there. Reading next to
        /// Assembly.GetExecutingAssembly().Location matches how the
        /// package is actually laid out on disk.
        /// </summary>
        private static Stream TryOpenManifestJsonFallbackStream()
        {
            try
            {
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(assemblyLocation))
                {
                    return null;
                }

                string directory = Path.GetDirectoryName(assemblyLocation);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return null;
                }

                string manifestPath = Path.Combine(directory, "manifest.json");
                return File.Exists(manifestPath) ? File.OpenRead(manifestPath) : null;
            }
            catch (Exception ex)
            {
                Logger.Debug("Could not locate the packaged manifest.json next to the module assembly: {0}", ex.Message);
                return null;
            }
        }

        private static AboutInfo FallbackDefaultInfo()
        {
            return new AboutInfo
            {
                Name = ModuleDisplayName,
                Version = NotAvailableText,
                Description = "",
                Url = "",
                AuthorDisplay = null,
                BlishVersionRange = null,
            };
        }
    }
}
