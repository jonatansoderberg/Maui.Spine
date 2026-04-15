using Android.Content.Res;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.View;
using Google.Android.Material.BottomSheet;
using Google.Android.Material.Shape;
using Microsoft.Maui.Controls.Platform;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Presentation;
using Plugin.Maui.Spine.Sheets;
using AView = Android.Views.View;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace Plugin.Maui.Spine;

internal static class BottomSheetPageExtensions
{
    internal static Action? ActiveBottomSheetDismiss { get; private set; }

    internal static event Action? ActiveBottomSheetChanged;

    internal static void DismissActiveBottomSheet() => ActiveBottomSheetDismiss?.Invoke();

    internal static async Task<bool> DisplayBottomSheet(
        this MauiPage page,
        Func<IView> bottomSheetFactory,
        Action<BottomSheetBuilder>? builder = null)
    {
        var bottomSheetContent = bottomSheetFactory();
        var tcs = new TaskCompletionSource<bool>();

        var mauiContext = page.Handler?.MauiContext
            ?? throw new InvalidOperationException("MauiContext is null");

        var activity = mauiContext.Context as AppCompatActivity
            ?? throw new InvalidOperationException("Activity is null");

        var bottomSheetBuilder = new BottomSheetBuilder();
        builder?.Invoke(bottomSheetBuilder);

        // ── Dismiss guard ────────────────────────────────────────────────────────
        async Task<bool> CanDismissAsync()
        {
            if (bottomSheetContent is NavigationRegion region
                && region.BindingContext is NavigationRegionViewModel regionVm
                && regionVm.CurrentRegionViewModel is ViewModelBase currentVm)
            {
                return await currentVm.OnCloseRequestedAsync();
            }

            if (bottomSheetContent is BindableObject bo && bo.BindingContext is ViewModelBase vm)
                return await vm.OnCloseRequestedAsync();

            return true;
        }

        // ── Back guard ────────────────────────────────────────────────────────────
        // Returns true if the back press was consumed by in-sheet navigation.
        async Task<bool> HandleBackAsync()
        {
            if (bottomSheetContent is NavigationRegion region
                && region.BindingContext is NavigationRegionViewModel regionVm
                && regionVm.BackEnabled())
            {
                await regionVm.BackAsync();
                return true;
            }

            return false;
        }

        // ── Detent resolution ────────────────────────────────────────────────────
        var displayMetrics = activity.Resources!.DisplayMetrics!;
        var screenHeightPx = displayMetrics.HeightPixels;
        var density        = (double)displayMetrics.Density;

        double ResolveDetentHeightPx(SheetDetent detent)
        {
            if (detent.AbsoluteHeight.HasValue) return detent.AbsoluteHeight.Value * density;
            if (detent.Percentage.HasValue)     return screenHeightPx * detent.Percentage.Value;
            return screenHeightPx * 0.5;
        }

        var allowedDetents = bottomSheetBuilder.AllowedDetents.Count > 0
            ? bottomSheetBuilder.AllowedDetents
            : new List<SheetDetent> { SheetDetent.MediumDetent };
        var selectedDetent   = bottomSheetBuilder.SelectedDetent ?? allowedDetents[0];
        var sortedDetents    = allowedDetents.OrderBy(d => ResolveDetentHeightPx(d)).ToList();
        var selectedHeightPx = ResolveDetentHeightPx(selectedDetent);

        SpineBottomSheetDialog? dialog = null;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            dialog = new SpineBottomSheetDialog(activity, CanDismissAsync, HandleBackAsync, tcs);

            // ── Overlay ──────────────────────────────────────────────────────────
            if (bottomSheetBuilder.BackgroundPageOverlay == BackgroundPageOverlay.None)
                dialog.Window?.ClearFlags(WindowManagerFlags.DimBehind);

            // ── Drag handle + content wrapper ────────────────────────────────────
            // Detach the MAUI native view from any previous dialog's wrapper so it
            // can be safely re-parented on every open (fixes "can't reopen" bug).
            var nativeContent = bottomSheetContent.ToPlatform(mauiContext);
            (nativeContent.Parent as ViewGroup)?.RemoveView(nativeContent);


            nativeContent.SetBackgroundColor(Android.Graphics.Color.Transparent);

            var outerWrapper = BuildSheetWrapper(activity, nativeContent, density);

            // Pass MATCH_PARENT params so Material's wrapInBottomSheet adds the
            // wrapper to design_bottom_sheet with full dimensions.
            dialog.SetContentView(outerWrapper,
                new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent));

