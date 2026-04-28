#if IOS || MACCATALYST

using AsyncAwaitBestPractices;
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
                return await currentVm.OnCloseRequestedAsync();
            }

            if (bottomSheetContent is BindableObject bo && bo.BindingContext is ViewModelBase vm)
                return await vm.OnCloseRequestedAsync();

            return true;
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

        // ── Detents ──────────────────────────────────────────────────────────────
        var allowedDetents = bottomSheetBuilder.AllowedDetents.Count > 0
            ? bottomSheetBuilder.AllowedDetents
            : new List<SheetDetent> { SheetDetent.MediumDetent };

        var selectedDetent = bottomSheetBuilder.SelectedDetent ?? allowedDetents[0];

        if (sheetVc.SheetPresentationController is { } spc)
        {
            spc.PrefersGrabberVisible = true;
            spc.PreferredCornerRadius = 20;

            ConfigureDetents(spc, allowedDetents, selectedDetent, bottomSheetBuilder.BackgroundPageOverlay);

            if (bottomSheetBuilder.BackgroundPageOverlay == BackgroundPageOverlay.Blurred)
                AddBlurOverlay(presenterVc);

            spc.Delegate = new SpineSheetDelegate(CanDismissAsync, HandleBackAsync, sheetVc);
        }

        // ── Programmatic dismiss hook ─────────────────────────────────────────────
        ActiveBottomSheetDismiss = () =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (!await CanDismissAsync()) return;
                sheetVc.ModalInPresentation = false;
                presenterVc.DismissViewController(true, null);
            });
        };
        ActiveBottomSheetChanged?.Invoke();

        await MainThread.InvokeOnMainThreadAsync(() =>
            presenterVc.PresentViewController(sheetVc, true, null));

        await tcs.Task;

        if (bottomSheetBuilder.BackgroundPageOverlay == BackgroundPageOverlay.Blurred)
            await MainThread.InvokeOnMainThreadAsync(() => RemoveBlurOverlay(presenterVc));

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

    private const string BlurOverlayTag = "SpineBlurOverlay";

    private static void AddBlurOverlay(UIViewController vc)
    {
        var blurView = new UIVisualEffectView(UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemMaterial))
        {
            Frame = vc.View!.Bounds,
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
            AccessibilityIdentifier = BlurOverlayTag
        };
        vc.View!.AddSubview(blurView);
    }

    private static void RemoveBlurOverlay(UIViewController vc)
    {
        vc.View?.Subviews
            .FirstOrDefault(v => v.AccessibilityIdentifier == BlurOverlayTag)
            ?.RemoveFromSuperview();
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

            _nativeContent.TranslatesAutoresizingMaskIntoConstraints = false;
            View!.AddSubview(_nativeContent);

            NSLayoutConstraint.ActivateConstraints([
                _nativeContent.LeadingAnchor.ConstraintEqualTo(View.LeadingAnchor),
                _nativeContent.TrailingAnchor.ConstraintEqualTo(View.TrailingAnchor),
                _nativeContent.TopAnchor.ConstraintEqualTo(View.TopAnchor),
                _nativeContent.BottomAnchor.ConstraintEqualTo(View.BottomAnchor),
            ]);
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
