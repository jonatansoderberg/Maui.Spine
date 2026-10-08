namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class SearchField
{
    // SearchView underlines its query; inside the capsule the capsule is the field's edge.
    partial void ConfigurePlatform()
    {
        if (FindInSearchView("search_plate") is { } plate)
            plate.Background = null;

        ApplyCancelButton();
    }

    // The back arrow takes the magnifier's place while a search goes on, as in Material's search view.
    partial void ApplyCancelButton()
    {
        if (_back is null)
            return;

        var shows = ShowsCancelButton;
        _back.IsVisible = shows;

        if (FindInSearchView("search_mag_icon") is { } magnifier)
            magnifier.Visibility = shows ? Android.Views.ViewStates.Gone : Android.Views.ViewStates.Visible;
    }

    private Android.Views.View? FindInSearchView(string name)
    {
        if (_bar.Handler?.PlatformView is not Android.Views.View view || view.Context is not { Resources: { } resources } context)
            return null;

        foreach (var package in (string?[])["android", context.PackageName])
        {
            var id = resources.GetIdentifier(name, "id", package);
            if (id != 0 && view.FindViewById(id) is { } found)
                return found;
        }

        return null;
    }
}