            // Force design_bottom_sheet to MATCH_PARENT height so the
            // BottomSheetBehavior can position the full-height frame at any detent
            // offset. Without this the frame stays at WRAP_CONTENT (~content height)
            // and leaves an empty dark gap below the content.
            // Accessed via outerWrapper.Parent to avoid a resource-ID dependency.
            if (outerWrapper.Parent is ViewGroup sheetView
                && sheetView.LayoutParameters is { } sheetLp)
            {
                sheetLp.Height = ViewGroup.LayoutParams.MatchParent;
                sheetView.LayoutParameters = sheetLp;
            }

            dialog.SetCancelable(true);
            dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize);

            // ── Behavior ─────────────────────────────────────────────────────────
            var behavior = dialog.Behavior;
            behavior.Draggable     = true;
            behavior.FitToContents = false;
            behavior.Hideable      = true;

            var lastSettledState = BottomSheetBehavior.StateHalfExpanded;

            if (sortedDetents.Count == 1)
            {
                // Single detent: anchor at halfExpandedRatio, skip collapsed.
                var heightPx = (int)ResolveDetentHeightPx(sortedDetents[0]);
                behavior.SkipCollapsed     = true;
                behavior.PeekHeight        = heightPx;
                behavior.HalfExpandedRatio = Math.Clamp(
                    (float)(heightPx / (double)screenHeightPx), 0.01f, 0.99f);
                behavior.ExpandedOffset    = Math.Max(0, screenHeightPx - heightPx);
                lastSettledState           = BottomSheetBehavior.StateHalfExpanded;
                behavior.State             = BottomSheetBehavior.StateHalfExpanded;
            }
            else if (sortedDetents.Count == 2)
            {
                // Two detents: collapsed ↔ half-expanded.
                var smallPx = (int)ResolveDetentHeightPx(sortedDetents[0]);
                var largePx = (int)ResolveDetentHeightPx(sortedDetents[1]);
                behavior.SkipCollapsed     = false;
                behavior.PeekHeight        = smallPx;
                behavior.HalfExpandedRatio = Math.Clamp(
                    (float)(largePx / (double)screenHeightPx), 0.01f, 0.99f);
                behavior.ExpandedOffset    = Math.Max(0, screenHeightPx - largePx);
                lastSettledState = selectedHeightPx <= smallPx
                    ? BottomSheetBehavior.StateCollapsed
                    : BottomSheetBehavior.StateHalfExpanded;
                behavior.State = lastSettledState;
            }
            else
            {
                // Three+ detents: smallest → collapsed, middle → half-expanded,
                // largest → expanded.
                var smallPx = (int)ResolveDetentHeightPx(sortedDetents[0]);
                var midPx   = (int)ResolveDetentHeightPx(sortedDetents[sortedDetents.Count / 2]);
                var largePx = (int)ResolveDetentHeightPx(sortedDetents[^1]);
                behavior.SkipCollapsed     = false;
                behavior.PeekHeight        = smallPx;
                behavior.HalfExpandedRatio = Math.Clamp(
                    (float)(midPx / (double)screenHeightPx), 0.01f, 0.99f);
                behavior.ExpandedOffset    = Math.Max(0, screenHeightPx - largePx);
                lastSettledState = GetClosestState(selectedHeightPx, smallPx, midPx, largePx);
                behavior.State   = lastSettledState;
            }

            dialog.SetBehaviorAndStateProvider(behavior, () => lastSettledState);

            // Track the last stable state so a rejected cancel can snap back to it.
            behavior.AddBottomSheetCallback(new SheetStateCallback(
                onStateChanged: (_, state) =>
                {
                    if (state == BottomSheetBehavior.StateExpanded
                        || state == BottomSheetBehavior.StateHalfExpanded
                        || state == BottomSheetBehavior.StateCollapsed)
                    {
                        lastSettledState = state;
                    }
                }
            ));

            dialog.Show();

            // ── Material container shape ─────────────────────────────────────────
            // Apply rounded top corners directly to the design_bottom_sheet container
            // so the shape is authoritative and cannot be overridden by any child view.
            var sheetId     = activity.Resources?.GetIdentifier("design_bottom_sheet", "id", activity.PackageName) ?? 0;
            var bottomSheet = sheetId != 0 ? dialog.FindViewById<FrameLayout>(sheetId) : null;
            if (bottomSheet != null)
            {
                var radius = (float)(28 * density);

                var shape = new ShapeAppearanceModel.Builder()
                    .SetTopLeftCorner(CornerFamily.Rounded, radius)
                    .SetTopRightCorner(CornerFamily.Rounded, radius)
                    .Build();

                var shapeDrawable = new MaterialShapeDrawable(shape);
                shapeDrawable.FillColor = ColorStateList.ValueOf(ResolveSurfaceColor(activity));

                bottomSheet.SetBackground(shapeDrawable);

                bottomSheet.ClipToOutline = true;
                bottomSheet.SetClipChildren(true);
                bottomSheet.SetClipToPadding(true);
            }
        });

        // ── Programmatic dismiss hook ─────────────────────────────────────────────
        ActiveBottomSheetDismiss = () =>
        {
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (!await CanDismissAsync()) return;
                dialog?.Dismiss();
            });
        };
        ActiveBottomSheetChanged?.Invoke();

        var result = await tcs.Task;

        ActiveBottomSheetDismiss = null;
        ActiveBottomSheetChanged?.Invoke();
        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a vertical <see cref="LinearLayout"/> with a drag-handle pill at the
    /// top followed by the MAUI content view filling all remaining space.
    /// </summary>
    private static LinearLayout BuildSheetWrapper(
        Android.Content.Context context,
        AView nativeContent,
        double density)
    {
        var handleHeightPx = (int)(4  * density);
        var handleWidthPx  = (int)(32 * density);
        var handleCornerPx = (int)(2  * density);
        var handleVPadPx   = (int)(8  * density);

        var handleBaseColor = ResolveMaterialColor(
            context,
            "colorOnSurfaceVariant",
            Android.Graphics.Color.Gray);
        var handleAlpha     = (int)(0.4f * 255);
        var handleColorInt  = (handleAlpha << 24) | ((int)handleBaseColor & 0x00FFFFFF);

        var handleDrawable = new GradientDrawable();
        handleDrawable.SetShape(ShapeType.Rectangle);
        handleDrawable.SetColor(new Android.Graphics.Color(handleColorInt));
        handleDrawable.SetCornerRadius(handleCornerPx);

        var handlePill = new AView(context) { Background = handleDrawable };

        var handleContainer = new FrameLayout(context);
        handleContainer.SetPadding(0, handleVPadPx, 0, 0 /*handleVPadPx*/);
        handleContainer.AddView(handlePill,
            new FrameLayout.LayoutParams(handleWidthPx, handleHeightPx, GravityFlags.Center));

        var wrapper = new LinearLayout(context);
        wrapper.Orientation = Android.Widget.Orientation.Vertical;

        wrapper.AddView(handleContainer,
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent));

        // height=0 + weight=1 expands the content to fill all space below the handle.
        wrapper.AddView(nativeContent,
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f));

        // Respect the system navigation bar for edge-to-edge layouts: apply bottom
        // padding equal to the nav-bar height, unless the keyboard is visible (in
        // which case AdjustResize already handled the offset).
        ViewCompat.SetOnApplyWindowInsetsListener(wrapper, new BottomPaddingInsetsListener());

        return wrapper;
    }

    private static Android.Graphics.Color ResolveSurfaceColor(Android.Content.Context context)
        => ResolveMaterialColor(
            context,
            "colorSurface",
            Android.Graphics.Color.White);

    /// <summary>
    /// Resolves a Material / AppCompat theme color attribute by name, falling back to
    /// <paramref name="fallback"/> if the attribute is not present or resolution fails.
    /// Attribute IDs are looked up at runtime so no generated Resource class is needed.
    /// </summary>
    private static Android.Graphics.Color ResolveMaterialColor(
        Android.Content.Context context,
        string attrName,
        Android.Graphics.Color fallback)
    {
        try
        {
            // Merged app resources contain all library attrs (including Material).
            var attrId = context.Resources?.GetIdentifier(attrName, "attr", context.PackageName) ?? 0;
            if (attrId == 0) return fallback;

            var tv = new Android.Util.TypedValue();
            if (context.Theme?.ResolveAttribute(attrId, tv, true) == true)
                return new Android.Graphics.Color(tv.Data);
        }
        catch { }
        return fallback;
    }

    private static int GetClosestState(double targetPx, int smallPx, int midPx, int largePx)
    {
        (double dist, int state)[] candidates =
        [
            (Math.Abs(targetPx - smallPx), BottomSheetBehavior.StateCollapsed),
            (Math.Abs(targetPx - midPx),   BottomSheetBehavior.StateHalfExpanded),
            (Math.Abs(targetPx - largePx), BottomSheetBehavior.StateExpanded),
        ];
        return candidates.MinBy(c => c.dist).state;
    }

    // ── SpineBottomSheetDialog ────────────────────────────────────────────────────

    /// <summary>
    /// Extends <see cref="BottomSheetDialog"/> to intercept <c>Cancel()</c> and run the
    /// dismiss guard before allowing the sheet to close.
    /// </summary>
    private sealed class SpineBottomSheetDialog : BottomSheetDialog
    {
        private readonly Func<Task<bool>> _canDismiss;
        private readonly Func<Task<bool>> _handleBack;
        private BottomSheetBehavior? _behavior;
        private Func<int>? _getLastSettledState;
        // Set to true once the dismiss guard has passed so that re-entry from the
        // STATE_HIDDEN → cancel() callback path doesn't re-run the guard.
        private bool _dismissGranted;

        public SpineBottomSheetDialog(
            Android.Content.Context context,
            Func<Task<bool>> canDismiss,
            Func<Task<bool>> handleBack,
            TaskCompletionSource<bool> tcs)
            : base(context)
        {
            _canDismiss = canDismiss;
            _handleBack = handleBack;
            // Resolve the TCS when the dialog window is actually removed (any path).
            DismissEvent += (_, _) => tcs.TrySetResult(false);
        }

        public void SetBehaviorAndStateProvider(
            BottomSheetBehavior behavior,
            Func<int> getLastSettledState)
        {
            _behavior            = behavior;
            _getLastSettledState = getLastSettledState;
        }

        // ── Physical / system back key ────────────────────────────────────────────
        // Overriding OnBackPressed intercepts the key BEFORE BottomSheetDialog's
        // own handler sets behavior.State = STATE_HIDDEN, which would slide the
        // sheet away and leave the dim overlay stranded (issue #2).
        public override void OnBackPressed()
        {
            _ = HandleBackKeyAsync();
        }

        private async Task HandleBackKeyAsync()
        {
            // In-sheet navigation takes priority.
            if (await _handleBack())
                return;

            // Nothing left to navigate back to — fall through to dismiss.
            await ExecuteDismissAsync(animateToHidden: true);
        }

        // ── Swipe-to-dismiss / outside-tap ────────────────────────────────────────
        // Cancel() is reached when the behavior has already moved to STATE_HIDDEN
        // (swipe) or the user tapped the backdrop.  No in-sheet navigation here.
        public override void Cancel()
        {
            _ = ExecuteDismissAsync(animateToHidden: false);
        }

        // ── Shared dismiss path ───────────────────────────────────────────────────
        private async Task ExecuteDismissAsync(bool animateToHidden)
        {
            // Skip the guard if it already passed (re-entry from STATE_HIDDEN callback).
            if (!_dismissGranted && !await _canDismiss())
            {
                // Guard rejected — snap the sheet back to the last stable detent.
                if (_behavior != null && _getLastSettledState != null)
                {
                    var restoreState = _getLastSettledState();
                    MainThread.BeginInvokeOnMainThread(() => _behavior.State = restoreState);
                }
                return;
            }

            _dismissGranted = true;

            if (animateToHidden
                && _behavior != null
                && _behavior.Hideable
                && _behavior.State != BottomSheetBehavior.StateHidden)
            {
                // Slide the sheet down; the behavior's internal STATE_HIDDEN callback
                // will call cancel() / dismiss() to complete the teardown.
                MainThread.BeginInvokeOnMainThread(
                    () => _behavior.State = BottomSheetBehavior.StateHidden);
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() => base.Cancel());
            }
        }
    }

    // ── SheetStateCallback ────────────────────────────────────────────────────────

    private sealed class SheetStateCallback : BottomSheetBehavior.BottomSheetCallback
    {
        private readonly Action<AView, int>?   _onStateChanged;
        private readonly Action<AView, float>? _onSlide;

        public SheetStateCallback(
            Action<AView, int>?   onStateChanged = null,
            Action<AView, float>? onSlide        = null)
        {
            _onStateChanged = onStateChanged;
            _onSlide        = onSlide;
        }

        public override void OnSlide(AView bottomSheet, float slideOffset)
            => _onSlide?.Invoke(bottomSheet, slideOffset);

        public override void OnStateChanged(AView bottomSheet, int newState)
            => _onStateChanged?.Invoke(bottomSheet, newState);
    }

    // ── BottomPaddingInsetsListener ───────────────────────────────────────────────

    /// <summary>
    /// Applies bottom padding equal to the system navigation-bar height so that
    /// sheet content is never hidden behind gesture handles or the nav bar.
    /// Padding is suppressed when the IME is visible because <c>AdjustResize</c>
    /// already repositions the content.
    /// </summary>
    private sealed class BottomPaddingInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(AView? v, WindowInsetsCompat? insets)
        {
            if (v is null || insets is null)
                return insets;

            var sysBarInsets  = insets.GetInsets(WindowInsetsCompat.Type.SystemBars()) ?? AndroidX.Core.Graphics.Insets.None;
            var imeInsets     = insets.GetInsets(WindowInsetsCompat.Type.Ime()) ?? AndroidX.Core.Graphics.Insets.None;
            var bottomPadding = imeInsets!.Bottom > 0 ? 0 : sysBarInsets!.Bottom;
            v.SetPadding(v.PaddingLeft, v.PaddingTop, v.PaddingRight, bottomPadding);
            return insets;
        }
    }
}
