#if IOS || MACCATALYST

using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    // Every MAUI mapper that repaints something the glass owns, so the glass is re-asserted after each of them.
    // Keys a handler does not have (Text on ImageButton) become harmless extra entries.
    static readonly string[] GlassKeys =
    [
        Glass.MapperKey,
        nameof(IView.Background),
        nameof(IButtonStroke.CornerRadius),
        nameof(IButtonStroke.StrokeThickness),
        nameof(IButtonStroke.StrokeColor),
        nameof(IPadding.Padding),
        nameof(IText.Text),
        nameof(ITextStyle.TextColor),
        nameof(ITextStyle.Font),
        nameof(ITextStyle.CharacterSpacing),
        nameof(Microsoft.Maui.IImage.Source),
        nameof(Button.ContentLayout),
    ];

    static void ConfigureGlassButtons()
    {
        foreach (var key in GlassKeys)
        {
            ButtonHandler.Mapper.AppendToMapping(key, ApplyGlass);
            ImageButtonHandler.Mapper.AppendToMapping(key, ApplyGlass);
        }
    }

    static void ApplyGlass(IElementHandler handler, IElement element)
    {
        if (handler.PlatformView is not UIButton button || element is not VisualElement view)
            return;

        var style = Glass.GetStyle(view);
        if (style == GlassStyle.None)
        {
            if (button.Configuration is not null)
                RestorePlainButton(handler, button, view);
            return;
        }

        if (!OperatingSystem.IsIOSVersionAtLeast(26))
            return;

        var config = style switch
        {
            GlassStyle.Prominent => UIButtonConfiguration.ProminentGlassButtonConfiguration,
            GlassStyle.Clear => UIButtonConfiguration.ClearGlassButtonConfiguration,
            GlassStyle.ProminentClear => UIButtonConfiguration.ProminentClearGlassButtonConfiguration,
            _ => UIButtonConfiguration.GlassButtonConfiguration,
        };

        // MAUI painted these a moment ago, and paints them again on every visual-state change.
        button.BackgroundColor = UIColor.Clear;
        button.Layer.CornerRadius = 0;
        button.Layer.BorderWidth = 0;
        // The press highlight grows past the frame; ImageButtonHandler turns clipping on.
        button.ClipsToBounds = false;

        if (style is GlassStyle.Prominent or GlassStyle.ProminentClear
            && (view.BackgroundColor ?? (view.Background as SolidColorBrush)?.Color) is { Alpha: > 0 } tint)
        {
            config.BaseBackgroundColor = tint.ToPlatform();
        }

        switch (view)
        {
            case Button b:
                ApplyGlassButton(handler, button, b, config);
                break;
            case ImageButton:
                // The bitmap carries its own inset (SvgImageSource.Padding); the capsule adds none.
                config.ContentInsets = new NSDirectionalEdgeInsets(0, 0, 0, 0);
                break;
        }

        // The state image MAUI set with SetImage is folded into a configuration only when it
        // arrives after the configuration exists; an in-memory bitmap arrives before, so take it now.
        config.Image = button.ImageForState(UIControlState.Normal);

        // And when it arrives later (a Button's image is loaded asynchronously and possibly
        // resized), the next configuration update picks it up.
        button.ConfigurationUpdateHandler ??= static btn =>
        {
            if (btn.Configuration is not { Image: null } current
                || btn.ImageForState(UIControlState.Normal) is not { } image)
                return;

            current.Image = image;
            btn.Configuration = current;
        };

        button.Configuration = config;
    }

    static void ApplyGlassButton(IElementHandler handler, UIButton platformButton, Button button, UIButtonConfiguration config)
    {
        // MAUI measures a Button as its own title plus Padding, so the configuration must lay out
        // with exactly that font and exactly those insets or the title wraps inside the capsule.
        // The transformer, rather than an attributed title, because UIKit swaps in the title that
        // SetTitle set whenever a later mapper calls it again.
        var font = handler.GetRequiredService<IFontManager>().GetFont(((ITextStyle)button).Font);
        var kerning = button.CharacterSpacing;

        config.Title = platformButton.Title(UIControlState.Normal) ?? string.Empty;
        config.TitleTextAttributesTransformer = attributes =>
        {
            var result = new NSMutableDictionary(attributes);
            result[UIStringAttributeKey.Font] = font;
            if (kerning != 0)
                result[UIStringAttributeKey.KerningAdjustment] = NSNumber.FromDouble(kerning);
            return result;
        };

        if (button.TextColor is { } textColor)
            config.BaseForegroundColor = textColor.ToPlatform();

        // A Button without a Padding setter carries NaN, which MAUI itself replaces with the handler default.
        var padding = button.Padding.IsNaN ? ButtonHandler.DefaultPadding : button.Padding;
        config.ContentInsets = new NSDirectionalEdgeInsets(
            (nfloat)padding.Top, (nfloat)padding.Left, (nfloat)padding.Bottom, (nfloat)padding.Right);

        if (button.ImageSource is not null)
        {
            config.ImagePlacement = button.ContentLayout.Position switch
            {
                Button.ButtonContentLayout.ImagePosition.Top => NSDirectionalRectEdge.Top,
                Button.ButtonContentLayout.ImagePosition.Bottom => NSDirectionalRectEdge.Bottom,
                Button.ButtonContentLayout.ImagePosition.Right => NSDirectionalRectEdge.Trailing,
                _ => NSDirectionalRectEdge.Leading,
            };
            config.ImagePadding = (nfloat)button.ContentLayout.Spacing;
        }
    }

    // Glass was turned off at runtime: drop the configuration and let MAUI's own mappers paint again.
    static void RestorePlainButton(IElementHandler handler, UIButton button, VisualElement view)
    {
        button.Configuration = null;
        button.ConfigurationUpdateHandler = null;
        button.ClipsToBounds = view is ImageButton;

        foreach (var key in GlassKeys)
        {
            if (key != Glass.MapperKey)
                handler.UpdateValue(key);
        }
    }
}

#endif
