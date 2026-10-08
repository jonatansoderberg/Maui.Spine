using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Presentation;

internal sealed partial class SearchField
{
    /// <summary>Material 3's search bar: the leading icon in a 48-point slot from the bar's edge, the text 68 points in.</summary>
    private const double LeadingIconSlot = 48;

    /// <summary>Material 3's search view: the clear button is a 56-point target at the trailing edge.</summary>
    private const double ClearButtonSize = 56;

    // SearchView underlines its query; inside the capsule the capsule is the field's edge. Its own
    // insets come off, so the icons and the text sit where Material 3's search bar and search view
    // put them: the magnifier centred 24 points in from the capsule's edge with the text after it,
    // and at the other end a clear button as wide as the search view's.
    partial void ConfigurePlatform()
    {
        if (_bar.Handler?.PlatformView is Android.Views.View searchView && searchView.Context is { } context)
        {
            int Px(double points) => (int)Math.Round(points * (context.Resources?.DisplayMetrics?.Density ?? 1));

            if (FindInSearchView("search_mag_icon") is { } magnifier)
            {
                ClearInsets(magnifier, searchView, leading: true);
                if (magnifier.LayoutParameters is { } parameters)
                {
                    parameters.Width = Px(LeadingIconSlot);
                    magnifier.LayoutParameters = parameters;
                }
            }

            if (FindInSearchView("search_close_btn") is { } clear)
            {
                // Its 24-point icon at its own size, as the search view's, not stretched to the button.
                if (clear is Android.Widget.ImageView image)
                    image.SetScaleType(Android.Widget.ImageView.ScaleType.Center);

                ClearInsets(clear, searchView, leading: false);
                if (clear.LayoutParameters is { } parameters)
                {
                    parameters.Width = Px(ClearButtonSize);
                    clear.LayoutParameters = parameters;
                }
            }

            if (FindInSearchView("search_src_text") is { } text)
                ClearInsets(text, searchView, leading: true);
        }

        // Last: SearchView puts the underline back as its parts are laid out again.
        if (FindInSearchView("search_plate") is { } plate)
            plate.Background = null;

        PaintIcons(Material.IsDark());

        ApplyCancelButton();
    }

    // Takes the leading (or trailing) margins and paddings off a view and its parents up to the search view.
    private static void ClearInsets(Android.Views.View view, Android.Views.View searchView, bool leading)
    {
        for (var v = view; v is not null; v = v.Parent as Android.Views.View)
        {
            if (v.LayoutParameters is Android.Views.ViewGroup.MarginLayoutParams margins)
            {
                if (leading)
                {
                    margins.LeftMargin = 0;
                    margins.MarginStart = 0;
                }
                else
                {
                    margins.RightMargin = 0;
                    margins.MarginEnd = 0;
                }
                v.LayoutParameters = margins;
            }

            if (!ReferenceEquals(v, view) || v is not Android.Widget.ImageView)
                v.SetPaddingRelative(leading ? 0 : v.PaddingStart, v.PaddingTop, leading ? v.PaddingEnd : 0, v.PaddingBottom);

            if (ReferenceEquals(v, searchView))
                break;
        }
    }

    // The back arrow takes the magnifier's place while a search goes on, as in Material's search view.
    // SearchView shows its magnifier again when the focus changes, after the focus events, so it is
    // put right again once the view has done that.
    partial void ApplyCancelButton()
    {
        ApplyBackArrow();
        (_bar.Handler?.PlatformView as Android.Views.View)?.Post(ApplyBackArrow);
    }

    private void ApplyBackArrow()
    {
        if (_back is null)
            return;

        var shows = ShowsCancelButton;
        _back.IsVisible = shows;

        if (FindInSearchView("search_mag_icon") is { } magnifier)
            magnifier.Visibility = shows ? Android.Views.ViewStates.Gone : Android.Views.ViewStates.Visible;
    }

    // Material 3's on-surface-variant, the colour of its search view's icons.
    private void PaintIcons(bool dark)
    {
        var colour = dark ? Android.Graphics.Color.Rgb(0xCA, 0xC4, 0xD0) : Android.Graphics.Color.Rgb(0x49, 0x45, 0x4F);
        var tint = Android.Content.Res.ColorStateList.ValueOf(colour);

        foreach (var name in (string[])["search_close_btn", "search_mag_icon"])
        {
            if (FindInSearchView(name) is Android.Widget.ImageView icon)
                icon.ImageTintList = tint;
        }

        if (_back?.Handler?.PlatformView is Android.Widget.ImageView back)
            back.ImageTintList = tint;
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
