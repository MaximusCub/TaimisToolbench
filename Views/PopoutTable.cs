using System;
using System.Collections.Generic;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// One popout's table: the Crafting Plan tab's own section renderer,
    /// drawn into a popout window, with a tick column beside it.
    /// <para>
    /// The renderers are called, never copied. Everything a table does on
    /// the plan tab - the pinned column header, the click-to-sort cells, the
    /// ellipsis that re-measures on resize, the row rules, the icon hovers
    /// and the wiki right-click - is the code that does it there, reached
    /// through <see cref="ISectionRelayoutSink"/>, which is the seam the
    /// renderers were already written against.
    /// </para>
    /// <para>
    /// The tick column is a SIBLING of the renderer's flow, not a cell
    /// inside its rows: the plan tab draws those rows too and must not grow
    /// a tick column. The flow is inset by
    /// <see cref="PopoutLayout.CheckColumnWidth"/> and the boxes are laid
    /// down that gutter at the row offsets <see cref="PopoutLayout"/>
    /// derives from the same per-row heights the renderer draws at.
    /// </para>
    /// </summary>
    internal sealed class PopoutTable
    {
        /// <summary>How far a ticked row is faded.</summary>
        private const float CheckedRowOpacity = 0.4f;

        private readonly PopoutSectionState _state;
        private readonly ISectionRelayoutSink _sink;
        private readonly TableSortState<PlanTableColumn> _sortState;
        private readonly Action _onSortChanged;
        private readonly Func<int, ItemTooltipFacts> _getItemFacts;
        private readonly Func<int, CurrencyTooltipFacts> _getCurrencyFacts;

        internal PopoutTable(
            PopoutSectionState state,
            ISectionRelayoutSink sink,
            TableSortState<PlanTableColumn> sortState,
            Action onSortChanged,
            Func<int, ItemTooltipFacts> getItemFacts,
            Func<int, CurrencyTooltipFacts> getCurrencyFacts)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _sortState = sortState ?? throw new ArgumentNullException(nameof(sortState));
            _onSortChanged = onSortChanged ?? throw new ArgumentNullException(nameof(onSortChanged));
            _getItemFacts = getItemFacts ?? throw new ArgumentNullException(nameof(getItemFacts));
            _getCurrencyFacts = getCurrencyFacts
                ?? throw new ArgumentNullException(nameof(getCurrencyFacts));
        }

        /// <summary>
        /// Builds the table into <paramref name="parent"/>, which must be
        /// the popout's scrolling flow. Returns the host panel so the caller
        /// can dispose the whole table in one call.
        /// </summary>
        internal Panel Build(FlowPanel parent, int contentWidth)
        {
            var rows = DrawOrder();
            int tableWidth = PopoutLayout.TableWidth(contentWidth);
            int hostWidth = tableWidth + PopoutLayout.CheckColumnWidth;
            int bodyHeight = PlanContentHeightMath.SectionBodyHeight(_state.SectionType, rows);

            var host = new ClippedPanel()
            {
                Size = new Point(hostWidth, bodyHeight),
                Parent = parent,
            };

            var checkColumn = new ClippedPanel()
            {
                Size = new Point(PopoutLayout.CheckColumnWidth, bodyHeight),
                Parent = host,
            };

            var tableFlow = new ClippedFlowPanel()
            {
                Size = new Point(tableWidth, bodyHeight),
                Location = new Point(PopoutLayout.CheckColumnWidth, 0),
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                Parent = host,
            };

            RenderRows(rows, tableFlow, tableWidth);
            BuildCheckColumn(rows, checkColumn, tableFlow);

            // The argument is the TABLE's width, not the popout's content
            // box: every other closure in this registry is a section
            // renderer's, and that is the width they justify their columns
            // to. The two containers here are the only ones that have to
            // add the tick column back on.
            _sink.AddRelayout(w =>
            {
                host.Size = new Point(w + PopoutLayout.CheckColumnWidth, bodyHeight);
                tableFlow.Size = new Point(w, bodyHeight);
            });

            return host;
        }

        /// <summary>
        /// The rows in the order they will be drawn. The Shopping List
        /// renderer sorts its own rows, so the same pure sort runs here
        /// against the same state: the tick column has to follow the rows
        /// down the screen, and re-deriving the order from the same function
        /// is what stops the two disagreeing.
        /// </summary>
        private IReadOnlyList<PlanRowViewModel> DrawOrder()
        {
            if (_state.SectionType == PlanSectionType.CraftingSteps)
            {
                return _state.Rows;
            }

            return PlanTableSorter.Sort(_state.Rows, _sortState);
        }

        private void RenderRows(
            IReadOnlyList<PlanRowViewModel> rows, FlowPanel tableFlow, int tableWidth)
        {
            var section = new PlanSectionViewModel
            {
                SectionType = _state.SectionType,
                Rows = new List<PlanRowViewModel>(rows),
                IsDefaultExpanded = true,
            };

            if (_state.SectionType == PlanSectionType.CraftingSteps)
            {
                new CraftStepsSectionRenderer(_sink, _getItemFacts)
                    .Render(section, tableFlow, tableWidth);
                return;
            }

            new ShoppingListSectionRenderer(
                _sink, _sortState, _onSortChanged, _getCurrencyFacts, _getItemFacts)
                .Render(section, tableFlow, tableWidth);
        }

        /// <summary>
        /// One box per drawn row, and the row's own fade when it carries a
        /// tick. The row panels are the renderer's own: each row builder
        /// parents exactly one control to the flow, in draw order, so the
        /// last <c>rows.Count</c> children are the rows and whatever
        /// precedes them is the column header band's place-holder.
        /// </summary>
        private void BuildCheckColumn(
            IReadOnlyList<PlanRowViewModel> rows, Container checkColumn, FlowPanel tableFlow)
        {
            var offsets = PopoutLayout.CheckboxYOffsets(_state.SectionType, rows);
            var rowPanels = TrailingChildren(tableFlow, rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                int y = offsets[i];
                var rowPanel = rowPanels[i];
                if (y < 0)
                {
                    continue;
                }

                var row = rows[i];
                bool ticked = _state.IsChecked(row);
                ApplyRowFade(rowPanel, ticked);

                var box = new Checkbox()
                {
                    Checked = ticked,
                    Size = new Point(PopoutLayout.CheckboxSize, PopoutLayout.CheckboxSize),
                    Location = new Point(PopoutLayout.CheckboxX, y),
                    BasicTooltipText = "Mark this line done. It greys out and stays on the list.",
                    Parent = checkColumn,
                };

                var captured = rowPanel;
                box.CheckedChanged += (_, e) =>
                {
                    _state.SetChecked(row, e.Checked);
                    ApplyRowFade(captured, e.Checked);
                };
            }
        }

        /// <summary>
        /// Fades a ticked row. Opacity reaches the GPU through
        /// <c>AbsoluteOpacity()</c>, which walks the parent chain, so
        /// dimming the row panel dims its labels, icons and coin cells with
        /// it - see Views/Rendering/PressFeedback.cs for the measurement.
        /// </summary>
        private static void ApplyRowFade(Control rowPanel, bool ticked)
        {
            if (rowPanel == null)
            {
                return;
            }

            rowPanel.Opacity = ticked ? CheckedRowOpacity : 1f;
        }

        private static IReadOnlyList<Control> TrailingChildren(Container parent, int count)
        {
            var all = new List<Control>(parent.Children);
            var trailing = new List<Control>(count);
            int first = all.Count - count;
            for (int i = 0; i < count; i++)
            {
                int index = first + i;
                trailing.Add(index >= 0 && index < all.Count ? all[index] : null);
            }

            return trailing;
        }
    }
}
