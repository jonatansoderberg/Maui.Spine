#if IOS || MACCATALYST
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace GlassSpike;

public static partial class Glass
{
	static partial void RegisterPlatform()
	{
		string[] keys =
		[
			MapperKey,
			nameof(IView.Background),
			nameof(IButtonStroke.CornerRadius),
			nameof(IButtonStroke.StrokeThickness),
			nameof(IButtonStroke.StrokeColor),
			nameof(IPadding.Padding),
			nameof(ITextStyle.TextColor),
			nameof(ITextStyle.Font),
			nameof(IText.Text),
			nameof(Microsoft.Maui.IImage.Source),
			nameof(Button.ContentLayout),
		];

		foreach (var key in keys)
		{
			ButtonHandler.Mapper.AppendToMapping(key, Apply);
			ImageButtonHandler.Mapper.AppendToMapping(key, Apply);
		}
	}

	static void Apply(IElementHandler handler, IElement element)
	{
		if (handler.PlatformView is not UIButton button || element is not VisualElement view)
			return;

		var style = GetStyle(view);
		if (style == GlassStyle.None || !OperatingSystem.IsIOSVersionAtLeast(26))
			return;

		var config = style switch
		{
			GlassStyle.Prominent => UIButtonConfiguration.ProminentGlassButtonConfiguration,
			GlassStyle.Clear => UIButtonConfiguration.ClearGlassButtonConfiguration,
			GlassStyle.ProminentClear => UIButtonConfiguration.ProminentClearGlassButtonConfiguration,
			_ => UIButtonConfiguration.GlassButtonConfiguration,
		};

		// MAUI's own mappers paint a solid background and round/clip the layer on every update;
		// undo that so the glass is the only surface.
		button.BackgroundColor = UIColor.Clear;
		button.Layer.CornerRadius = 0;
		button.Layer.BorderWidth = 0;
		button.ClipsToBounds = false;

		var background = view.BackgroundColor ?? (view.Background as SolidColorBrush)?.Color;
		if (style is GlassStyle.Prominent or GlassStyle.ProminentClear && view.AutomationId != "notint" && background is { Alpha: > 0 })
			config.BaseBackgroundColor = background.ToPlatform();

		switch (view)
		{
			case Button b:
				if (b.ImageSource is not null)
				{
					config.ImagePlacement = b.ContentLayout.Position switch
					{
						Button.ButtonContentLayout.ImagePosition.Top => NSDirectionalRectEdge.Top,
						Button.ButtonContentLayout.ImagePosition.Bottom => NSDirectionalRectEdge.Bottom,
						Button.ButtonContentLayout.ImagePosition.Right => NSDirectionalRectEdge.Trailing,
						_ => NSDirectionalRectEdge.Leading,
					};
					config.ImagePadding = (nfloat)b.ContentLayout.Spacing;
				}

				if (!b.Padding.IsEmpty)
					config.ContentInsets = new NSDirectionalEdgeInsets((nfloat)b.Padding.Top, (nfloat)b.Padding.Left, (nfloat)b.Padding.Bottom, (nfloat)b.Padding.Right);

				{
					var font = handler.GetRequiredService<IFontManager>().GetFont(((ITextStyle)b).Font);
					config.AttributedTitle = new NSAttributedString(b.Text ?? string.Empty, new UIStringAttributes
					{
						Font = font,
						ForegroundColor = b.TextColor?.ToPlatform(),
					});
				}
				break;

			case ImageButton:
				config.ContentInsets = new NSDirectionalEdgeInsets(0, 0, 0, 0);
				if (view.AutomationId == "svgcopy")
				{
					// Template rendering: the configuration colours the glyph like its title text.
					button.ConfigurationUpdateHandler = btn =>
					{
						var c = btn.Configuration;
						if (c is null) return;
						var img = btn.ImageForState(UIControlState.Normal);
						if (img is not null && c.Image is null)
						{
							c.Image = img.ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);
							btn.Configuration = c;
						}
					};
				}
				break;
		}

		button.Configuration = config;
		((IView)view).InvalidateMeasure();
	}
}
#endif
