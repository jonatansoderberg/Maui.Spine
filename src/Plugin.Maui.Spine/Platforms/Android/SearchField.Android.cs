namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class SearchField
{
    // SearchView underlines its query; inside the capsule the capsule is the field's edge.
    partial void ConfigurePlatform()
    {
        if (_bar.Handler?.PlatformView is not Android.Views.View view || view.Context is not { Resources: { } resources } context)
            return;

        foreach (var package in (string?[])["android", context.PackageName])
        {
            var id = resources.GetIdentifier("search_plate", "id", package);
            if (id != 0 && view.FindViewById(id) is { } plate)
                plate.Background = null;
        }
    }
}
