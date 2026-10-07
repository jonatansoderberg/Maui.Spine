using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// The row of buttons at the foot of a lightbox, as in Photos: Share at the leading edge, the
/// others spread to the trailing edge. It belongs to the region, as the header bar does, so it
/// stays put while the image zooms or is dragged, and fades with the rest of the chrome.
/// </summary>
internal sealed class LightboxToolbar : ContentView
{
    private readonly FlexLayout _row = new()
    {
        Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
        JustifyContent = Microsoft.Maui.Layouts.FlexJustify.SpaceBetween,
        AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
    };

    public LightboxToolbar()
    {
        VerticalOptions = LayoutOptions.End;
        HeightRequest = HeaderBarConstants.Height;
        IsVisible = false;
        Content = _row;
    }

    /// <summary>
    /// Shows <paramref name="actions"/>, or hides the toolbar when there are none, as buttons the
    /// size of the header bar's <paramref name="header"/>, so the two line up at the sides.
    /// </summary>
    public void Show(IReadOnlyList<PageAction> actions, HeaderBarView header)
    {
        _row.Clear();

        foreach (var action in actions)
        {
            var view = new PageActionView
            {
                HeightRequest = HeaderBarConstants.Height,
                IconWidth = header.IconButtonWidth,
                ButtonPadding = new Thickness(HeaderBarConstants.RegionButtonPadding),
                Foreground = Colors.White,
                Glass = HeaderBarGlass.Clear,
                OverContent = true,
            };

            view.Action = action;
            _row.Add(view);
        }

        IsVisible = actions.Count > 0;
    }

    /// <summary>The button that shows <paramref name="action"/>, to anchor a popover to.</summary>
    public View? ViewOf(PageAction action) =>
        _row.Children.OfType<PageActionView>().FirstOrDefault(view => ReferenceEquals(view.Action, action));
}
