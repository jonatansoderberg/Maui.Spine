#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WImage = Microsoft.UI.Xaml.Controls.Image;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// A <see cref="FlipView"/> of zooming <see cref="ScrollViewer"/>s, one per image: the mouse
/// wheel with Ctrl, a pinch on a touch screen and a double tap zoom; the arrows page. The zoom out
/// of the thumbnail and the drag down to close are not there on Windows: it opens and closes with
/// a fade.
/// </summary>
internal sealed class LightboxHandler : ViewHandler<Lightbox, FlipView>
{
    public static readonly IPropertyMapper<Lightbox, LightboxHandler> Mapper = new PropertyMapper<Lightbox, LightboxHandler>(ViewMapper)
    {
        [nameof(Lightbox.ItemsSource)] = static (handler, view) => handler.SetItems(view.ItemsSource ?? []),
        [nameof(Lightbox.Position)] = static (handler, view) => handler.Show(view.Position),
    };

    public LightboxHandler() : base(Mapper) { }

    protected override FlipView CreatePlatformView() => new() { Background = null };

    protected override void ConnectHandler(FlipView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.SelectionChanged += OnSelectionChanged;
    }

    protected override void DisconnectHandler(FlipView platformView)
    {
        platformView.SelectionChanged -= OnSelectionChanged;
        base.DisconnectHandler(platformView);
    }

    private void SetItems(IReadOnlyList<LightboxImage> items)
    {
        PlatformView.Items.Clear();
        foreach (var item in items)
            PlatformView.Items.Add(Page(item));

        Show(VirtualView.Position);
    }

    private void Show(int position)
    {
        if (position >= 0 && position < PlatformView.Items.Count && PlatformView.SelectedIndex != position)
            PlatformView.SelectedIndex = position;

        VirtualView.OnCurrentImageReady();
    }

    private void OnSelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (PlatformView.SelectedIndex >= 0 && VirtualView.Position != PlatformView.SelectedIndex)
            VirtualView.Position = PlatformView.SelectedIndex;
    }

    private ScrollViewer Page(LightboxImage item)
    {
        var image = new WImage { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
        var page = new ScrollViewer
        {
            ZoomMode = Microsoft.UI.Xaml.Controls.ZoomMode.Enabled,
            MinZoomFactor = 1,
            MaxZoomFactor = 4,
            HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden,
            HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Auto,
            VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Auto,
            Content = image,
        };

        // The image fills the page at its smallest zoom, fitted.
        page.SizeChanged += (_, args) =>
        {
            image.Width = args.NewSize.Width;
            image.Height = args.NewSize.Height;
        };

        page.Tapped += (_, _) => VirtualView?.OnTapped();
        page.DoubleTapped += (_, args) =>
        {
            if (page.ZoomFactor > 1.01f)
            {
                page.ChangeView(0, 0, 1);
                return;
            }

            var point = args.GetPosition(image);
            page.ChangeView(point.X * 1.5, point.Y * 1.5, 2.5f);
        };

        Load(image, item.Source);
        return page;
    }

    private async void Load(WImage image, ImageSource source)
    {
        try
        {
            if (MauiContext is { } context && source is IImageSource imageSource)
                image.Source = (await imageSource.GetPlatformImageAsync(context))?.Value;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[Spine] Lightbox: an image did not load: {exception.Message}");
        }
    }
}
#endif
