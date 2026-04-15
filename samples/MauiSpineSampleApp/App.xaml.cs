namespace MauiBottomSheetPoc;

public partial class App
{
    public App()
    {
        InitializeComponent();

#if ANDROID
        // On Android, AppThemeBinding in implicit styles does not re-apply to native view
        // properties when UserAppTheme is changed programmatically. Subscribe to
        // RequestedThemeChanged and push the new background colour directly instead.
        RequestedThemeChanged += (_, e) =>
        {
            if (Windows.FirstOrDefault()?.Page is ContentPage page)
                page.BackgroundColor = e.RequestedTheme == AppTheme.Dark
                    ? (Color)Resources["OffBlack"]
                    : (Color)Resources["White"];
        };
#endif
    }
}
