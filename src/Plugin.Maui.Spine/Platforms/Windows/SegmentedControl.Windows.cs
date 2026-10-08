using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>A WinUI <see cref="SelectorBar"/>, one item per segment.</summary>
internal sealed class SegmentedControlHandler : ViewHandler<SegmentedControl, SelectorBar>
{
    private const double IconSize = 16;

    public static readonly IPropertyMapper<SegmentedControl, SegmentedControlHandler> Mapper = new PropertyMapper<SegmentedControl, SegmentedControlHandler>(ViewMapper)
    {
        [nameof(SegmentedControl.Segments)] = static (handler, _) => handler.SetSegments(),
        [nameof(SegmentedControl.SelectedIndex)] = static (handler, _) => handler.SetSelection(),
        [nameof(SegmentedControl.SelectedSegmentColor)] = static (handler, _) => handler.SetSegments(),
    };

    private bool _updating;

    public SegmentedControlHandler() : base(Mapper) { }

    protected override SelectorBar CreatePlatformView() => new();

    protected override void ConnectHandler(SelectorBar platformView)
    {
        base.ConnectHandler(platformView);
        platformView.SelectionChanged += OnSelectionChanged;
    }

    protected override void DisconnectHandler(SelectorBar platformView)
    {
        platformView.SelectionChanged -= OnSelectionChanged;
        base.DisconnectHandler(platformView);
    }

    private void OnSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updating || VirtualView is null)
            return;

        VirtualView.OnUserSelected(sender.SelectedItem is { } item ? sender.Items.IndexOf(item) : -1);
    }

    private void SetSegments()
    {
        _updating = true;
        try
        {
            PlatformView.Items.Clear();

            // Icons are drawn in the theme's text colour: an ImageIcon is not tinted.
            var foreground = SegmentedControl.CurrentTheme == AppTheme.Dark ? Colors.White : Colors.Black;

            foreach (var segment in VirtualView.Segments)
            {
                var item = new SelectorBarItem { Text = segment.Title ?? string.Empty, IsEnabled = segment.IsEnabled };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, segment.Title ?? string.Empty);

                if (MenuButton.Icon(this, segment.Svg, IconSize, foreground) is { } png)
                {
                    var bitmap = new BitmapImage();
                    using (var stream = new MemoryStream(png))
                        bitmap.SetSource(stream.AsRandomAccessStream());
                    item.Icon = new ImageIcon { Source = bitmap, Width = IconSize, Height = IconSize };
                }

                PlatformView.Items.Add(item);
            }
        }
        finally
        {
            _updating = false;
        }

        SetSelection();
        VirtualView.InvalidateMeasure();
    }

    private void SetSelection()
    {
        var index = VirtualView.SelectedIndex;

        _updating = true;
        try
        {
            PlatformView.SelectedItem = index >= 0 && index < PlatformView.Items.Count ? PlatformView.Items[index] : null;
        }
        finally
        {
            _updating = false;
        }
    }
}
