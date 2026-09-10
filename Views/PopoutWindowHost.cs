using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// Owns the popout windows and the lists behind them.
    /// <para>
    /// It is held by Module and torn down by Module.Unload, not by the
    /// Crafting Plan tab: the windows are parented to the sprite screen so
    /// they can outlive the module window, and nothing in a view's control
    /// tree would ever reach them. The tab only asks this to open one.
    /// </para>
    /// </summary>
    internal sealed class PopoutWindowHost : IDisposable
    {
        private static readonly Logger Logger = Logger.GetLogger<PopoutWindowHost>();

        private readonly Func<AsyncTexture2D> _background;
        private readonly Func<Task<AccountSnapshot>> _refreshAsync;
        private readonly Func<AccountSnapshot> _getSnapshot;
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;
        private readonly ModuleSettings _settings;

        private readonly Dictionary<PlanSectionType, PopoutSectionState> _states =
            new Dictionary<PlanSectionType, PopoutSectionState>();

        private readonly Dictionary<PlanSectionType, PopoutWindow> _windows =
            new Dictionary<PlanSectionType, PopoutWindow>();

        private PlanViewModel _plan;

        internal PopoutWindowHost(
            Func<AsyncTexture2D> background,
            Func<Task<AccountSnapshot>> refreshAsync,
            Func<AccountSnapshot> getSnapshot,
            Func<int, ItemStatBlock> getItemStatBlock,
            ModuleSettings settings)
        {
            _background = background ?? throw new ArgumentNullException(nameof(background));
            _refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
            _getSnapshot = getSnapshot ?? throw new ArgumentNullException(nameof(getSnapshot));
            _getItemStatBlock = getItemStatBlock;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _states[PlanSectionType.ShoppingList] =
                new PopoutSectionState(PlanSectionType.ShoppingList);
            _states[PlanSectionType.CraftingSteps] =
                new PopoutSectionState(PlanSectionType.CraftingSteps);
        }

        /// <summary>
        /// Whether this section can be popped out right now, which is what
        /// the plan tab's header button reads to decide if it shows at all.
        /// </summary>
        internal static bool Supports(PlanSectionType sectionType)
        {
            return sectionType == PlanSectionType.ShoppingList
                || sectionType == PlanSectionType.CraftingSteps;
        }

        /// <summary>
        /// Hands the host the plan now on screen. A different plan is a
        /// different list, so its sections are adopted afresh and the ticks
        /// and sync baseline of the previous one are dropped. The same plan
        /// re-rendered - a sort click, a tree override - changes nothing
        /// here, which is what keeps a popout's ticks through one.
        /// </summary>
        internal void PublishPlan(PlanViewModel plan)
        {
            if (ReferenceEquals(plan, _plan))
            {
                return;
            }

            _plan = plan;
            var baseline = PopoutRemaining.CountItems(_getSnapshot()?.Items);

            foreach (var pair in _states)
            {
                pair.Value.Adopt(RowsFor(plan, pair.Key), baseline);
            }

            foreach (var pair in _windows)
            {
                pair.Value.Rebuild();
            }
        }

        /// <summary>
        /// Opens the popout for a section, or brings it back to the front
        /// when it is already open.
        /// </summary>
        internal void Open(PlanSectionType sectionType)
        {
            if (!Supports(sectionType))
            {
                return;
            }

            PopoutWindow window;
            if (_windows.TryGetValue(sectionType, out window))
            {
                window.Show();
                window.BringWindowToFront();
                return;
            }

            try
            {
                window = Create(sectionType);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Popout window could not be opened");
                ModuleLog.Shared.Write(
                    ModuleLogLevel.Warn,
                    "ui",
                    "Pop out window failed to open: " + ex.GetType().Name + " - " + ex.Message);
                return;
            }

            _windows[sectionType] = window;
            window.Show();
            window.BringWindowToFront();
        }

        private PopoutWindow Create(PlanSectionType sectionType)
        {
            var state = _states[sectionType];
            var contentSize = new Point(
                PopoutLayout.MinContentWidth(sectionType),
                PopoutLayout.DefaultContentHeight(sectionType, state.Rows));

            var window = new PopoutWindow(
                _background(),
                state,
                TitleFor(sectionType),
                "Module_Popout_" + sectionType.ToString(),
                contentSize,
                _refreshAsync,
                _getItemStatBlock,
                () => _settings.GetClampedPopoutOpacityPercent(sectionType),
                percent => _settings.SetPopoutOpacityPercent(sectionType, percent));

            var screen = GameService.Graphics.SpriteScreen;
            if (screen != null)
            {
                window.Location = new Point(
                    Math.Max(0, (screen.Width - window.Width) / 2),
                    Math.Max(0, (screen.Height - window.Height) / 3));
            }

            return window;
        }

        private static string TitleFor(PlanSectionType sectionType)
        {
            return sectionType == PlanSectionType.CraftingSteps
                ? "Crafting Steps"
                : "Shopping List";
        }

        private static IReadOnlyList<PlanRowViewModel> RowsFor(
            PlanViewModel plan, PlanSectionType sectionType)
        {
            if (plan?.Sections == null)
            {
                return new List<PlanRowViewModel>();
            }

            foreach (var section in plan.Sections)
            {
                if (section != null && section.SectionType == sectionType)
                {
                    return section.Rows ?? new List<PlanRowViewModel>();
                }
            }

            return new List<PlanRowViewModel>();
        }

        /// <summary>
        /// Disposes both windows. Called from Module.Unload: these are
        /// sprite-screen children, so disposing the module window does not
        /// reach them and one left behind would outlive the module.
        /// </summary>
        public void Dispose()
        {
            foreach (var pair in _windows)
            {
                try
                {
                    pair.Value.Hide();
                    pair.Value.Dispose();
                }
                catch (Exception ex)
                {
                    // One window that will not go must not strand the other
                    // on screen with a disabled module behind it.
                    Logger.Warn(ex, "Popout window teardown failed");
                }
            }

            _windows.Clear();
        }
    }
}
