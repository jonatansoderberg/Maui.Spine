using Android.App;
using Android.Content;
using Android.Content.PM;

namespace MauiSpineSampleApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([ Platform.Intent.ActionAppAction ], Categories = [ Intent.CategoryDefault ])]
public class MainActivity : MauiAppCompatActivity
{
    // MAUI's Material 3 theme brings the baseline purple and ignores colors.xml; the overlay puts
    // the default accent on native parts MAUI does not colour itself (the radio button circle).
    protected override void OnApplyThemeResource(Android.Content.Res.Resources.Theme? theme, int resid, bool first)
    {
        base.OnApplyThemeResource(theme, resid, first);
        theme?.ApplyStyle(MauiBottomSheetPoc.Resource.Style.AccentOverlay, force: true);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Platform.OnResume(this);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Platform.OnNewIntent(intent);
    }
}
