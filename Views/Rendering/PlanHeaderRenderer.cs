using System;
using System.Collections.Generic;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// The plan's page title: the rarity-framed item icon, the item name,
    /// the "x N needed" annotation, and the stat tooltip all three carry.
    /// A multi-item batch adds its remaining items' names as an uncoloured
    /// suffix and their icons as a run after it - see RenderBatchIconRun.
    /// Freshly constructed per render like every other renderer here, and
    /// stateless for the same reason.
    /// </summary>
    internal sealed class PlanHeaderRenderer
    {
        private readonly ISectionRelayoutSink _sink;
        private readonly Func<int, ItemTooltipFacts> _getItemFacts;

        internal PlanHeaderRenderer(ISectionRelayoutSink sink, Func<int, ItemTooltipFacts> getItemFacts)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _getItemFacts = getItemFacts ?? throw new ArgumentNullException(nameof(getItemFacts));
        }

        /// <summary>
        /// Plan header: rarity-framed item icon + the item's own name in its
        /// rarity colour + a grey quantity, left-aligned at the content gutter
        /// every section below it also starts at.
        /// <para>
        /// The title renders at DefaultFont32 and CreateSectionHeader at
        /// DefaultFont16, so Font18-and-up belongs to the page title alone -
        /// this is the module's one Display-tier seat, not a local choice.
        /// There is no in-scroll timestamp: the fixed status strip 70px above
        /// carries it and never scrolls away.
        /// </para>
        /// What used to compete here: docs/ARCHITECTURE.md, "Views: relocated
        /// design narrative".
        /// </summary>
        internal void Render(PlanViewModel vm, FlowPanel contentPanel, int panelWidth)
        {
            // Tier 1 of the two-tier icon system: the plan's
            // heading item carries in-game bag-slot-sized art, like the
            // Snapshot grid and the Ranker rows. The frame thickness comes
            // from the tier now, not from a local constant - this header
            // was the one tier-1 site drawing a 2px frame, so its icon was
            // 56px where every other tier-1 icon was 54. Header height is
            // unchanged at 64, which now clears the 54px frame by 5px a
            // side instead of 4.
            const int headerHeight = 64;
            const int iconPad = 10;

            // Same 8px content gutter the Summary section's tiles, the
            // currency table's icon column and the footnote all start at.
            const int headerX = 8;

            int frameSize = ItemIconTiers.FrameSize(ItemIconTier.BagSlot);

            var titleFont = UiFonts.Display;

            // Regular weight, one tier down from the title it annotates -
            // and not the 18-regular it used to be, whose 4px space glyph
            // rendered " x 42 needed" no wider than Body did.
            var qtyFont = UiFonts.SmallHeading;

            string nameText = vm.TargetItemName ?? "Unknown Item";

            // The batch remainder (" + 2 others"). Same font as the name it
            // follows - it is the rest of one heading - but never the
            // name's rarity colour: the count is about the batch, not
            // about that item.
            string suffixText = vm.TargetNameSuffix ?? "";

            // "needed", not a bare count: the quantity here is what the
            // plan still has to obtain after owned materials were
            // subtracted, which is routinely smaller than the number in
            // the Qty box the user typed (live capture ph13: box 77,
            // header 42, 35 already owned). A bare "x 42" beside a box
            // reading 77 reads as a bug. Deliberately not "to craft" -
            // a root the solver decided to BUY is just as legitimate.
            string qtyText = vm.TargetQuantity > 1 ? $" x {vm.TargetQuantity} needed" : "";

            var nameMeasure = titleFont.MeasureString(nameText);
            int nameWidth = (int)System.Math.Ceiling(nameMeasure.Width);
            int textHeight = (int)System.Math.Ceiling(nameMeasure.Height);

            int suffixWidth = suffixText.Length > 0
                ? (int)System.Math.Ceiling(titleFont.MeasureString(suffixText).Width)
                : 0;

            int qtyHeight = 0;
            if (qtyText.Length > 0)
            {
                qtyHeight = (int)System.Math.Ceiling(qtyFont.MeasureString(qtyText).Height);
            }

            int iconY = (headerHeight - frameSize) / 2;
            int textY = iconY + (frameSize - textHeight) / 2;
            // Bottom-aligned against the much taller name rather than
            // top-aligned, with a small optical lift off the descender
            // line, so the two sit on one reading line.
            int qtyY = textY + textHeight - qtyHeight - 4;

            var titlePanel = new ClippedPanel()
            {
                Size = new Point(panelWidth, headerHeight),
                Parent = contentPanel,
            };

            // PlanViewModel carries no target item id of its own, so the
            // tree root - the very item this header names - is the id. A
            // batch has no TreeRoot, but it does head on its first
            // requested item, and that item's root is MultiItemRoots[0], so
            // the hover answers for the icon actually drawn.
            //
            // Composed at hover time, so a plan restored from disk shows
            // its stats as soon as the background top-up lands (Q13).
            var treeRoot = vm.TreeRoot ?? FirstBatchRoot(vm);
            IconControls.DrawItemIcon(
                titlePanel, treeRoot == null ? 0 : treeRoot.ItemId,
                headerX, iconY, ItemIconTier.BagSlot, TargetFactsFor(vm));

            int textX = headerX + frameSize + iconPad;
            var nameLabel = new Label()
            {
                Text = nameText,
                Font = titleFont,
                TextColor = RarityColors.GetRarityNameColor(vm.TargetRarity),
                ShowShadow = true,
                ShadowColor = Color.Black * 0.8f,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(textX, textY),
                Parent = titlePanel,
            };

            if (suffixText.Length > 0)
            {
                var suffixLabel = new Label()
                {
                    Text = suffixText,
                    Font = titleFont,
                    // The palette's own no-rarity grey, asked for as such:
                    // this run has no rarity, it is a count of the batch.
                    TextColor = RarityColors.GetRarityNameColor(null),
                    ShowShadow = true,
                    ShadowColor = Color.Black * 0.8f,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(textX + nameWidth, textY),
                    Parent = titlePanel,
                };
            }

            // Batches suppress the quantity, so this and the icon run below
            // never both claim the space after the title.
            if (qtyText.Length > 0)
            {
                var qtyLabel = new Label()
                {
                    Text = qtyText,
                    Font = qtyFont,
                    TextColor = new Color(170, 170, 170),
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(textX + nameWidth, qtyY),
                    Parent = titlePanel,
                };
            }

            // Every x in the TITLE is a constant or a font-only measurement,
            // so nothing there moves with the panel width - only the panel's
            // own cosmetic width, same as TextRowRenderer's rows. The icon
            // run is the one part that does, and it registers its own
            // closure AFTER this one so a replay grows the container before
            // its children move inside it.
            _sink.AddRelayout(w => titlePanel.Size = new Point(w, headerHeight));

            RenderBatchIconRun(
                vm.AdditionalTargetItems, titlePanel,
                textX + nameWidth + suffixWidth + MultiItemHeaderLayout.TextGap,
                headerHeight, panelWidth);
        }

        /// <summary>
        /// Everything the header item's icon draws and its tooltip shows.
        /// The view model's captured identity leads: a plan whose tree
        /// failed to build has no root id, and a restored plan may name an
        /// item this session's store never fetched.
        /// </summary>
        private Func<int, ItemTooltipFacts> TargetFactsFor(PlanViewModel vm)
        {
            string name = vm.TargetItemName;
            string iconUrl = vm.TargetIconUrl;
            string rarity = vm.TargetRarity;
            var fromStore = _getItemFacts;

            return id => ItemTooltipFacts.ForCapturedItem(
                name, iconUrl, rarity, fromStore(id).Stats);
        }

        /// <summary>
        /// The first requested item's tree root, which for a batch is the
        /// item this header names. Null for anything else, including the
        /// impossible-but-cheap empty list.
        /// </summary>
        private static CraftingTreeNode FirstBatchRoot(PlanViewModel vm)
        {
            var roots = vm.MultiItemRoots;
            return roots != null && roots.Count > 0 ? roots[0] : null;
        }

        // Padding either side of the overflow marker's glyphs, so the only
        // route to the items it hides is not a three-character hit box.
        private const int MarkerSidePad = 5;

        /// <summary>
        /// The batch's remaining items, stacked left-to-right after the
        /// title at the row-level icon tier, each hovering its own item
        /// tooltip. What does not fit collapses into a trailing marker
        /// whose hover lists the unshown items one per line.
        /// <para>
        /// Every icon and the marker are built once and then shown or
        /// hidden: the run's width follows the panel and a resize must not
        /// rebuild controls. <see cref="MultiItemHeaderLayout"/> owns the
        /// arithmetic - what fits, and where the marker starts - so both
        /// the build pass and the resize closure ask the same function.
        /// </para>
        /// </summary>
        private void RenderBatchIconRun(
            IReadOnlyList<PlanHeaderItem> items, Panel titlePanel,
            int runX, int headerHeight, int panelWidth)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            int iconFrame = ItemIconTiers.FrameSize(ItemIconTier.BagSidebar);
            int iconY = (headerHeight - iconFrame) / 2;

            // The marker is boxed to the icons' own height and given a
            // little side padding: three glyphs is a ~14px hover target,
            // and it is the ONLY way to reach the items it stands for. The
            // boxed width is what the run reserves, so what is drawn and
            // what was measured are the same rectangle.
            var markerFont = UiFonts.SmallHeading;
            int markerWidth = MarkerSidePad
                + (int)System.Math.Ceiling(markerFont.MeasureString(TextWrapMath.Ellipsis).Width)
                + MarkerSidePad;

            var icons = new Panel[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                icons[i] = IconControls.DrawItemIcon(
                    titlePanel, item.ItemId,
                    runX + MultiItemHeaderLayout.IconX(i, iconFrame, MultiItemHeaderLayout.IconGap),
                    iconY, ItemIconTier.BagSidebar, _getItemFacts);
            }

            // Read by the marker's hover builder, which runs at hover time
            // and so must see the CURRENT fit, not the one this pass found.
            int firstHidden = items.Count;

            var marker = new Label()
            {
                Text = TextWrapMath.Ellipsis,
                Font = markerFont,
                TextColor = new Color(170, 170, 170),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Middle,
                Size = new Point(markerWidth, iconFrame),
                Location = new Point(runX, iconY),
                Visible = false,
                Parent = titlePanel,
            };
            TooltipFacility.ApplyRichDeferred(
                marker,
                () => MultiItemHeaderTooltipComposer.BuildHiddenItemsContent(items, firstHidden));

            void Fit(int width)
            {
                var run = MultiItemHeaderLayout.Plan(
                    items.Count, PlanRelayoutMath.PinnedRightEdge(width) - runX,
                    iconFrame, MultiItemHeaderLayout.IconGap, markerWidth);

                for (int i = 0; i < icons.Length; i++)
                {
                    icons[i].Visible = i < run.VisibleCount;
                }

                marker.Visible = run.ShowsEllipsis;
                marker.Location = new Point(runX + run.EllipsisOffset, iconY);
                firstHidden = run.VisibleCount;
            }

            Fit(panelWidth);
            _sink.AddRelayout(Fit);
        }
    }
}
