#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

internal sealed class SegmentedControlHandler : ViewHandler<SegmentedControl, UISegmentedControl>
{
    private const double IconSize = 18;
    private const double IconWithTitleSize = 16;
    private const double IconSpacing = 6;
    private const double TitleSize = 13;

    public static readonly IPropertyMapper<SegmentedControl, SegmentedControlHandler> Mapper = new PropertyMapper<SegmentedControl, SegmentedControlHandler>(ViewMapper)
    {
        [nameof(SegmentedControl.Segments)] = static (handler, _) => handler.SetSegments(),
        [nameof(SegmentedControl.SelectedIndex)] = static (handler, _) => handler.SetSelection(),
        [nameof(SegmentedControl.SelectedSegmentColor)] = static (handler, _) => handler.SetColors(),
        [nameof(IView.FlowDirection)] = static (handler, view) =>
        {
            ViewHandler.MapFlowDirection(handler, view);
            handler.SetSegments();
        },
    };

    public SegmentedControlHandler() : base(Mapper) { }

    protected override UISegmentedControl CreatePlatformView() => new();

    protected override void ConnectHandler(UISegmentedControl platformView)
    {
        base.ConnectHandler(platformView);
        platformView.ValueChanged += OnValueChanged;
    }

    protected override void DisconnectHandler(UISegmentedControl platformView)
    {
        platformView.ValueChanged -= OnValueChanged;
        base.DisconnectHandler(platformView);
    }

    private void OnValueChanged(object? sender, EventArgs e) =>
        VirtualView?.OnUserSelected((int)PlatformView.SelectedSegment);

    private void SetSegments()
    {
        var control = PlatformView;
        control.RemoveAllSegments();

        var segments = VirtualView.Segments;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (Image(segment) is { } image)
                control.InsertSegment(image, i, false);
            else
                control.InsertSegment(segment.Title ?? string.Empty, i, false);

            control.SetEnabled(segment.IsEnabled, i);
        }

        SetSelection();
        VirtualView.InvalidateMeasure();
    }

    private void SetSelection()
    {
        var index = VirtualView.SelectedIndex;
        PlatformView.SelectedSegment = index < PlatformView.NumberOfSegments ? index : -1;
    }

    private void SetColors()
    {
        var color = VirtualView.SelectedSegmentColor;
        PlatformView.SelectedSegmentTintColor = color?.ToPlatform();
        PlatformView.SetTitleTextAttributes(
            color is null ? new UIStringAttributes() : new UIStringAttributes { ForegroundColor = SpineAccent.TextOn(color).ToPlatform() },
            UIControlState.Selected);
    }

    /// <remarks>
    /// A segment shows a title or an image, never both; one with both gets the icon and the title
    /// drawn into a single template image, which UIKit tints like a title.
    /// </remarks>
    private UIImage? Image(Segment segment)
    {
        var hasTitle = !string.IsNullOrEmpty(segment.Title);
        if (MenuButton.Icon(this, segment.Svg, hasTitle ? IconWithTitleSize : IconSize, Colors.Black) is not { } png)
            return null;

        using var data = NSData.FromArray(png);
        if (UIImage.LoadFromData(data, UIScreen.MainScreen.Scale) is not { } icon)
            return null;

        var image = (hasTitle ? WithTitle(icon, segment.Title!) : icon).ImageWithRenderingMode(UIImageRenderingMode.AlwaysTemplate);
        image.AccessibilityLabel = segment.Title;
        return image;
    }

    private UIImage WithTitle(UIImage icon, string title)
    {
        var attributes = new UIStringAttributes { Font = UIFont.SystemFontOfSize((nfloat)TitleSize, UIFontWeight.Medium), ForegroundColor = UIColor.Black };
        var text = new NSString(title);
        var textSize = text.GetSizeUsingAttributes(attributes);
        var width = Math.Ceiling(icon.Size.Width + IconSpacing + textSize.Width);
        var height = Math.Ceiling(Math.Max(icon.Size.Height, textSize.Height));
        var rightToLeft = ((IVisualElementController)VirtualView).EffectiveFlowDirection.HasFlag(EffectiveFlowDirection.RightToLeft);

        using var renderer = new UIGraphicsImageRenderer(new CGSize(width, height));
        return renderer.CreateImage(_ =>
        {
            var iconX = rightToLeft ? width - icon.Size.Width : 0;
            var textX = rightToLeft ? 0 : icon.Size.Width + IconSpacing;
            icon.Draw(new CGPoint(iconX, (height - icon.Size.Height) / 2));
            text.DrawString(new CGPoint(textX, (height - textSize.Height) / 2), attributes);
        });
    }
}
#endif
