using System;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    internal class ModalDialog : IDisposable
    {
        private const string WindowId = "TaimisToolbench_ModalDialog_c4f19a";

        // Every caller shares this one dialog, and every one of them is
        // confirming or acknowledging something - the word the window ships.
        private const string TitleText = "Confirm";

        // Sizing, arrangement and every clamp: Services/DialogLayoutMath,
        // which is measured against the message and the button labels this
        // Show was handed. Nothing here is a geometry constant any more.
        private readonly DialogWindow _window;
        private readonly ModuleSettings _settings;

        // The surface a confirm has to freeze while it is up, resolved
        // lazily: Module builds this dialog before it builds the module
        // window. Null (no backdrop) is a supported shape - the dialog
        // still works, it just is not modal, which is what every caller
        // got before this existed.
        private readonly Func<Control> _blockedSurface;

        // Built on the FIRST Show(), not in the constructor, and that is
        // load-bearing - see ModalBackdrop's z-order note: it has to be a
        // later SpriteScreen child than the window it blocks.
        private ModalBackdrop _backdrop;

        private bool _isShowing;
        private bool _suppressMoved;
        private Action _onConfirm;
        private Action _onCancel;

        public ModalDialog(ModuleSettings settings, Func<Control> blockedSurface = null)
        {
            _settings = settings;
            _blockedSurface = blockedSurface;

            // Use a 1x1 pixel texture to avoid overflow from large asset
            // textures. StandardWindow chrome (title bar, borders, close
            // button) uses its own built-in textures and does not depend on
            // the background parameter. The size below is the floor and is
            // re-seated by every Show before the window is ever visible.
            _window = new DialogWindow(
                new AsyncTexture2D(ContentService.Textures.Pixel),
                DialogLayoutMath.MinContentWidth,
                DialogLayoutMath.MinContentHeight(0))
            {
                BackgroundColor = new Color(30, 30, 30),
                Parent = GameService.Graphics.SpriteScreen,
                // Blank on purpose: Blish paints WindowBase2's own Title
                // left-aligned at a fixed indent with no alignment control
                // (decompiled 1.3.0, PaintTitleText), so the centered title
                // this dialog ships is drawn by ShowCore instead - it
                // renders TitleText as a Label of its own.
                Title = string.Empty,
                Id = WindowId,
                TopMost = true,
            };

            _window.Moved += OnWindowMoved;

            // Resets _isShowing - and runs the caller's cancel callback -
            // whenever the window's own Visible=false transition completes,
            // not just when the Confirm/Cancel StandardButton handlers below
            // run. WindowBase2's built-in title-bar X button and Escape key
            // both call Hide() directly (CanClose/CanCloseWithEscape default
            // true, never overridden here), bypassing those handlers
            // entirely - without this, dismissing the dialog that way would
            // leave _isShowing stuck true and every later Show() call, from
            // every caller of this shared instance, would silently no-op for
            // the rest of the session. Same subscription, same reason, as
            // ApiAccessDialog's.
            _window.Hidden += OnWindowHidden;
        }

        // confirmText is required so every caller states its own verb
        // ("Regenerate", "Delete") - a default here would hand an
        // unrelated caller the wrong label on a destructive confirm.
        // cancelText is optional and defaults to the plain "Cancel" every
        // existing caller wants: for those, the second button really does
        // abandon the operation. It exists for the callers whose second
        // button is a CHOICE rather than an escape - the Settings tab's
        // unsaved-changes prompt cannot put the user back where they were
        // (see KNOWN-ISSUES #51), so a button labelled
        // "Cancel" there would promise something it does not do.
        // Returns false when another caller's dialog is already on screen,
        // so a caller that arms state for the dialog's lifetime (MainView
        // disables its Snapshot buttons) knows not to arm it.
        public bool Show(string message, Action onConfirm, Action onCancel, string confirmText, string cancelText = "Cancel")
        {
            return ShowCore(message, onConfirm, onCancel, confirmText, cancelText, acknowledgeOnly: false);
        }

        /// <summary>
        /// A dialog with nothing to confirm: one button, which only
        /// dismisses it. For telling the user why the thing they just did
        /// produced no result - the status line under the toolbar says the
        /// same thing and keeps saying it, but it is nowhere near the
        /// button they pressed, so on its own it reads as no response.
        /// <para>
        /// Same refusal contract as <see cref="Show"/>: false when another
        /// caller's dialog is already up. A refused acknowledgement is
        /// simply not shown - there is no state to unwind, and the status
        /// line has the message either way.
        /// </para>
        /// </summary>
        public bool ShowAcknowledgement(string message, string dismissText = "OK")
        {
            return ShowCore(message, null, null, dismissText, null, acknowledgeOnly: true);
        }

        private bool ShowCore(
            string message, Action onConfirm, Action onCancel,
            string confirmText, string cancelText, bool acknowledgeOnly)
        {
            if (_isShowing)
            {
                return false;
            }

            _isShowing = true;
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            // Clear old children
            foreach (var child in _window.Children.ToArray())
            {
                child.Dispose();
            }

            // Pre-wrapped, not left to the Label control's WrapText
            // property, for the reason ApiAccessDialog documents: that
            // property pins its wrap width at the control's first internal
            // layout pass, which runs before a Width assigned later in the
            // same object initializer takes effect.
            //
            // Measured in Caption for the buttons, not in the message's
            // Body: StandardButton (FeedbackButton's base) draws its own
            // label in DefaultFont14 and exposes no Font seam, exactly like
            // Checkbox - see UiFonts' note on the exclusions. The title is
            // measured in Display, the face WindowBase2 paints it in.
            var font = UiFonts.Body;
            var measure = LabelHelpers.MeasureWith(font);
            var buttonMeasure = LabelHelpers.MeasureWith(UiFonts.Caption);
            int lineHeight = font.LineHeight > 0 ? font.LineHeight : 1;
            string cancelLabel = string.IsNullOrEmpty(cancelText) ? "Cancel" : cancelText;
            int titleWidth = LabelHelpers.MeasureWith(UiFonts.Display)(TitleText);

            var screen = GameService.Graphics.SpriteScreen;
            var paragraphs = DialogLayoutMath.Paragraphs(message);
            var layout = DialogLayoutMath.Measure(
                paragraphs,
                measure,
                lineHeight,
                titleWidth,
                buttonMeasure(confirmText ?? ""),
                acknowledgeOnly ? -1 : buttonMeasure(cancelLabel),
                DialogLayoutMath.MaxContentWidth(screen.Width, DialogWindow.ChromeWidth),
                DialogLayoutMath.MaxContentHeight(screen.Height, DialogWindow.ChromeHeight, lineHeight));

            // Before the children: they are placed against the region this
            // call establishes.
            _window.Resize(layout.ContentWidth, layout.ContentHeight);

            // The window's title, drawn by us because Blish paints its own
            // left-aligned at a fixed indent with no alignment control
            // (PaintTitleText). Same face and the same ColonialWhite, and
            // the seat is derived from this label's own measured height
            // (DialogLayoutMath.TitleLineY) because the vendor's own draw
            // centres the title in the title-bar rect rather than starting
            // at its origin - so the centred word reads exactly where the
            // left-aligned one did, only moved. AutoSize is applied in this
            // initialiser, and Label recalculates synchronously on each of
            // those setters, so Width and Height are live on the next line.
            // ClipsBounds off: the seat is above the content region, and
            // Container.PaintChildren scissors a clipping child to it.
            var title = new Label()
            {
                Text = TitleText,
                Font = UiFonts.Display,
                TextColor = ContentService.Colors.ColonialWhite,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                ClipsBounds = false,
            };
            title.Location = _window.ContentLocationFor(new Point(
                DialogLayoutMath.TitleX(_window.Width, title.Width),
                DialogLayoutMath.TitleLineY(title.Height)));
            title.Parent = _window;

            // One Label per physical line, each centred on the content box.
            // A single multi-line Label centres only the BLOCK: Blish paints
            // the whole string in one DrawString and the pen returns to the
            // block's own left edge at every newline, so every line after
            // the first sat left-aligned under the one above it whatever
            // alignment the Label carried.
            //
            // Auto-size BOTH axes and parent last - ApiAccessDialog's proven
            // AddWrappedLine shape. A fixed Width with AutoSizeHeight takes
            // Blish's stale-layout-pass measure and clipped the second
            // wrapped line mid-glyph, seen in a verification screenshot.
            // Each line centers by its own measured width instead.
            // Every block, not just the first. DialogLayoutMath has always
            // sized and seated N paragraphs - its own ParagraphGap and
            // per-block Y - and rendering only Blocks[0] silently dropped
            // whatever a caller put after a blank line.
            foreach (var block in layout.Blocks)
            {
                for (int i = 0; i < block.Lines.Count; i++)
                {
                    var lineLabel = new Label()
                    {
                        Text = block.Lines[i],
                        Font = font,
                        AutoSizeWidth = true,
                        AutoSizeHeight = true,
                    };
                    lineLabel.Location = new Point(
                        DialogLayoutMath.LineX(layout.ContentWidth, lineLabel.Width),
                        block.Y + (i * lineHeight));
                    lineLabel.Parent = _window;

                    // Only when text was actually dropped - a tooltip repeating
                    // the visible sentence is noise.
                    TooltipFacility.ApplyPlain(lineLabel, block.Truncated ? message : null);
                }
            }

            var confirmBtn = new FeedbackButton()
            {
                Text = confirmText,
                Size = new Point(layout.ConfirmWidth, DialogLayoutMath.ButtonHeight),
                Location = new Point(layout.ConfirmX, layout.ButtonY),
                Parent = _window,
            };
            confirmBtn.Click += (_, __) => Dismiss(confirmed: true);

            if (!acknowledgeOnly)
            {
                var cancelBtn = new FeedbackButton()
                {
                    Text = cancelLabel,
                    Size = new Point(layout.CancelWidth, DialogLayoutMath.ButtonHeight),
                    Location = new Point(layout.CancelX, layout.ButtonY),
                    Parent = _window,
                };
                cancelBtn.Click += (_, __) => Dismiss(confirmed: false);
            }

            // Position: restore saved location, or center on first show
            int sx = _settings.ModalDialogX.Value;
            int sy = _settings.ModalDialogY.Value;

            if (sx < 0 || sy < 0)
            {
                sx = (screen.Width - _window.Width) / 2;
                sy = (screen.Height - _window.Height) / 2;
                _settings.ModalDialogX.Value = sx;
                _settings.ModalDialogY.Value = sy;
            }
            else
            {
                // Clamped without writing back, where the pre-sizing code
                // re-centered and overwrote. The box now follows its
                // message, so a saved corner that only a taller dialog
                // overflows must not cost the user the spot they dragged
                // this to.
                sx = Math.Min(sx, Math.Max(0, screen.Width - _window.Width));
                sy = Math.Min(sy, Math.Max(0, screen.Height - _window.Height));
            }

            _window.Location = new Point(sx, sy);

            ShowBackdrop();
            _window.Show();
            return true;
        }

        /// <summary>
        /// Programmatic close: drops the pending callbacks without running
        /// either of them. Clearing _isShowing first is what stops
        /// OnWindowHidden below from reading this as a user cancel.
        /// </summary>
        public void Hide()
        {
            _isShowing = false;
            _onConfirm = null;
            _onCancel = null;
            _backdrop?.Hide();
            _window.Hide();
        }

        /// <summary>
        /// Raises the input-eating layer beneath the dialog. Deferred to
        /// the first Show() because the surface it blocks does not exist
        /// when this dialog is constructed, AND because it must be a later
        /// child of SpriteScreen than that surface to win the sibling-index
        /// tiebreak (see ModalBackdrop).
        /// </summary>
        private void ShowBackdrop()
        {
            if (_blockedSurface == null)
            {
                return;
            }

            if (_backdrop == null)
            {
                _backdrop = new ModalBackdrop(_window, _blockedSurface);
            }

            // Bounds and z-order before Visible: a frame that shows the
            // backdrop at a stale rect is a frame where the click it exists
            // to eat gets through.
            _backdrop.Sync();
            _backdrop.Show();
        }

        public void Dispose()
        {
            // Unsubscribe BEFORE hiding: teardown must not fire a caller's
            // cancel callback into controls the module is already disposing.
            _window.Hidden -= OnWindowHidden;
            _window.Moved -= OnWindowMoved;
            _backdrop?.Dispose();
            _backdrop = null;
            _window.Hide();
            _window.Dispose();
        }

        /// <summary>
        /// The single exit path for both buttons and for the window's own
        /// X/Escape dismissal. Callbacks are read into locals and cleared
        /// before either runs, so a callback that reopens the dialog gets a
        /// clean slate and cannot see the previous request's handlers.
        /// </summary>
        private void Dismiss(bool confirmed)
        {
            if (!_isShowing)
            {
                return;
            }

            _isShowing = false;
            var onConfirm = _onConfirm;
            var onCancel = _onCancel;
            _onConfirm = null;
            _onCancel = null;

            try
            {
                if (confirmed)
                {
                    onConfirm?.Invoke();
                }
                else
                {
                    onCancel?.Invoke();
                }
            }
            finally
            {
                // The window is dropped AFTER the callback, and only if the
                // callback did not re-arm this dialog by calling Show().
                // Measured in the vendored 1.3.0 binary: WindowBase2.Hide()
                // does NOT set Visible=false - it resumes the shared 0.2s
                // reflecting fade tween, whose OnComplete sets Visible=false
                // and raises Hidden - while WindowBase2.Show() begins
                // "BringWindowToFront(); if (Visible) return;". Hiding first
                // therefore made a re-raised dialog paint its new children
                // into a window already fading out: Show() early-returned,
                // the fade finished ~0.2s later, and the Hidden event
                // dismissed the replacement as a cancel. It read as a flash.
                // Leaving the window visible lets Show()'s early return hand
                // the second request the same on-screen window with the
                // replaced content. try/finally so a throwing callback still
                // closes the dialog.
                if (!_isShowing)
                {
                    _backdrop?.Hide();
                    _window.Hide();
                }
            }
        }

        // Fires on every Visible=false transition, whichever path caused it.
        // The button handlers and Hide() clear _isShowing before hiding, so
        // for those this is a no-op and only the title-bar X and Escape key
        // reach the Dismiss below - which is what makes them behave like
        // Cancel instead of stranding both _isShowing and whatever state the
        // caller armed for the dialog's lifetime.
        private void OnWindowHidden(object sender, EventArgs e)
        {
            Dismiss(confirmed: false);
        }

        private void OnWindowMoved(object sender, MovedEventArgs e)
        {
            if (_suppressMoved)
            {
                return;
            }

            var screen = GameService.Graphics.SpriteScreen;
            int maxX = Math.Max(0, screen.Width - _window.Width);
            int maxY = Math.Max(0, screen.Height - _window.Height);

            int clampedX = Math.Min(Math.Max(0, e.CurrentLocation.X), maxX);
            int clampedY = Math.Min(Math.Max(0, e.CurrentLocation.Y), maxY);

            if (clampedX != e.CurrentLocation.X || clampedY != e.CurrentLocation.Y)
            {
                _suppressMoved = true;
                _window.Location = new Point(clampedX, clampedY);
                _suppressMoved = false;
            }

            _settings.ModalDialogX.Value = _window.Location.X;
            _settings.ModalDialogY.Value = _window.Location.Y;
        }
    }
}
