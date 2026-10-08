using Android.App;
using Android.Content;
using Android.OS;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// Opened by a search shortcut, and hands its item's id to the app's launcher activity, which MAUI's
/// lifecycle surfaces as <c>OnCreate</c> or <c>OnNewIntent</c>. In the package, so Spine does not need
/// the app's own activity by its generated Java name.
/// </summary>
// Its own task (empty affinity): the launcher starts a shortcut with CLEAR_TASK, which would otherwise
// clear the app's task and recreate its activity instead of delivering the intent to it.
[Activity(Name = JavaName, Exported = false, NoHistory = true, ExcludeFromRecents = true, TaskAffinity = "", Theme = "@android:style/Theme.NoDisplay")]
internal sealed class SpineSearchActivity : Activity
{
    internal const string JavaName = "plugin.maui.spine.SpineSearchActivity";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Intent?.GetStringExtra(SearchIndex.IdExtra) is { } id && PackageManager?.GetLaunchIntentForPackage(PackageName!)?.Component is { } main)
            StartActivity(new Intent(SearchIndex.OpenAction)
                .SetComponent(main)
                .PutExtra(SearchIndex.IdExtra, id)
                .AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop));

        Finish();
    }
}
