#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

internal static partial class ActionSheetPresenter
{
    private static partial Task<MenuAction?> ShowPlatformAsync(IServiceProvider services, ActionSheet sheet, IReadOnlyList<MenuAction> actions, View? anchor)
    {
        if (Platform.GetCurrentUIViewController() is not { } presenter)
            throw new InvalidOperationException("ShowActionsAsync found no view controller to present the action sheet from.");

        var done = new TaskCompletionSource<MenuAction?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var alert = UIAlertController.Create(
            string.IsNullOrEmpty(sheet.Title) ? null : sheet.Title,
            string.IsNullOrEmpty(sheet.Message) ? null : sheet.Message,
            UIAlertControllerStyle.ActionSheet);

        // The app's own Light or Dark, which the MAUI window does not pass on to what it presents.
        alert.OverrideUserInterfaceStyle = Application.Current?.UserAppTheme switch
        {
            AppTheme.Dark => UIUserInterfaceStyle.Dark,
            AppTheme.Light => UIUserInterfaceStyle.Light,
            _ => UIUserInterfaceStyle.Unspecified,
        };

        foreach (var action in actions)
        {
            var style = action.IsDestructive ? UIAlertActionStyle.Destructive : UIAlertActionStyle.Default;
            var row = UIAlertAction.Create(action.Title, style, _ => done.TrySetResult(action));
            row.Enabled = action.IsEnabled;
            SetImage(row, Image(services, action.Svg));
            alert.AddAction(row);
        }

        // A sheet grown out of a view, and iPad's popover, leave this row out; a tap outside cancels.
        alert.AddAction(UIAlertAction.Create(CancelText(sheet), UIAlertActionStyle.Cancel, _ => done.TrySetResult(null)));

        if (alert.PopoverPresentationController is { } popover)
        {
            // With a source, iOS 26 grows the sheet out of it on the iPhone too; without one the
            // iPhone shows its own sheet, and iPad centres the popover. The Mac has no popover here.
            if (anchor?.Handler?.PlatformView is UIView source && source.Window is not null)
            {
                popover.SourceView = source;
                popover.SourceRect = source.Bounds;
            }
            else if (UIDevice.CurrentDevice.UserInterfaceIdiom != UIUserInterfaceIdiom.Phone && presenter.View is { } view)
            {
                popover.SourceView = view;
                popover.SourceRect = new CGRect(view.Bounds.GetMidX(), view.Bounds.GetMidY(), 0, 0);
                popover.PermittedArrowDirections = 0;
            }
        }

        // Gone without a pick: a tap outside the popover, or dismissed with whatever presented it.
        alert.View?.AddSubview(new GoneWatcher(done) { Hidden = true });

        presenter.PresentViewController(alert, true, null);
        return done.Task;
    }

    /// <summary>
    /// An icon beside the title. <c>UIAlertAction</c> has no public image property; its <c>image</c>
    /// key is what the system's own sheets use, and a missing key leaves the row without an icon.
    /// </summary>
    static void SetImage(UIAlertAction row, UIImage? image)
    {
        if (image is null)
            return;

        try
        {
            row.SetValueForKey(image, new NSString("image"));
        }
        catch (ObjCException e)
        {
            Console.WriteLine($"[Spine] ShowActionsAsync: the action sheet row \"{row.Title}\" has no icon, UIAlertAction refused its image: {e.Message}");
        }
    }

    static UIImage? Image(IServiceProvider services, string? svg)
    {
        if (MenuButton.Icon(services, svg, 22, Colors.Black) is not { } png)
            return null;

        using var data = NSData.FromArray(png);
        return UIImage.LoadFromData(data, UIScreen.MainScreen.Scale)?.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);
    }

    /// <summary>
    /// Completes with <see langword="null"/> once the sheet leaves the window without a pick. A row's
    /// handler runs as the sheet's view leaves, so the check waits a turn of the run loop.
    /// </summary>
    sealed class GoneWatcher(TaskCompletionSource<MenuAction?> done) : UIView
    {
        bool _shown;

        public override void MovedToWindow()
        {
            base.MovedToWindow();

            if (Window is not null)
                _shown = true;
            else if (_shown)
                NSRunLoop.Main.BeginInvokeOnMainThread(() => done.TrySetResult(null));
        }
    }
}
#endif
