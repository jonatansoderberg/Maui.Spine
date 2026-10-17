#if IOS || MACCATALYST

using AsyncAwaitBestPractices;
using CoreAnimation;
using Foundation;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Presentation;
using Plugin.Maui.Spine.Sheets;
using UIKit;
using MauiPage = Microsoft.Maui.Controls.Page;

namespace Plugin.Maui.Spine;

internal static class BottomSheetPageExtensions
{
    /// <summary>Asks the open sheet to close: <see langword="true"/> once it goes, <see langword="false"/> when its guard refuses.</summary>
    internal static Func<Task<bool>>? ActiveBottomSheetDismiss { get; private set; }

    internal static event Action? ActiveBottomSheetChanged;

    internal static Task<bool> DismissActiveBottomSheet() => ActiveBottomSheetDismiss?.Invoke() ?? Task.FromResult(false);

    internal static async Task<bool> DisplayBottomSheet(
        this MauiPage page,
        Func<IView> bottomSheetFactory,
        Action<BottomSheetBuilder>? builder = null)
    {
        var bottomSheetContent = bottomSheetFactory();
        var tcs = new TaskCompletionSource<bool>();

        var mauiContext = page.Handler?.MauiContext
            ?? throw new InvalidOperationException("MauiContext is null");

        var bottomSheetBuilder = new BottomSheetBuilder();
        builder?.Invoke(bottomSheetBuilder);

        var nativeContent = bottomSheetContent.ToPlatform(mauiContext);
        nativeContent.RemoveFromSuperview();

        var presenterVc = GetTopmostViewController()
            ?? throw new InvalidOperationException("No UIViewController available to present from");

        // ── Dismiss guard ────────────────────────────────────────────────────────
        async Task<bool> CanDismissAsync()
        {
            if (bottomSheetContent is NavigationRegion region
                && region.BindingContext is NavigationRegionViewModel regionVm
                && regionVm.CurrentRegionViewModel is ViewModelBase currentVm)
            {
                return await AskAsync(currentVm);
            }

            if (bottomSheetContent is BindableObject bo && bo.BindingContext is ViewModelBase vm)
                return await AskAsync(vm);

            return true;
        }

        // A guard that answers at once gets the refusal haptic; one that awaits (a "discard changes?"
        // prompt) has already given the user its answer.
        static async Task<bool> AskAsync(ViewModelBase vm)
        {
            var request = vm.OnCloseRequestedAsync();
            var immediate = request.IsCompleted;
            var allowed = await request;

            if (!allowed && immediate)
                Haptics.Play(Haptics.Options.Haptics.DismissBlocked);

            return allowed;
        }

        // ── Back guard ───────────────────────────────────────────────────────────
        // Returns true if the gesture was consumed by in-sheet navigation.
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

        // ── Sheet view controller ────────────────────────────────────────────────
        var sheetVc = new SpineSheetViewController(nativeContent, tcs);
        sheetVc.ModalPresentationStyle = UIModalPresentationStyle.PageSheet;
        // Block interactive swipe-to-dismiss; every attempt goes through the delegate.
        sheetVc.ModalInPresentation = true;

        // UISheetPresentationController's card surface does not reliably inherit the
        // trait collection from the MAUI window. Explicitly mirror the MAUI app theme
        // so the native sheet background renders dark when the app is in dark mode.
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark
            || (Application.Current?.RequestedTheme != AppTheme.Light
                && Application.Current?.PlatformAppTheme == AppTheme.Dark);
        sheetVc.OverrideUserInterfaceStyle = isDark
            ? UIUserInterfaceStyle.Dark
            : UIUserInterfaceStyle.Light;

        // ── Detents ──────────────────────────────────────────────────────────────
        SheetBlurOverlay? blurOverlay = null;
        var allowedDetents = bottomSheetBuilder.AllowedDetents.Count > 0
            ? bottomSheetBuilder.AllowedDetents
            : new List<SheetDetent> { SheetDetent.MediumDetent };

        var selectedDetent = bottomSheetBuilder.SelectedDetent ?? allowedDetents[0];

        if (sheetVc.SheetPresentationController is { } spc)
        {
            spc.PrefersGrabberVisible = true;

            // iOS 26 rounds a sheet concentrically with the screen; a fixed radius made it look
            // tighter than the system's own sheets.
            if (!OperatingSystem.IsIOSVersionAtLeast(26) && !OperatingSystem.IsMacCatalystVersionAtLeast(26))
                spc.PreferredCornerRadius = 20;

            ConfigureDetents(spc, allowedDetents, selectedDetent, bottomSheetBuilder.BackgroundPageOverlay);

            if (bottomSheetBuilder.BackgroundPageOverlay == BackgroundPageOverlay.Blurred)
                blurOverlay = new SheetBlurOverlay(presenterVc.View!, spc, () => _ = DismissActiveBottomSheet());

            spc.Delegate = new SpineSheetDelegate(CanDismissAsync, HandleBackAsync, sheetVc);
        }

        // ── Programmatic dismiss hook ─────────────────────────────────────────────
        ActiveBottomSheetDismiss = () => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!await CanDismissAsync()) return false;
            sheetVc.ModalInPresentation = false;
            presenterVc.DismissViewController(true, null);
            return true;
        });
        ActiveBottomSheetChanged?.Invoke();

        await MainThread.InvokeOnMainThreadAsync(() =>
            presenterVc.PresentViewController(sheetVc, true, null));

        await tcs.Task;

        if (blurOverlay is not null)
            await MainThread.InvokeOnMainThreadAsync(blurOverlay.Dispose);

        ActiveBottomSheetDismiss = null;
        ActiveBottomSheetChanged?.Invoke();

        return false;
    }

    // ── Detent configuration ──────────────────────────────────────────────────────

    private static void ConfigureDetents(
        UISheetPresentationController spc,
        List<SheetDetent> detents,
        SheetDetent selectedDetent,
        BackgroundPageOverlay overlay)
    {
        if (OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16, 1))
            ConfigureCustomDetents(spc, detents, selectedDetent, overlay);
        else
            ConfigureNativeDetents(spc, detents, selectedDetent, overlay);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("ios16.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("maccatalyst16.1")]
    private static void ConfigureCustomDetents(
        UISheetPresentationController spc,
        List<SheetDetent> detents,
        SheetDetent selectedDetent,
        BackgroundPageOverlay overlay)
    {
        var sortedDetents = detents.OrderBy(NormalisedValue).ToList();

        var nativeDetents = sortedDetents
            .Select((d, i) => UISheetPresentationControllerDetent.Create(
                $"spine_{i}",
                ctx =>
                {
                    if (d.AbsoluteHeight.HasValue) return (nfloat)d.AbsoluteHeight.Value;
                    if (d.Percentage.HasValue)     return (nfloat)(ctx.MaximumDetentValue * d.Percentage.Value);
                    return ctx.MaximumDetentValue * 0.5f;
                }))
            .ToArray();

        spc.Detents = nativeDetents;

        // The .NET iOS binding exposes SelectedDetentIdentifier as an enum with only
        // predefined Medium/Large values; custom identifiers ("spine_N") must be set
        // via KVC so the raw NSString reaches the ObjC property setter.
        var selectedNorm  = NormalisedValue(selectedDetent);
        var closestIdx    = Enumerable.Range(0, sortedDetents.Count)
            .MinBy(i => Math.Abs(NormalisedValue(sortedDetents[i]) - selectedNorm));
        spc.SetValueForKey(new NSString(nativeDetents[closestIdx].Identifier!), new NSString("selectedDetentIdentifier"));

        if (overlay != BackgroundPageOverlay.Dimmed)
            spc.SetValueForKey(new NSString(nativeDetents[^1].Identifier!), new NSString("largestUndimmedDetentIdentifier"));
    }

    private static void ConfigureNativeDetents(
        UISheetPresentationController spc,
        List<SheetDetent> detents,
        SheetDetent selectedDetent,
        BackgroundPageOverlay overlay)
    {
        // iOS 15 supports only .medium (~50%) and .large (100%).
        // Map each requested detent to the closest native tier.
        bool needsMedium = detents.Any(d => NormalisedValue(d) <= 0.6);
        bool needsLarge  = detents.Any(d => NormalisedValue(d) > 0.6) || !needsMedium;

        var list = new List<UISheetPresentationControllerDetent>();
        if (needsMedium) list.Add(UISheetPresentationControllerDetent.CreateMediumDetent());
        if (needsLarge)  list.Add(UISheetPresentationControllerDetent.CreateLargeDetent());

        spc.Detents = list.ToArray();

        spc.SelectedDetentIdentifier = NormalisedValue(selectedDetent) <= 0.6
            ? UISheetPresentationControllerDetentIdentifier.Medium
            : UISheetPresentationControllerDetentIdentifier.Large;

        if (overlay != BackgroundPageOverlay.Dimmed)
            spc.LargestUndimmedDetentIdentifier = UISheetPresentationControllerDetentIdentifier.Large;
    }

    private static double NormalisedValue(SheetDetent d) =>
        d.Percentage ?? (d.AbsoluteHeight.HasValue ? d.AbsoluteHeight.Value / 1_000.0 : 0.5);

    // ── Blur overlay ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="MaterialPreset.BlurThin"/> material over the page behind a sheet. It shows as much
    /// of itself as the sheet shows of its own height, so it fades in as the sheet slides up, out as it
    /// slides away, and follows the finger when the sheet is dragged, at the same strength at every detent.
    /// It takes the touches meant for the page, as the dimming does: the sheet is left undimmed so the blur
    /// can stand in for the dim, and UIKit lets an undimmed sheet's page be used. A tap closes the sheet.
    /// </summary>
    private sealed class SheetBlurOverlay : IDisposable
    {
        // The surface reads its values from a MAUI view, as every material does; this one is never shown.
        private readonly Microsoft.Maui.Controls.ContentView _owner = new();
        private readonly MaterialSurfaceView _surface;
        private readonly UIView _catcher;
        private readonly UISheetPresentationController _sheet;
        private CADisplayLink? _link;

        public SheetBlurOverlay(UIView page, UISheetPresentationController sheet, Action tapped)
        {
            _sheet = sheet;
            Material.SetPreset(_owner, MaterialPreset.BlurThin);

            // The surface turns its own interaction off whenever it updates, so a plain view takes the touches
            _catcher = new UIView(page.Bounds)
            {
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
                BackgroundColor = UIColor.Clear,
            };
            _catcher.AddGestureRecognizer(new UITapGestureRecognizer(tapped));

            _surface = new MaterialSurfaceView(_owner)
            {
                Frame = _catcher.Bounds,
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
                UserInteractionEnabled = false,
                Presence = 0,
            };
            _catcher.AddSubview(_surface);
            page.AddSubview(_catcher);
            _surface.Update();

            _link = CADisplayLink.Create(Follow);
            _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
        }

        private void Follow()
        {
            if (_sheet.ContainerView is not { } container || _sheet.PresentedView?.Layer.PresentationLayer is not { } layer)
                return;

            // The layer in flight, not the model layer, which already holds where the animation ends.
            var frame = container.Layer.ConvertRectFromLayer(layer.Frame, layer.SuperLayer);
            if (frame.Height <= 0)
                return;

            _surface.Presence = (container.Bounds.Height - frame.Y) / frame.Height;
        }

        public void Dispose()
        {
            _link?.Invalidate();
            _link = null;
            _catcher.RemoveFromSuperview();
            _surface.Dispose();
            _catcher.Dispose();
        }
    }

    // ── Presenter resolution ──────────────────────────────────────────────────────

    private static UIViewController? GetTopmostViewController()
    {
        UIWindow? keyWindow = null;

        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is UIWindowScene ws)
            {
                keyWindow = ws.Windows.FirstOrDefault(w => w.IsKeyWindow)
                         ?? ws.Windows.FirstOrDefault();
                if (keyWindow != null) break;
            }
        }

        var vc = keyWindow?.RootViewController;
        while (vc?.PresentedViewController != null)
            vc = vc.PresentedViewController;

        return vc;
    }

    // ── SpineSheetViewController ─────────────────────────────────────────────────

    /// <summary>
    /// Wraps a MAUI native view in a <see cref="UIViewController"/> suitable for
    /// presentation via <see cref="UISheetPresentationController"/>.
    /// Resolves the <see cref="TaskCompletionSource{T}"/> when the sheet disappears.
    /// </summary>
    private sealed class SpineSheetViewController : UIViewController
    {
        private readonly UIView _nativeContent;
        private readonly TaskCompletionSource<bool> _tcs;

        public SpineSheetViewController(UIView nativeContent, TaskCompletionSource<bool> tcs)
        {
            _nativeContent = nativeContent;
            _tcs = tcs;
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            // A Mac sheet is an opaque card, and Liquid Glass draws from what lies behind it: over a clear view a
            // glass header button (the close button) had nothing to sample and drew flat grey in the light theme
            // (#555). The system background is what a native sheet's view has. An iPhone sheet at a partial detent
            // is glass itself and stays clear.
            View!.BackgroundColor = OperatingSystem.IsMacCatalyst() ? UIColor.SystemBackground : UIColor.Clear;

            _nativeContent.TranslatesAutoresizingMaskIntoConstraints = false;
            View!.AddSubview(_nativeContent);

            NSLayoutConstraint.ActivateConstraints([
                _nativeContent.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
                _nativeContent.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
                _nativeContent.TopAnchor.ConstraintEqualTo(View.TopAnchor),
                _nativeContent.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
            ]);
        }

        public override void ViewWillAppear(bool animated)
        {
            base.ViewWillAppear(animated);

            // The UISheetPresentationController card chrome lives in the presentation
            // container view, not in this VC's view subtree. OverrideUserInterfaceStyle
            // on the VC alone doesn't reach it — we must set it on the container too.
            if (PresentationController?.ContainerView is { } container)
                container.OverrideUserInterfaceStyle = OverrideUserInterfaceStyle;

            // The container is in the window now, above an overlay (an app lock, say) that must stay on top.
            SpineOverlay.Raise();
        }

        public override void ViewDidDisappear(bool animated)
        {
            base.ViewDidDisappear(animated);
            _tcs.TrySetResult(false);
        }
    }

    // ── SpineSheetDelegate ───────────────────────────────────────────────────────

    /// <summary>
    /// Intercepts swipe-to-dismiss attempts (which <c>ModalInPresentation = true</c>
    /// routes to <see cref="DidAttemptToDismiss"/>), checks in-sheet back navigation
    /// first, then runs the dismiss guard before allowing the sheet to close.
    /// </summary>
    private sealed class SpineSheetDelegate : NSObject, IUISheetPresentationControllerDelegate
    {
        private readonly Func<Task<bool>> _canDismiss;
        private readonly Func<Task<bool>> _handleBack;
        private readonly UIViewController _sheetVc;

        public SpineSheetDelegate(
            Func<Task<bool>> canDismiss,
            Func<Task<bool>> handleBack,
            UIViewController sheetVc)
        {
            _canDismiss = canDismiss;
            _handleBack = handleBack;
            _sheetVc = sheetVc;
        }

        // Fires when ModalInPresentation = true blocks a swipe-to-dismiss gesture.
        [Export("presentationControllerDidAttemptToDismiss:")]
        public void DidAttemptToDismiss(UIPresentationController presentationController)
            => HandleDismissAttemptAsync().SafeFireAndForget();

        // UIKit calls this only when the user drags the sheet to another detent.
        [Export("sheetPresentationControllerDidChangeSelectedDetentIdentifier:")]
        public void DidChangeSelectedDetentIdentifier(UISheetPresentationController sheetPresentationController)
            => Haptics.Play(Haptics.Options.Haptics.SheetDetent);

        private async Task HandleDismissAttemptAsync()
        {
            // In-sheet back navigation takes priority over sheet dismissal.
            if (await _handleBack())
                return;

            if (!await _canDismiss())
                return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Unblock the standard dismiss path then trigger it programmatically.
                _sheetVc.ModalInPresentation = false;
                _sheetVc.PresentingViewController?.DismissViewController(true, null);
            });
        }
    }
}

#endif
