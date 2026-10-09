#if WINDOWS
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using FrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using MenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using MenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using MenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;
using FlyoutShowOptions = Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;

namespace Plugin.Maui.Spine.Presentation;

/// <remarks>A <c>MenuFlyout</c> at the anchor, or in the middle of the window; a click outside it cancels.</remarks>
internal static partial class ActionSheetPresenter
{
    private static partial Task<MenuAction?> ShowPlatformAsync(IServiceProvider services, ActionSheet sheet, IReadOnlyList<MenuAction> actions, View? anchor)
    {
        var source = anchor?.Handler?.PlatformView as FrameworkElement;
        var target = source
            ?? (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.Content as FrameworkElement
            ?? throw new InvalidOperationException("ShowActionsAsync found no window to show the action sheet in.");

        var done = new TaskCompletionSource<MenuAction?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var flyout = new MenuFlyout();

        // A flyout has no heading; the title and message are dimmed rows above a separator.
        foreach (var line in new[] { sheet.Title, sheet.Message }.Where(static l => !string.IsNullOrEmpty(l)))
            flyout.Items.Add(new MenuFlyoutItem { Text = line, IsEnabled = false });
        if (flyout.Items.Count > 0)
            flyout.Items.Add(new MenuFlyoutSeparator());

        foreach (var action in actions)
        {
            var item = new MenuFlyoutItem
            {
                Text = action.Title,
                IsEnabled = action.IsEnabled,
                Icon = SpineExtensions.BuildIcon(services, action.Svg, target.XamlRoot),
            };

            if (action.IsDestructive)
                item.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Firebrick);

            item.Click += (_, _) => done.TrySetResult(action);
            flyout.Items.Add(item);
        }

        // A click closes the flyout before the item's Click is raised; wait for the queue to settle.
        flyout.Closed += (_, _) => target.DispatcherQueue.TryEnqueue(() => done.TrySetResult(null));

        if (source is not null)
            flyout.ShowAt(source);
        else
            flyout.ShowAt(target, new FlyoutShowOptions { Position = new(target.ActualWidth / 2, target.ActualHeight / 2) });

        return done.Task;
    }
}
#endif
