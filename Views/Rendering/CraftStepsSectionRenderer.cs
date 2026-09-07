using System;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // The Crafting Steps row list (including its TimegatedNotice
    // informational rows) and the step-number rendering.
    //
    // CreateCraftStepRow's
    // divider+relayout tail goes through RowRelayoutHelpers.FinishRow -
    // the shared "row panel resize + extra reposition + divider resize"
    // shape identical across all five extracted renderers' row
    // builders (see that class's doc comment). This row's name/qty labels
    // are NOT run through IconNameRowHelpers: this row has no icon column
    // of the shape that helper builds, and its name sits at a cursor x
    // accumulated from the two fixed words before it ("Craft " + "{n}x ")
    // rather than at a fixed column - see IconNameRowHelpers' own doc
    // comment. Only the ellipsis idiom itself is shared.
    internal sealed class CraftStepsSectionRenderer
    {
        private readonly ISectionRelayoutSink _sink;
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;

        internal CraftStepsSectionRenderer(
            ISectionRelayoutSink sink, Func<int, ItemStatBlock> getItemStatBlock = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _getItemStatBlock = getItemStatBlock;
        }

        /// <summary>
        /// The sublabel column is pinned to the panel edge and the
        /// "Craft Nx Name" run flexes into whatever is left of the row,
        /// ellipsizing with its full name on a tooltip.
        /// <para>
        /// The one pre-scan is the sublabel BAND - the widest sublabel this
        /// render draws, which is where the step name's budget has to stop.
        /// Budgeting against a row's own (possibly absent) sublabel instead
        /// would let a short-sublabel row's name run under the widest one.
        /// TimegatedNotice rows are plain full-width text rows with no
        /// columns of their own, so they take no part in the scan; a
        /// section where no row carries a sublabel gives its names the
        /// whole row. The column has no header, so unlike every other table
        /// here the band has no header label to floor it.
        /// </para>
        /// </summary>
        internal void Render(PlanSectionViewModel section, FlowPanel contentFlow, int panelWidth)
        {
            var sublabelFont = UiFonts.Caption;
            int maxSublabelWidth = 0;
            for (int i = 0; i < section.Rows.Count; i++)
            {
                var row = section.Rows[i];
                if (row.RowType == PlanRowType.TimegatedNotice)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(row.Sublabel))
                {
                    continue;
                }

                int width = (int)System.Math.Ceiling(sublabelFont.MeasureString(row.Sublabel).Width);
                if (width > maxSublabelWidth)
                {
                    maxSublabelWidth = width;
                }
            }

            // A TimegatedNotice row (vendor-cap informational
            // line) is a plain text row, not a numbered craft step - render
            // it via the same generic TextRowRenderer pattern every other
            // section's fallback rows use, and don't consume a step number
            // for it (stepNumber only advances for real CraftStep rows).
            int stepNumber = 1;
            for (int i = 0; i < section.Rows.Count; i++)
            {
                var row = section.Rows[i];
                bool isLast = i == section.Rows.Count - 1;
                if (row.RowType == PlanRowType.TimegatedNotice)
                {
                    TextRowRenderer.CreateTextRow(row.Label, contentFlow, panelWidth, _sink);
                }
                else
                {
                    CreateCraftStepRow(row, stepNumber++, contentFlow, panelWidth, maxSublabelWidth, isLast);
                }
            }
        }

        private static string QtyPrefix(int quantity)
        {
            return $"{quantity}x ";
        }

        // Left x of the row's tier-2 icon frame (past the numbered badge),
        // of the text run after it, and the run's fixed leading word - all
        // shared with Render()'s pre-scan so the measured extent is exactly
        // what the row lays out.
        private const int IconX = 52;
        private const int TextX = IconX + PlanContentHeightMath.RowIconFrameSize + 8;
        private const string CraftPrefix = "Craft ";

        // Text anchor of the row's reading line, and of the right-aligned
        // sublabel under it. Both are offsets from the icon frame's y, so
        // the line stays level with the icon whenever the frame moves.
        private const int RowTextY = PlanContentHeightMath.IconRowIconY + 12;
        private const int SublabelY = PlanContentHeightMath.IconRowIconY + 15;

        // Gap the step name's ellipsis budget keeps between itself and the
        // sublabel band, matching the name-to-column gap every other table
        // in the plan reserves.
        private const int NameToSublabelGap = 12;

        private void CreateCraftStepRow(
            PlanRowViewModel row, int stepNumber, FlowPanel parent, int panelWidth,
            int maxSublabelWidth, bool isLast)
        {
            const int rowHeight = PlanContentHeightMath.CraftStepRowHeight;
            const int badgeSize = PlanContentHeightMath.CraftStepBadgeSize;
            const int badgeX = 8;

            // Centred on the band a reader sees, not on rowHeight. The rule
            // and its clearance pixel are not space, so counting them left
            // 8px over the badge and 4px under it.
            const int badgeY = PlanContentHeightMath.CraftStepBadgeY;

            var rowPanel = new ClippedPanel() { Size = new Point(panelWidth, rowHeight), Parent = parent };

            new ClippedPanel()
            {
                Size = new Point(badgeSize, badgeSize),
                Location = new Point(badgeX, badgeY),
                BackgroundColor = Color.White * 0.08f,
                Parent = rowPanel,
            };
            // Digits only, so the space-glyph defect that retired
            // 18-regular elsewhere is not the reason this moved - the badge
            // is chrome, and chrome above body is bold. 20-bold's cap fills
            // the badge square better than 18-regular's did.
            string numberText = stepNumber.ToString();
            var numberFont = UiFonts.SmallHeadingBold;
            var numberMeasure = numberFont.MeasureString(numberText);
            int numberWidth = (int)System.Math.Ceiling(numberMeasure.Width);
            int numberHeight = (int)System.Math.Ceiling(numberMeasure.Height);
            new Label()
            {
                Text = numberText,
                Font = numberFont,
                TextColor = Color.White,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(badgeX + (badgeSize - numberWidth) / 2, badgeY + (badgeSize - numberHeight) / 2),
                Parent = rowPanel,
            };

            int itemId = row.ItemId;
            var hover = ItemIconTooltip.ForItem(
                ItemTooltipIdentity.ForItem(row.Label ?? "", row.IconUrl, row.Rarity),
                _getItemStatBlock == null || itemId <= 0 ? (Func<ItemStatBlock>)null
                    : () => _getItemStatBlock(itemId));

            IconControls.CreateItemIcon(
                rowPanel, row.IconUrl, ItemIconFrame.ForRarity(row.Rarity),
                IconX, PlanContentHeightMath.IconRowIconY, ItemIconTier.BagSidebar, hover);

            var textFont = UiFonts.Body;
            var greyColor = new Color(170, 170, 170);
            int x = TextX;

            // "Craft ", "12x " and the item name are one sentence on one
            // baseline: every label on it gets the same box treatment, so
            // the clearance can never make the three disagree.
            var craftLabel = LabelHelpers.WithDescenderClearance(
                new Label()
                {
                    Text = CraftPrefix, Font = textFont, TextColor = greyColor,
                    AutoSizeWidth = true, AutoSizeHeight = true,
                    Location = new Point(x, RowTextY), Parent = rowPanel,
                });
            x += craftLabel.Width;

            var qtyLabel = LabelHelpers.WithDescenderClearance(
                new Label()
                {
                    Text = QtyPrefix(row.Quantity), Font = textFont, TextColor = greyColor,
                    AutoSizeWidth = true, AutoSizeHeight = true,
                    Location = new Point(x, RowTextY), Parent = rowPanel,
                });
            x += qtyLabel.Width;

            // The name is the row's flexing run: "Craft " and "Nx " are
            // fixed words at a font-only cursor x, so the whole of the
            // row's slack lands here. nameX is that cursor - invariant to
            // panelWidth, which is why the settle closure recaptures it
            // rather than re-measuring the two labels before it.
            int nameX = x;
            string fullName = row.Label ?? "";
            int nameMaxWidth = PlanRelayoutMath.NameMaxWidthBeforeColumn(
                PlanRelayoutMath.PinnedRightEdge(panelWidth), maxSublabelWidth, NameToSublabelGap, nameX);
            var nameLabel = LabelHelpers.WithDescenderClearance(
                new Label()
                {
                    Text = LabelHelpers.EllipsizeToWidth(textFont, fullName, nameMaxWidth),
                    Font = textFont, TextColor = RarityColors.GetRarityNameColor(row.Rarity),
                    ShowShadow = true, ShadowColor = Color.Black * 0.8f,
                    AutoSizeWidth = true, AutoSizeHeight = true,
                    Location = new Point(nameX, RowTextY), Parent = rowPanel,
                });

            Label sublabelLabel = null;
            if (!string.IsNullOrEmpty(row.Sublabel))
            {
                sublabelLabel = LabelHelpers.CreateRightAlignedLabel(
                    rowPanel, row.Sublabel, UiFonts.Caption,
                    new Color(153, 153, 153),
                    PlanRelayoutMath.PinnedRightEdge(panelWidth), SublabelY);
            }

            // IconRowDividerClearance, not 0: the row height absorbs the
            // clearance pixel in its own derivation, and the simulation
            // behind LabelHelpers.CreateRowDivider (executable in
            // RowDividerScissorSimulationTests) proves this height needs
            // the clearance.
            //
            // Name/qty labels sit at a fixed x (font-only, not
            // width-dependent - textX never depended on panelWidth); only
            // the row width, its divider, and the right-aligned sublabel
            // need to move.
            RowRelayoutHelpers.FinishRow(
                rowPanel, panelWidth, rowHeight, isLast,
                PlanContentHeightMath.IconRowDividerClearance, _sink,
                w =>
                {
                    if (sublabelLabel != null)
                    {
                        sublabelLabel.Location = new Point(
                            PlanRelayoutMath.RightAlignedX(
                                PlanRelayoutMath.PinnedRightEdge(w), sublabelLabel.Width),
                            SublabelY);
                    }
                });
            _sink.AddReellipsis(w =>
            {
                string newDisplayName = LabelHelpers.EllipsizeToWidth(
                    textFont, fullName,
                    PlanRelayoutMath.NameMaxWidthBeforeColumn(
                        PlanRelayoutMath.PinnedRightEdge(w), maxSublabelWidth, NameToSublabelGap, nameX));
                if (nameLabel.Text != newDisplayName)
                {
                    nameLabel.Text = newDisplayName;
                }
            });
        }
    }
}
