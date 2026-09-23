using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpineSampleApp.Widgets;

/// <summary>
/// A picture as the whole tree, on a gradient with a picture over it: the shape of a widget that draws its
/// content itself and hands Spine the result. The card is narrower than the widget and is centred on it on
/// both platforms.
/// </summary>
/// <remarks>
/// Both PNGs are the sample's own, from <c>Resources/Raw</c>, stored at every build because the widget reads
/// them from the shared container and a fresh install has nothing there yet.
/// </remarks>
[Widget("card")]
public sealed class CardWidget(IWidgetService _widgets) : IWidgetProvider
{
    private const string Card = "sample_card.png";
    private const string Backdrop = "sample_backdrop.png";

    private static readonly WidgetGradient Ground = new([WidgetColor.FromHex("#1B5E3F"), WidgetColor.FromHex("#3FA37A")], WidgetGradientDirection.Diagonal);

    public async Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        foreach (var asset in new[] { Card, Backdrop })
            await using (var png = await FileSystem.OpenAppPackageFileAsync(asset))
                await _widgets.StoreAssetAsync(asset, png, cancellationToken);

        return new WidgetTimeline()
            .Add(DateTimeOffset.UtcNow, W.Image(Card, height: 120).FullColor(), new WidgetSurface(Ground, Backdrop))
            .Refresh(TimeSpan.FromHours(6));
    }
}
