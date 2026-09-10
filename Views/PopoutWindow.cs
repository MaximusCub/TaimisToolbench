using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// A Crafting Plan section on its own floating window, so the player can
    /// shut the module and keep the list while they play.
    /// <para>
    /// Parented to the sprite screen and never to the module window, which
    /// is what lets it outlive one. It is disposed from Module.Unload.
    /// </para>
    /// <para>
    /// One window type serves both sections: everything except the row shape
    /// is shared, and the row shape is the section renderer's, chosen by
    /// <see cref="PopoutTable"/>. See docs/ARCHITECTURE.md, "Popout windows".
    /// </para>
    /// </summary>
    internal sealed class PopoutWindow : DialogWindow, ISectionRelayoutSink
    {
        private static readonly Logger Logger = Logger.GetLogger<PopoutWindow>();

        private readonly PopoutSectionState _state;
        private readonly Func<Task<AccountSnapshot>> _refreshAsync;
        private readonly Func<int, ItemTooltipFacts> _getItemFacts;
        private readonly Func<int, CurrencyTooltipFacts> _getCurrencyFacts;
        private readonly Func<int> _opacityPercent;
        private readonly Action<int> _setOpacityPercent;
        private readonly Point _minWindowSize;

        private readonly List<Action<int>> _relayoutActions = new List<Action<int>>();
        private readonly List<Action<int>> _reellipsisActions = new List<Action<int>>();
        private readonly ResizeSettleDebounce _resizeSettle;

        private PopoutToolbar _toolbar;
        private StickyClipAuthorityFlowPanel _contentPanel;
        private StickyHeaderHost _stickyHeaders;
        private Panel _table;

        private int _laidOutWidth = -1;
        private bool _refreshing;

        // Read by the two callbacks that can land after teardown: an
        // in-flight sync and the settle pass's marshalled re-ellipsis.
        private bool _disposed;

        internal PopoutWindow(
            AsyncTexture2D background,
            PopoutSectionState state,
            string title,
            string windowId,
            Point contentSize,
            Func<Task<AccountSnapshot>> refreshAsync,
            Func<int, ItemTooltipFacts> getItemFacts,
            Func<int, CurrencyTooltipFacts> getCurrencyFacts,
            Func<int> opacityPercent,
            Action<int> setOpacityPercent)
            : base(background, contentSize.X, contentSize.Y)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
            _getItemFacts = getItemFacts ?? throw new ArgumentNullException(nameof(getItemFacts));
            _getCurrencyFacts = getCurrencyFacts
                ?? throw new ArgumentNullException(nameof(getCurrencyFacts));
            _opacityPercent = opacityPercent ?? throw new ArgumentNullException(nameof(opacityPercent));
            _setOpacityPercent = setOpacityPercent ?? throw new ArgumentNullException(nameof(setOpacityPercent));

            // A window is never dragged narrower than the table's own
            // five columns, nor shorter than its chrome plus a header band
            // and one row: below either the popout stops being a picture of
            // the plan tab's table, which is the whole point of it.
            _minWindowSize = new Point(
                PopoutLayout.MinContentWidth(state.SectionType) + ChromeWidth,
                PopoutLayout.ChromeHeight
                    + PlanContentHeightMath.ColumnHeaderRowHeight
                    + PlanContentHeightMath.ShoppingRowHeight
                    + ChromeHeight);

            // A 1x1 pixel with a solid fill, the way Views/ModalDialog
            // seats its own window: an asset-texture background at this
            // size overflows, and StandardWindow draws its chrome from its
            // own textures either way.
            BackgroundColor = new Color(30, 30, 30);
            Title = title;
            Id = windowId;
            Parent = GameService.Graphics.SpriteScreen;
            CanResize = true;
            SavesPosition = true;
            SavesSize = true;

            _resizeSettle = new ResizeSettleDebounce(
                RunReellipsis,
                MainThreadMarshal.Run,
                ResizeSettleDebounce.DefaultSettleMs,
                ex => Logger.Warn(ex, "Popout re-ellipsis wait failed"));

            BuildChrome();
            ApplyOpacity(_opacityPercent());
            Rebuild();

            // The base constructor's own layout pass ran before
            // _minWindowSize was assigned, so it could not apply the floor.
            // A plan whose section is empty constructs under it.
            ClampToMinimum();
        }

        void ISectionRelayoutSink.AddRelayout(Action<int> closure)
        {
            _relayoutActions.Add(closure);
        }

        void ISectionRelayoutSink.AddReellipsis(Action<int> closure)
        {
            _reellipsisActions.Add(closure);
        }

        /// <summary>
        /// Nothing in either section renderer asks for this: only the Notes
        /// section builds a row count that depends on width, and no popout
        /// hosts it. A full rebuild is still the honest answer if one ever
        /// does.
        /// </summary>
        void ISectionRelayoutSink.RequestRerenderAfterSettle()
        {
            Rebuild();
        }

        void ISectionRelayoutSink.TrackStickyBand(HeaderBands.FlowBand band, Func<int> rowsHeight)
        {
            if (band.Band == null || band.Spacer == null || rowsHeight == null)
            {
                return;
            }

            _stickyHeaders?.Track(
                band.Band,
                band.Spacer,
                () =>
                {
                    var flow = band.Spacer.Parent;
                    return new StickyHeaderHost.BandGeometry(
                        flow != null && flow.Visible,
                        0,
                        0,
                        band.Band.Width,
                        HeaderBands.RowHeight,
                        HeaderBands.RowHeight + rowsHeight());
                });
        }

        int ISectionRelayoutSink.RelayoutCount => _relayoutActions.Count;

        /// <summary>
        /// Re-reads the state and redraws the table. Called on open, after a
        /// sort click, and after a sync.
        /// </summary>
        internal void Rebuild()
        {
            if (_contentPanel == null)
            {
                return;
            }

            _stickyHeaders?.Clear();
            _relayoutActions.Clear();
            _reellipsisActions.Clear();
            _table?.Dispose();
            _table = null;

            int width = TableWidth();
            _table = new PopoutTable(
                _state, this, _state.Sort, OnSortChanged, _getItemFacts, _getCurrencyFacts)
                .Build(_contentPanel, ContentRegion.Width);

            _laidOutWidth = width;
            SetStatus(IdleStatus());
        }

        private void OnSortChanged()
        {
            Rebuild();
            HoverChainResync.AfterRebuild();
        }

        /// <summary>
        /// The width every registered relayout closure is replayed at. It is
        /// the TABLE's width and not the content box's, because those
        /// closures belong to the section renderer - see
        /// <see cref="PopoutLayout.TableWidth"/>.
        /// </summary>
        private int TableWidth()
        {
            return PopoutLayout.TableWidth(ContentRegion.Width);
        }

        private void BuildChrome()
        {
            _toolbar = new PopoutToolbar(
                this,
                ContentRegion.Width,
                _opacityPercent(),
                _setOpacityPercent,
                ApplyOpacity,
                RefreshAsync);

            _contentPanel = new StickyClipAuthorityFlowPanel(() => _stickyHeaders?.PinnedBandBottom)
            {
                Size = new Point(ContentRegion.Width, ContentHeight()),
                Location = new Point(0, PopoutLayout.ChromeHeight),
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                CanScroll = true,
                Parent = this,
            };

            _stickyHeaders = new StickyHeaderHost(this, _contentPanel);
        }

        /// <summary>
        /// Applies the scale to the whole window. Set on the window and not
        /// on its children, because Views/Rendering/PressFeedback owns a
        /// control's own Opacity while it is pressed and would fight a
        /// second writer for it.
        /// </summary>
        private void ApplyOpacity(int percent)
        {
            Opacity = PopoutOpacity.ToFactor(percent);
        }

        private int ContentHeight()
        {
            int height = ContentRegion.Height - PopoutLayout.ChromeHeight;
            return height > 0 ? height : 0;
        }

        private void SetStatus(string text)
        {
            _toolbar?.SetStatus(text);
        }

        private string IdleStatus()
        {
            int count = _state.Rows.Count;
            if (count == 0)
            {
                return "Nothing left on this list.";
            }

            return count == 1 ? "1 item left." : count + " items left.";
        }

        /// <summary>
        /// The player pressed Refresh. Deliberately ungated by the module's
        /// freshness windows (Services/SnapshotRefreshPolicy): those exist
        /// for refreshes the module decides to run, and this one is a
        /// deliberate press, exactly like the Account Snapshot tab's own
        /// Refresh Now.
        /// </summary>
        private async Task RefreshAsync()
        {
            if (_refreshing)
            {
                return;
            }

            _refreshing = true;
            _toolbar.SetBusy(true);
            SetStatus("Syncing your account...");

            AccountSnapshot snapshot = null;
            bool failed = false;
            try
            {
                snapshot = await _refreshAsync();
            }
            catch (Exception ex)
            {
                failed = true;
                Logger.Warn(ex, "Popout account sync failed");
            }

            if (!MainThreadMarshal.Run(() => FinishRefresh(snapshot, failed)))
            {
                Logger.Warn("Popout sync finished with no main thread to report it on");
            }
        }

        /// <summary>
        /// A null snapshot is not a success with no data: the module hands
        /// one back when another refresh already holds the slot, and a
        /// partial fetch never commits at all. Either way nothing is
        /// applied and the line says the list is unchanged.
        /// </summary>
        private void FinishRefresh(AccountSnapshot snapshot, bool failed)
        {
            if (_disposed)
            {
                return;
            }

            _refreshing = false;
            _toolbar.SetBusy(false);

            if (failed || snapshot == null)
            {
                SetStatus(PopoutSectionState.RefreshFailedStatus());
                return;
            }

            _state.ApplyRefresh(snapshot.Items);
            Rebuild();
            SetStatus(PopoutSectionState.RefreshedStatus(
                snapshot.CapturedAt.ToLocalTime(), _state.Rows.Count));
        }

        public override void RecalculateLayout()
        {
            base.RecalculateLayout();
            if (ClampToMinimum())
            {
                return;
            }

            ApplyLayout();
        }

        /// <summary>
        /// True when it wrote a new Size, which re-enters this method: the
        /// caller must stop rather than lay out against the size it is
        /// leaving.
        /// </summary>
        private bool ClampToMinimum()
        {
            if (Width >= _minWindowSize.X && Height >= _minWindowSize.Y)
            {
                return false;
            }

            Size = new Point(
                Math.Max(Width, _minWindowSize.X), Math.Max(Height, _minWindowSize.Y));
            return true;
        }

        protected override Point HandleWindowResize(Point newSize)
        {
            return new Point(
                Math.Max(newSize.X, _minWindowSize.X), Math.Max(newSize.Y, _minWindowSize.Y));
        }

        /// <summary>
        /// Width and position only. The measuring pass that re-ellipsizes a
        /// truncated name is behind the module's settle debounce, on the
        /// same rule the Crafting Plan tab follows.
        /// </summary>
        private void ApplyLayout()
        {
            if (_toolbar == null || _contentPanel == null)
            {
                return;
            }

            int region = ContentRegion.Width;
            _toolbar.Relayout(region);
            _contentPanel.Size = new Point(region, ContentHeight());

            int width = TableWidth();
            if (width == _laidOutWidth)
            {
                return;
            }

            _laidOutWidth = width;
            for (int i = 0; i < _relayoutActions.Count; i++)
            {
                _relayoutActions[i](width);
            }

            _resizeSettle.Schedule();
        }

        private void RunReellipsis()
        {
            if (_disposed)
            {
                return;
            }

            int width = _laidOutWidth;
            for (int i = 0; i < _reellipsisActions.Count; i++)
            {
                _reellipsisActions[i](width);
            }
        }

        /// <summary>
        /// Runs after the base, which is where a persisted position and
        /// size are restored. A size saved on a wider client, or a position
        /// saved on a taller one, is corrected against the screen in front
        /// of the player now - the rule is Services/WindowPlacement's.
        /// </summary>
        public override void Show()
        {
            base.Show();
            FitToScreen();
        }

        private void FitToScreen()
        {
            var screen = GameService.Graphics.SpriteScreen;
            if (screen == null)
            {
                return;
            }

            var fitted = new Point(
                WindowPlacement.ClampExtent(Width, _minWindowSize.X, screen.Width),
                WindowPlacement.ClampExtent(Height, _minWindowSize.Y, screen.Height));
            if (fitted != Size)
            {
                Size = fitted;
            }

            var clamped = new Point(
                WindowPlacement.ClampAxis(Location.X, Width, screen.Width),
                WindowPlacement.ClampAxis(Location.Y, Height, screen.Height));
            if (clamped != Location)
            {
                Location = clamped;
            }
        }

        public override void Hide()
        {
            FocusRelease.ReleaseWithin(this);
            base.Hide();
        }

        protected override void DisposeControl()
        {
            _disposed = true;
            _resizeSettle.Cancel();
            _stickyHeaders?.Clear();
            _relayoutActions.Clear();
            _reellipsisActions.Clear();

            _toolbar?.Dispose();

            FocusRelease.ReleaseWithin(this);
            base.DisposeControl();
        }
    }
}
