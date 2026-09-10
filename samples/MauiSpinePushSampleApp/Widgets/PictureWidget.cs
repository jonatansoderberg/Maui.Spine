using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Widgets;

namespace MauiSpinePushSampleApp.Widgets;

/// <summary>
/// A widget on a picture: the image fills the surface, a gradient shows wherever there is none, and the
/// one line of text sits on an accent-colored box so it stays legible on whatever the picture is.
/// </summary>
/// <remarks>
/// The picture is the sample's own, from <c>Resources/Raw</c>. It is stored at every build — it is small —
/// because the widget reads it from the App Group, where the extension on iOS can reach it, and a fresh
/// install has nothing there yet.
/// </remarks>
/// <param name="widgets">Stores the picture where the widget reads it.</param>
[Widget("picture")]
public sealed class PictureWidgetProvider(IWidgetService widgets) : IWidgetProvider
{
    private const string Picture = "sample_picture.png";

    /// <inheritdoc />
    public async Task<WidgetTimeline> BuildTimelineAsync(WidgetContext context, CancellationToken cancellationToken)
    {
        await using (var picture = await FileSystem.OpenAppPackageFileAsync(Picture))
            await widgets.StoreAssetAsync(Picture, picture, cancellationToken);

        return WidgetTimeline
            .Single(W.VStack(4,
                W.Spacer(),
                // The system's colors, which follow the user: Material You's accent from the wallpaper on
                // Android 12 and later, the app's accent color on iOS.
                W.HStack(W.Text("Spine bild").Headline().Bold().Color(WidgetColor.OnAccent).Accented())
                    .Background(WidgetColor.Accent).Padding(6).CornerRadius(8)))
            .Background(new WidgetGradient([WidgetColor.FromHex("#1B5E3F"), WidgetColor.FromHex("#3FA37A")], WidgetGradientDirection.Diagonal))
            .BackgroundImage(Picture)
            .Refresh(TimeSpan.FromHours(6));
    }
}
