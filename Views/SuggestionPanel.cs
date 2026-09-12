using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using TaimisToolbench.Contracts;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    internal class ItemSelectedEventArgs : EventArgs
    {
        public int ItemId { get; set; }

        public string Name { get; set; }

        public string IconUrl { get; set; }
    }

    internal class SuggestionPanel : IDisposable
    {
        private const int MaxResults = 8;
        private const int RowHeight = 28;

        /// <summary>Narrowest the list itself may be, whatever the box
        /// under it measures - see RebuildRows.</summary>
        private const int MinPanelWidth = 200;
        private const int IconSize = 24;
        private const int IconPad = 4;

        private readonly AutocompleteTextBox _textBox;
        private readonly IItemSearchProvider _searchProvider;

        private Panel _panel;
        private FlowPanel _rowContainer;
        private IReadOnlyList<ItemSearchResult> _results = Array.Empty<ItemSearchResult>();
        private int _highlightIndex;
        private bool _disposed;
        private bool _suppressTextChanged;
        private bool _globalMouseHooked;
        private bool _pressOverPanel;
        private CancellationTokenSource _searchCts;

        public event EventHandler<ItemSelectedEventArgs> ItemSelected;

        public SuggestionPanel(
            AutocompleteTextBox textBox,
            IItemSearchProvider searchProvider)
        {
            _textBox = textBox;
            _searchProvider = searchProvider;

            _textBox.TextChanged += OnTextChanged;
            _textBox.ArrowPressed += OnArrowPressed;
            _textBox.EnterKeyPressed += OnEnterPressed;
            _textBox.InputFocusChanged += OnFocusChanged;
            _textBox.Moved += OnTextBoxMoved;

            // Hooked for this panel's whole life, not just while it is
            // shown, and specifically here in the constructor: Blish raises
            // this event in subscription order, TextInputBase subscribes its
            // own unfocus-on-click handler when the box first gains focus,
            // and OnFocusChanged can only tell a click from an Escape if
            // this handler is already ahead of it on the list.
            GameService.Input.Mouse.LeftMouseButtonPressed += OnGlobalMousePressed;
            GameService.Input.Mouse.LeftMouseButtonReleased += OnGlobalMouseReleased;
        }

        private void OnGlobalMousePressed(object sender, MouseEventArgs e)
        {
            _pressOverPanel = !_disposed && _panel != null && _panel.Visible && _panel.MouseOver;
        }

        private void OnGlobalMouseReleased(object sender, MouseEventArgs e)
        {
            _pressOverPanel = false;
        }

        private async void OnTextChanged(object sender, EventArgs e)
        {
            if (_suppressTextChanged || _disposed)
            {
                return;
            }

            string text = _textBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                HidePanel();
                return;
            }

            string query = text.Trim();

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            IReadOnlyList<ItemSearchResult> results;
            try
            {
                // Not onto a captured context; the marshal below is the way
                // back to the UI thread (ResizeSettleDebounce says why).
                results = await _searchProvider
                    .SearchAsync(query, MaxResults, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                return;
            }

            // Blish HUD's XNA host has no SynchronizationContext, so this
            // continuation may resume on a ThreadPool thread if the search
            // provider ever completes asynchronously (see
            // Contracts/IItemSearchProvider.cs). Both providers wired up
            // today return already-completed tasks, so this is currently
            // inert, but marshal pre-emptively so a future provider cannot
            // reintroduce an off-thread control mutation here. The
            // stale-query guard stays inside the marshaled action so stale
            // results are still discarded against current state at the
            // moment of UI application, not at the moment the search
            // finished. The focus check guards against a slower path: the
            // textbox can lose focus (OnFocusChanged hides the panel) while
            // this search is still in flight, and without re-checking focus
            // here a queued result could ShowPanel() again right after
            // dismissal.
            MainThreadMarshal.Run(() =>
            {
                if (ct.IsCancellationRequested || _disposed || !_textBox.Focused)
                {
                    return;
                }

                if (_textBox.Text == null || _textBox.Text.Trim() != query)
                {
                    return;
                }

                if (results == null || results.Count == 0)
                {
                    HidePanel();
                    return;
                }

                _results = results;
                _highlightIndex = 0;
                RebuildRows();
                ShowPanel();
            });
        }

        private void OnArrowPressed(object sender, int delta)
        {
            if (_disposed || _results.Count == 0 || _panel == null || !_panel.Visible)
            {
                return;
            }

            _highlightIndex += delta;

            // Wrap around
            if (_highlightIndex < 0)
            {
                _highlightIndex = _results.Count - 1;
            }

            if (_highlightIndex >= _results.Count)
            {
                _highlightIndex = 0;
            }

            UpdateHighlights();
        }

        private void OnEnterPressed(object sender, AutocompleteEnterEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            if (_panel != null && _panel.Visible && _results.Count > 0)
            {
                e.Handled = true;
                SelectItem(_highlightIndex);
            }
        }

        private void OnFocusChanged(object sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            bool hasFocus = _textBox.Focused;
            if (!hasFocus)
            {
                // Re-focus ONLY when this focus loss is the click that is
                // landing on a suggestion row, so the row's own click can
                // fire before the panel is dismissed. Every other release -
                // Escape above all - has to pass through untouched: those
                // arrive as UnsetFocus(), which raises this notification and
                // only THEN nulls Blish's global focus slot, so re-focusing
                // from in here leaves a focused box no slot names. See
                // FocusRelease for what that state costs.
                if (_pressOverPanel)
                {
                    _pressOverPanel = false;
                    _textBox.Focused = true;
                    return;
                }

                HidePanel();
            }
        }

        private void OnTextBoxMoved(object sender, MovedEventArgs e)
        {
            if (_panel != null && _panel.Visible)
            {
                PositionPanel();
            }
        }

        private void EnsurePanel()
        {
            if (_panel != null)
            {
                return;
            }

            _panel = new Panel()
            {
                Parent = GameService.Graphics.SpriteScreen,
                ZIndex = Screen.TOOLTIP_BASEZINDEX,
                BackgroundColor = new Color(30, 30, 30, 240),
                Visible = false,
            };

            _rowContainer = new FlowPanel()
            {
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                WidthSizingMode = SizingMode.Fill,
                HeightSizingMode = SizingMode.AutoSize,
                Parent = _panel,
            };
        }

        private void RebuildRows()
        {
            EnsurePanel();

            // Clear old rows
            foreach (var child in _rowContainer.Children.ToArray())
            {
                child.Dispose();
            }

            // Not simply the box's width any more: the plan tab's input
            // strip is a grid, so its search boxes narrow as columns are
            // added, and a list that narrowed with them would clip item
            // names the box itself never had to show. 200 is the width the
            // list shipped at, held as a floor.
            int panelWidth = Math.Max(_textBox.Width, MinPanelWidth);

            for (int i = 0; i < _results.Count; i++)
            {
                int index = i;
                var item = _results[i];

                var row = new Panel()
                {
                    Size = new Point(panelWidth, RowHeight),
                    BackgroundColor = i == _highlightIndex
                        ? new Color(60, 60, 60, 255)
                        : new Color(30, 30, 30, 240),
                    Parent = _rowContainer,
                };

                // Item icon, through the module's one icon component: same
                // frame, same empty-slot placeholder for a missing IconUrl
                // (a data gap, never a load failure), same hover wiring as
                // every other item icon in the module. The search cache
                // carries no rarity, so the frame is the component's own
                // neutral unknown-rarity grey - never a guessed rarity.
                // The art is inset rather than the box grown, so the row's
                // own geometry is exactly what it was: the SearchSuggestion
                // tier's frame is IconSize on the nose.
                IconControls.CreateItemIconFromCapture(
                    row, item.IconUrl, ItemIconFrame.UnknownRarity(), 2,
                    (RowHeight - IconSize) / 2, ItemIconTier.SearchSuggestion,
                    ItemIconTooltip.None(ItemIconSilence.WouldCoverTheListItSitsIn));

                // Item name. Centred against the FONT's own line box rather
                // than a hand-tuned stand-in for it: these rows stack flush
                // with opaque backgrounds, so a stale offset puts a
                // descender on the next row's top edge.
                var nameFont = UiFonts.Body;
                new Label()
                {
                    Font = nameFont,
                    Text = item.Name,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(2 + IconSize + IconPad, (RowHeight - nameFont.LineHeight) / 2),
                    Parent = row,
                };

                row.MouseEntered += (_, __) =>
                {
                    _highlightIndex = index;
                    UpdateHighlights();
                };

                PressFeedback.Wire(row);

                row.Click += (_, __) =>
                {
                    SelectItem(index);
                };
            }

            int totalHeight = _results.Count * RowHeight;
            _panel.Size = new Point(panelWidth, totalHeight);
            _rowContainer.Size = new Point(panelWidth, totalHeight);

            PositionPanel();
        }

        private void UpdateHighlights()
        {
            var children = _rowContainer.Children.ToArray();
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i] as Panel;
                if (child != null)
                {
                    child.BackgroundColor = i == _highlightIndex
                        ? new Color(60, 60, 60, 255)
                        : new Color(30, 30, 30, 240);
                }
            }
        }

        private void PositionPanel()
        {
            if (_panel == null)
            {
                return;
            }

            var tbBounds = _textBox.AbsoluteBounds;
            int panelHeight = _panel.Height;
            var screen = GameService.Graphics.SpriteScreen;

            int yBelow = (int)tbBounds.Bottom;
            bool fitBelow = (yBelow + panelHeight) <= screen.Height;
            int y = fitBelow ? yBelow : Math.Max(0, (int)tbBounds.Top - panelHeight);

            // Left edge of the text box: a classic dropdown, directly under
            // the box being typed into. It was anchored right of the Qty
            // stepper for a while so it would not cover the controls beneath
            // it, and that was rejected outright in game: the popup read
            // as floating far off to the right. Transiently covering
            // the row's own quantity field and the rows below is what a
            // dropdown does; it closes on pick, on the box losing focus and
            // on a click outside.
            //
            // Still held on screen: the box belongs to a window the user may
            // have dragged against the right edge.
            int x = (int)tbBounds.X;
            int maxX = Math.Max(0, screen.Width - _panel.Width);
            if (x > maxX)
            {
                x = maxX;
            }

            _panel.Location = new Point(x, y);
        }

        private void ShowPanel()
        {
            if (_panel == null)
            {
                return;
            }

            // Invariant: _pressOverPanel never outlives the press that sets
            // it. TextInputBase hooks the same global press event when the
            // box gains focus, so its unfocus handler runs after this
            // panel's and OnFocusChanged consumes the flag in that same
            // dispatch. The clears here, in HidePanel and in Dispose hold
            // the bound without relying on that ordering, or on a
            // LeftMouseButtonReleased that Blish may never deliver.
            _pressOverPanel = false;

            _panel.Visible = true;

            if (!_globalMouseHooked)
            {
                GameService.Input.Mouse.LeftMouseButtonPressed += OnGlobalMouseClick;
                _globalMouseHooked = true;
            }
        }

        public void HidePanel()
        {
            _pressOverPanel = false;

            if (_panel != null)
            {
                _panel.Visible = false;
            }

            if (_globalMouseHooked)
            {
                GameService.Input.Mouse.LeftMouseButtonPressed -= OnGlobalMouseClick;
                _globalMouseHooked = false;
            }
        }

        private void OnGlobalMouseClick(object sender, MouseEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            if (_panel != null && !_panel.MouseOver && !_textBox.MouseOver)
            {
                HidePanel();
            }
        }

        private void SelectItem(int index)
        {
            if (index < 0 || index >= _results.Count)
            {
                return;
            }

            var item = _results[index];

            HidePanel();

            // Set text without triggering a new search
            _suppressTextChanged = true;
            _textBox.Text = item.Name;
            _suppressTextChanged = false;

            ItemSelected?.Invoke(this, new ItemSelectedEventArgs
            {
                ItemId = item.ItemId,
                Name = item.Name,
                IconUrl = item.IconUrl,
            });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pressOverPanel = false;

            _textBox.TextChanged -= OnTextChanged;
            _textBox.ArrowPressed -= OnArrowPressed;
            _textBox.EnterKeyPressed -= OnEnterPressed;
            _textBox.InputFocusChanged -= OnFocusChanged;
            _textBox.Moved -= OnTextBoxMoved;

            if (_globalMouseHooked)
            {
                GameService.Input.Mouse.LeftMouseButtonPressed -= OnGlobalMouseClick;
                _globalMouseHooked = false;
            }

            GameService.Input.Mouse.LeftMouseButtonPressed -= OnGlobalMousePressed;
            GameService.Input.Mouse.LeftMouseButtonReleased -= OnGlobalMouseReleased;

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;

            _panel?.Dispose();
            _panel = null;
            _rowContainer = null;
        }
    }
}
