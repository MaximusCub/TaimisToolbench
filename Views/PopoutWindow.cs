using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
    /// <para>
    /// Dressed in the module window's own art and regions
    /// (<see cref="ModuleWindowArt"/>), which Blish scales onto whatever
    /// size the window is at.
    /// </para>
    /// </summary>
    internal sealed class PopoutWindow : StandardWindow, ISectionRelayoutSink
    {
        private static readonly Logger Logger = Logger.GetLogger<PopoutWindow>();

        /// <summary>
        /// What the window costs outside its content box, from the regions
        /// Views/ModuleWindowArt hands Blish. Both terms are the module
        /// window's own, so a popout's frame is the frame the player
        /// already knows.
        /// </summary>
        private const int ChromeWidth =
            WindowSizing.WindowContentLeftInset + WindowSizing.WindowContentRightMargin;

        private const int ChromeHeight =
            WindowSizing.WindowContentTop + WindowSizing.WindowContentBottomMargin;

        /// <summary>
        /// Left rule of the window's own title. WindowBase2 draws a Title
        /// 78px in, which is the seat a TabbedWindow2's sidebar and emblem
        /// fill; a popout has neither, so the word reads as indented. This
        /// window leaves Blish's Title unset and draws its own on the same
        /// rule the table under it starts at.
        /// </summary>
        private const int TitleX = WindowSizing.WindowContentLeftInset;

        private readonly PopoutSectionState _state;
        private readonly Func<Task<AccountSnapshot>> _refreshAsync;
        private readonly Func<int, ItemTooltipFacts> _getItemFacts;
        private readonly Func<int, CurrencyTooltipFacts> _getCurrencyFacts;
        private readonly Func<int> _opacityPercent;
        private readonly Action<int> _setOpacityPercent;
        private readonly Point _minWindowSize;
        private readonly string _titleText;

        private readonly List<Action<int>> _relayoutActions = new List<Action<int>>();
        private readonly List<Action<int>> _reellipsisActions = new List<Action<int>>();
        private readonly ResizeSettleDebounce _resizeSettle;

        private PopoutToolbar _toolbar;
        private StickyClipAuthorityFlowPanel _contentPanel;
        private StickyHeaderHost _stickyHeaders;
        private Panel _table;

        private int _laidOutWidth = -1;
        private bool _refreshing;

        // The percent UpdateContainer holds the window under, cached rather
        // than read from the settings every frame. Written only by
        // ApplyOpacity, which is the one path that changes the setting.
        private int _appliedOpacityPercent = PopoutOpacity.DefaultPercent;

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
            : base(background, ModuleWindowArt.WindowRegion(), ModuleWindowArt.ContentRegion())
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
            _getItemFacts = getItemFacts ?? throw new ArgumentNullException(nameof(getItemFacts));
            _getCurrencyFacts = getCurrencyFacts
                ?? throw new ArgumentNullException(nameof(getCurrencyFacts));
            _opacityPercent = opacityPercent ?? throw new ArgumentNullException(nameof(opacityPercent));
            _setOpacityPercent = setOpacityPercent ?? throw new ArgumentNullException(nameof(setOpacityPercent));

            // A window is never dragged narrower than the table's own
            // columns, nor shorter than its chrome plus a header band and
            // one row: below either the popout stops being a picture of
            // the plan tab's table, which is the whole point of it.
            //
            // The toolbar strip has a floor of its own, and it is not
            // implied by the table's: Crafting Steps carries the narrower
            // table of the two, so its columns would allow a width at which
            // the Refresh button overlaps the opacity readout above them.
            // The window takes whichever floor is higher.
            _minWindowSize = new Point(
                Math.Max(
                    PopoutLayout.MinContentWidth(state.SectionType),
                    PopoutToolbar.MinContentWidth()) + ChromeWidth,
                PopoutLayout.ChromeHeight
                    + PlanContentHeightMath.ColumnHeaderRowHeight
                    + PlanContentHeightMath.ShoppingRowHeight
                    + PopoutLayout.ContentBottomPadding
                    + ChromeHeight);

            // The base constructor sized the window from the texture's own
            // window region. Written here, after the floor exists, so the
            // window is never observably below it.
            Size = new Point(
                Math.Max(contentSize.X + ChromeWidth, _minWindowSize.X),
                Math.Max(contentSize.Y + ChromeHeight, _minWindowSize.Y));

            _titleText = title;

            // Emptied, not left unset: WindowBase2's Title defaults to the
            // literal "No Title", which it would paint at its own 78px seat
            // beside the one this window draws.
            Title = string.Empty;
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
        }

        /// <summary>
        /// Builds the chrome and the first table. Separate from the
        /// constructor so that a failure here leaves the caller holding the
        /// window and able to dispose it. The constructor parents to the
        /// sprite screen, so a throw inside one strands a half-built child
        /// there that nothing references and nothing can reach.
        /// </summary>
        internal void Initialize()
        {
            BuildChrome();
            ApplyOpacity(_opacityPercent());
            Rebuild();
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
            _appliedOpacityPercent = PopoutOpacity.Clamp(percent);
            Opacity = PopoutOpacity.ToFactor(_appliedOpacityPercent);
        }

        /// <summary>
        /// Holds the window under the opacity the player stored.
        /// WindowBase2 drives Opacity to 1 on every Show through a tween
        /// this class cannot reach, which is what used to drop a stored
        /// setting the moment the window was reopened. Capping rather than
        /// assigning leaves the Hide tween's descent to 0 alone, so the
        /// window still fades out and still becomes invisible.
        /// </summary>
        public override void UpdateContainer(GameTime gameTime)
        {
            base.UpdateContainer(gameTime);
            Opacity = PopoutOpacity.CapToStored(Opacity, _appliedOpacityPercent);
        }

        public override void PaintBeforeChildren(SpriteBatch spriteBatch, Rectangle bounds)
        {
            base.PaintBeforeChildren(spriteBatch, bounds);
            if (string.IsNullOrEmpty(_titleText))
            {
                return;
            }

            spriteBatch.DrawStringOnCtrl(
                this,
                _titleText,
                UiFonts.Display,
                new Rectangle(TitleX, 0, Width - TitleX, WindowSizing.TitleBarHeight),
                ContentService.Colors.ColonialWhite);
        }

        private int ContentHeight()
        {
            int height = ContentRegion.Height
                - PopoutLayout.ChromeHeight
                - PopoutLayout.ContentBottomPadding;
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
