namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static partial void ConfigureHandlers(MauiAppBuilder builder)
    {
        ConfigureScrollInsets();
        ConfigureMaterials();
        ConfigureTypography();
        ConfigureMenus();

        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<Presentation.Lightbox, Presentation.LightboxHandler>();
            handlers.AddHandler<Presentation.SegmentedControl, Presentation.SegmentedControlHandler>();
        });

        // WinUI's ToggleSwitch is at least 154 wide, room for "On"/"Off" text that a MAUI Switch never shows, so a
        // switch at the end of a row stood well short of the edge. Without the minimum, and without the template's
        // fixed 12-point column before that text, it is as wide as its track.
        Microsoft.Maui.Handlers.SwitchHandler.Mapper.AppendToMapping("SpineCompactSwitch", static (handler, _) =>
        {
            if (handler.PlatformView is not Microsoft.UI.Xaml.Controls.ToggleSwitch toggle)
                return;

            toggle.MinWidth = 0;
            toggle.OnContent = toggle.OffContent = null;
            toggle.Loaded -= CollapseSwitchContent;
            toggle.Loaded += CollapseSwitchContent;
        });
    }

    static void CollapseSwitchContent(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // The template's content row: [track][12][On/Off text]. Without text the 12 is only a gap after the track.
        static Microsoft.UI.Xaml.Controls.Grid? Find(Microsoft.UI.Xaml.DependencyObject parent)
        {
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Microsoft.UI.Xaml.Controls.Grid { ColumnDefinitions.Count: 3 } grid && grid.ColumnDefinitions[1].MaxWidth == 12)
                    return grid;
                if (Find(child) is { } found)
                    return found;
            }

            return null;
        }

        if (sender is Microsoft.UI.Xaml.Controls.ToggleSwitch toggle && Find(toggle) is { } row)
        {
            row.ColumnDefinitions[1].Width = new Microsoft.UI.Xaml.GridLength(0);
            row.ColumnDefinitions[1].MaxWidth = 0;
        }
    }
}
