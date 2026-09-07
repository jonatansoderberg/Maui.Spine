using Android.App;
using Android.Content;
using Android.OS;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Receives <c>&lt;ApplicationId&gt;://widget/…</c> — from a widget tap, a Live Update, or anything else — and
/// hands the URL to the app's launcher activity, which MAUI's lifecycle then surfaces as <c>OnCreate</c> or
/// <c>OnNewIntent</c>. Owned by the package so the build can register the scheme without knowing the app's
/// own activity by its generated Java name.
/// </summary>
// Its own task (empty affinity): inside the app's task it would sit above the main activity, and a
// single-top launch would then create a second main activity instead of delivering the intent.
[Activity(Name = "plugin.maui.spine.widgets.SpineWidgetLinkActivity", Exported = true, NoHistory = true, ExcludeFromRecents = true, TaskAffinity = "", Theme = "@android:style/Theme.NoDisplay")]
internal sealed class SpineWidgetLinkActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Intent?.Data is { } url && PackageManager?.GetLaunchIntentForPackage(PackageName!)?.Component is { } main)
            StartActivity(new Intent(Android.Content.Intent.ActionView, url)
                .SetComponent(main)
                .AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop));

        Finish();
    }
}
