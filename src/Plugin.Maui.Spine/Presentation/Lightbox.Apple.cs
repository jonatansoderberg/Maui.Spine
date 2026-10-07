#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Photos;
using UIKit;

namespace Plugin.Maui.Spine.Presentation;

public partial class Lightbox
{
    private static bool? _canSaveToPhotos;

    internal static partial bool CanSaveToPhotos =>
        _canSaveToPhotos ??= NSBundle.MainBundle.ObjectForInfoDictionary("NSPhotoLibraryAddUsageDescription") is not null;

    private UIImage? CurrentImage => (Handler as LightboxHandler)?.PlatformView.CurrentImageView?.Image;

    /// <remarks>
    /// UIKit's own share sheet rather than MAUI's <see cref="Share"/>, which gives the sheet a title
    /// and a blank document icon: here its header shows the picture, as Photos' does.
    /// </remarks>
    private partial Task ShareFileAsync(string path, Rect anchor)
    {
        if (CurrentImage is not { } image || Platform.GetCurrentUIViewController() is not { } presenter)
            return Task.CompletedTask;

        var done = new TaskCompletionSource();
        var sheet = new UIActivityViewController([new ShareItem(NSUrl.FromFilename(path), image, CurrentItem?.Caption ?? Path.GetFileNameWithoutExtension(path))], null)
        {
            CompletionWithItemsHandler = (_, _, _, _) => done.TrySetResult(),
        };

        // A popover on iPad and the Mac, from the button.
        if (sheet.PopoverPresentationController is { } popover && presenter.View is { } view)
        {
            popover.SourceView = view;
            popover.SourceRect = anchor == Rect.Zero
                ? new CGRect(view.Bounds.GetMidX(), view.Bounds.GetMidY(), 0, 0)
                : new CGRect(anchor.X, anchor.Y, anchor.Width, anchor.Height);
        }

        presenter.PresentViewController(sheet, true, null);
        return done.Task;
    }

    /// <summary>The file to share, with the picture and the caption for the share sheet's header.</summary>
    private sealed class ShareItem(NSUrl file, UIImage image, string title) : UIActivityItemSource
    {
        public override NSObject GetPlaceholderData(UIActivityViewController activityViewController) => file;

        public override NSObject GetItemForActivity(UIActivityViewController activityViewController, NSString? activityType) => file;

        public override LinkPresentation.LPLinkMetadata GetLinkMetadata(UIActivityViewController activityViewController) => new()
        {
            Title = title,
            ImageProvider = new NSItemProvider(image),
            IconProvider = new NSItemProvider(image),
        };
    }

    private async partial Task<string?> WriteCurrentImageAsync()
    {
        if (CurrentImage is not { } image)
            return null;

        // A PNG stays a PNG, so a drawing keeps its edges; anything else goes as a JPEG.
        var png = CurrentItem?.Source is FileImageSource { File: { } file } && file.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        var data = png ? image.AsPNG() : image.AsJPEG(0.92f);
        if (data is null)
            return null;

        var folder = Path.Combine(FileSystem.CacheDirectory, "lightbox");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName(png ? "png" : "jpg"));

        await using (var stream = File.Create(path))
        await data.AsStream().CopyToAsync(stream);

        return path;
    }

    private async partial Task<SaveResult> SaveCurrentImageAsync()
    {
        if (!CanSaveToPhotos)
            return SaveResult.Denied;

        if (CurrentImage is not { } image)
            return SaveResult.NoPicture;

        var access = new TaskCompletionSource<PHAuthorizationStatus>();
        PHPhotoLibrary.RequestAuthorization(PHAccessLevel.AddOnly, status => access.TrySetResult(status));
        if (await access.Task is not (PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited))
            return SaveResult.Denied;

        var saved = new TaskCompletionSource<SaveResult>();
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () => PHAssetChangeRequest.FromImage(image),
            (success, error) =>
            {
                if (error is not null)
                    Console.WriteLine($"[Spine] Lightbox: the photo library refused the image: {error.LocalizedDescription}");

                saved.TrySetResult(success ? SaveResult.Saved : SaveResult.Failed);
            });

        return await saved.Task;
    }
}

internal sealed class LightboxHandler : ViewHandler<Lightbox, LightboxView>
{
    public static readonly IPropertyMapper<Lightbox, LightboxHandler> Mapper = new PropertyMapper<Lightbox, LightboxHandler>(ViewMapper)
    {
        [nameof(Lightbox.ItemsSource)] = static (handler, view) => handler.PlatformView.SetItems(view.ItemsSource ?? []),
        [nameof(Lightbox.Position)] = static (handler, view) => handler.PlatformView.Show(view.Position),
    };

    public LightboxHandler() : base(Mapper) { }

    protected override LightboxView CreatePlatformView() => new(this);
}

/// <summary>
/// A paging scroll view of zooming scroll views, one for the image showing and one on each side of
/// it, moved along as the pages go by. Pages are a gap apart, as Photos has them, which the view
/// cuts off at rest.
/// </summary>
internal sealed class LightboxView : UIView
{
    /// <summary>The black between two images while they slide past each other.</summary>
    private const double Gap = 20;

    private readonly WeakReference<LightboxHandler> _handler;
    private readonly UIScrollView _pager;
    private readonly Dictionary<int, LightboxPage> _pages = [];
    private readonly Stack<LightboxPage> _spare = new();
    private readonly UIPanGestureRecognizer _dismiss;
    private readonly UILabel _caption;

    private IReadOnlyList<LightboxImage> _items = [];
    private int _position;
    private CGSize _laidOut;

    public LightboxView(LightboxHandler handler)
    {
        _handler = new(handler);
        ClipsToBounds = true;

        _pager = new UIScrollView
        {
            PagingEnabled = true,
            ShowsHorizontalScrollIndicator = false,
            ShowsVerticalScrollIndicator = false,
            ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never,
            DirectionalLockEnabled = true,
            AlwaysBounceVertical = false,
        };
        _pager.Scrolled += (_, _) => OnPaged();
        AddSubview(_pager);

        // A drag down closes the page. It is decided before the pages slide or an image pans, so
        // they wait for it to fail: a drag sideways, or any drag on an image zoomed in.
        _dismiss = new UIPanGestureRecognizer(OnDismissPan) { ShouldBegin = _ => ShouldDismiss() };
        AddGestureRecognizer(_dismiss);
        _pager.PanGestureRecognizer.RequireGestureRecognizerToFail(_dismiss);

        _caption = new UILabel
        {
            TextColor = UIColor.White,
            Font = UIFont.PreferredSubheadline,
            AdjustsFontForContentSizeCategory = true,
            TextAlignment = UITextAlignment.Center,
            Lines = 3,
            UserInteractionEnabled = false,
        };
        _caption.Layer.ShadowColor = UIColor.Black.CGColor;
        _caption.Layer.ShadowOpacity = 0.6f;
        _caption.Layer.ShadowRadius = 4;
        _caption.Layer.ShadowOffset = CGSize.Empty;
        AddSubview(_caption);

        handler.VirtualView.FadeChrome = opacity => _caption.Alpha = (nfloat)opacity;
        handler.VirtualView.BottomInsetChanged += LayoutCaption;
    }

    private Lightbox? VirtualView => _handler.TryGetTarget(out var handler) ? handler.VirtualView : null;

    private IMauiContext? MauiContext => _handler.TryGetTarget(out var handler) ? handler.MauiContext : null;

    private nfloat PageWidth => Bounds.Width + (nfloat)Gap;

    /// <summary>The image view of the image showing, once it has its picture.</summary>
    public UIImageView? CurrentImageView =>
        _pages.TryGetValue(_position, out var page) && page.ImageView.Image is not null ? page.ImageView : null;

    public void SetItems(IReadOnlyList<LightboxImage> items)
    {
        _items = items;

        foreach (var page in _pages.Values)
            Recycle(page);

        _pages.Clear();
        _position = Math.Clamp(_position, 0, Math.Max(0, items.Count - 1));
        _laidOut = CGSize.Empty;
        SetNeedsLayout();
    }

    public void Show(int position)
    {
        position = Math.Clamp(position, 0, Math.Max(0, _items.Count - 1));
        if (position == _position)
            return;

        _position = position;
        if (Bounds.Width > 0)
            _pager.ContentOffset = new CGPoint(_position * PageWidth, 0);

        Tile();
        LayoutCaption();
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        if (Bounds.Size == _laidOut || Bounds.Width <= 0)
            return;

        _laidOut = Bounds.Size;
        _pager.Frame = new CGRect(0, 0, PageWidth, Bounds.Height);
        _pager.ContentSize = new CGSize(PageWidth * _items.Count, Bounds.Height);
        _pager.ContentOffset = new CGPoint(_position * PageWidth, 0);

        foreach (var (index, page) in _pages)
            page.Frame = FrameOf(index);

        Tile();
        LayoutCaption();
    }

    /// <summary>The caption of the image showing, at the foot of the page above the home indicator.</summary>
    private void LayoutCaption()
    {
        var margin = (nfloat)HeaderBarConstants.PageMargin;
        _caption.Text = _position < _items.Count ? _items[_position].Caption : null;

        var width = Bounds.Width - 2 * margin - SafeAreaInsets.Left - SafeAreaInsets.Right;
        var size = _caption.SizeThatFits(new CGSize(width, nfloat.MaxValue));
        var bottom = Bounds.Height - margin - (nfloat)(VirtualView?.BottomInset ?? 0);
        _caption.Frame = new CGRect(margin + SafeAreaInsets.Left, bottom - size.Height, width, size.Height);
    }

    public override void SafeAreaInsetsDidChange()
    {
        base.SafeAreaInsetsDidChange();
        LayoutCaption();
    }

    private CGRect FrameOf(int index) => new(index * PageWidth, 0, Bounds.Width, Bounds.Height);

    private void OnPaged()
    {
        if (Bounds.Width <= 0 || _items.Count == 0)
            return;

        var position = (int)Math.Clamp(Math.Round(_pager.ContentOffset.X / PageWidth), 0, _items.Count - 1);
        if (position != _position)
        {
            _position = position;
            LayoutCaption();

            if (VirtualView is { } view)
                view.Position = position;
        }

        Tile();
    }

    /// <summary>Keeps a page for the image showing and its neighbours, and lets go of the rest.</summary>
    private void Tile()
    {
        if (Bounds.Width <= 0)
            return;

        foreach (var index in _pages.Keys.Where(i => Math.Abs(i - _position) > 1).ToList())
        {
            Recycle(_pages[index]);
            _pages.Remove(index);
        }

        for (var index = Math.Max(0, _position - 1); index <= Math.Min(_items.Count - 1, _position + 1); index++)
        {
            if (_pages.TryGetValue(index, out var page))
            {
                // A page scrolled away is back at its full view when it returns, as in Photos.
                if (index != _position)
                    page.ZoomToFit();

                continue;
            }

            page = _spare.TryPop(out var spare) ? spare : NewPage();
            page.Frame = FrameOf(index);
            page.Hidden = false;
            _pages[index] = page;
            Load(page, index);
        }

        ReportCurrent();
    }

    private LightboxPage NewPage()
    {
        var page = new LightboxPage { Tapped = () => VirtualView?.OnTapped() };
        page.PanGestureRecognizer.RequireGestureRecognizerToFail(_dismiss);
        _pager.AddSubview(page);
        return page;
    }

    private void Recycle(LightboxPage page)
    {
        page.Clear();
        page.Hidden = true;
        _spare.Push(page);
    }

    private async void Load(LightboxPage page, int index)
    {
        var source = _items[index].Source;
        var token = page.Begin();

        UIImage? image = null;
        try
        {
            if (MauiContext is { } context && source is IImageSource imageSource)
                image = (await imageSource.GetPlatformImageAsync(context))?.Value;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[Spine] Lightbox: image {index} did not load: {exception.Message}");
        }

        if (!page.Show(token, image))
            return;

        if (image is null)
            Console.WriteLine($"[Spine] Lightbox: image {index} ({source}) has no picture.");

        if (index == _position)
            ReportCurrent();
    }

    private void ReportCurrent()
    {
        if (VirtualView is not { } view)
            return;

        if (_pages.TryGetValue(_position, out var page) && page.IsLoading)
            view.OnCurrentImageLoading();
        else
            view.OnCurrentImageReady();
    }

    private bool ShouldDismiss()
    {
        var velocity = _dismiss.VelocityInView(this);
        if (velocity.Y <= 0 || Math.Abs(velocity.Y) < Math.Abs(velocity.X))
            return false;

        if (_pages.TryGetValue(_position, out var page) && page.IsZoomedIn)
            return false;

        return VirtualView?.CanDismissByDrag == true;
    }

    private void OnDismissPan(UIPanGestureRecognizer pan)
    {
        var status = pan.State switch
        {
            UIGestureRecognizerState.Began => GestureStatus.Started,
            UIGestureRecognizerState.Changed => GestureStatus.Running,
            UIGestureRecognizerState.Ended => GestureStatus.Completed,
            _ => GestureStatus.Canceled,
        };

        var translation = pan.TranslationInView(this);
        var location = pan.LocationInView(this);
        var start = new Point(location.X - translation.X, location.Y - translation.Y);
        VirtualView?.OnDismissDrag(status, start, translation.X, translation.Y, pan.VelocityInView(this).Y);
    }
}

/// <summary>
/// One image, fitted to the page at its smallest zoom and centred while it is smaller than the
/// page; a pinch zooms it, and a double tap zooms in about the tap or back out.
/// </summary>
internal sealed class LightboxPage : UIScrollView
{
    /// <summary>How far a double tap zooms in.</summary>
    private const double DoubleTapZoom = 2.5;

    private int _token;
    private CGSize _fitted;

    public UIImageView ImageView { get; } = new() { ContentMode = UIViewContentMode.ScaleAspectFill, ClipsToBounds = true };

    public bool IsLoading { get; private set; }

    public bool IsZoomedIn => ZoomScale > MinimumZoomScale + 0.01;

    /// <summary>A single tap, once it is clear it is not the first of a double tap.</summary>
    public Action? Tapped { get; init; }

    public LightboxPage()
    {
        ShowsHorizontalScrollIndicator = false;
        ShowsVerticalScrollIndicator = false;
        ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never;
        DecelerationRate = DecelerationRateFast;
        BouncesZoom = true;
        AddSubview(ImageView);

        ViewForZoomingInScrollView = _ => ImageView;
        DidZoom += (_, _) => Centre();

        var doubleTap = new UITapGestureRecognizer(OnDoubleTap) { NumberOfTapsRequired = 2 };
        var tap = new UITapGestureRecognizer(() => Tapped?.Invoke());
        tap.RequireGestureRecognizerToFail(doubleTap);
        AddGestureRecognizer(doubleTap);
        AddGestureRecognizer(tap);
    }

    /// <summary>Starts showing another image; the token tells a load that finishes later whether the page still shows it.</summary>
    public int Begin()
    {
        Clear();
        IsLoading = true;
        return _token;
    }

    /// <summary>Shows <paramref name="image"/> if the page still shows what it was loaded for.</summary>
    public bool Show(int token, UIImage? image)
    {
        if (token != _token)
            return false;

        IsLoading = false;
        ImageView.Image = image;
        _fitted = CGSize.Empty;
        SetNeedsLayout();
        LayoutIfNeeded();
        return true;
    }

    public void Clear()
    {
        _token++;
        IsLoading = false;
        ImageView.Image = null;
        _fitted = CGSize.Empty;
        ZoomScale = 1;
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        if (Bounds.Size != _fitted)
            ZoomToFit();
    }

    /// <summary>Fits the image to the page, at its smallest zoom.</summary>
    public void ZoomToFit()
    {
        _fitted = Bounds.Size;
        MinimumZoomScale = 1;
        ZoomScale = 1;

        if (ImageView.Image is not { } image || image.Size.Width <= 0 || image.Size.Height <= 0 || Bounds.Width <= 0)
        {
            ImageView.Frame = CGRect.Empty;
            ContentSize = CGSize.Empty;
            return;
        }

        var scale = nfloat.Min(Bounds.Width / image.Size.Width, Bounds.Height / image.Size.Height);
        var size = new CGSize(image.Size.Width * scale, image.Size.Height * scale);

        ImageView.Frame = new CGRect(CGPoint.Empty, size);
        ContentSize = size;

        // Down to its own pixels, and at least as far as a double tap goes.
        MaximumZoomScale = nfloat.Max((nfloat)DoubleTapZoom * 1.5f, image.Size.Width * image.CurrentScale / UIScreen.MainScreen.Scale / size.Width);
        Centre();
        ContentOffset = new CGPoint(-ContentInset.Left, -ContentInset.Top);
    }

    private void Centre()
    {
        var left = nfloat.Max(0, (Bounds.Width - ContentSize.Width) / 2);
        var top = nfloat.Max(0, (Bounds.Height - ContentSize.Height) / 2);
        ContentInset = new UIEdgeInsets(top, left, top, left);
    }

    private void OnDoubleTap(UITapGestureRecognizer tap)
    {
        if (ImageView.Image is null)
            return;

        if (IsZoomedIn)
        {
            SetZoomScale(MinimumZoomScale, animated: true);
            return;
        }

        var point = tap.LocationInView(ImageView);
        var scale = nfloat.Min(MaximumZoomScale, (nfloat)DoubleTapZoom);
        var size = new CGSize(Bounds.Width / scale, Bounds.Height / scale);
        ZoomToRect(new CGRect(point.X - size.Width / 2, point.Y - size.Height / 2, size.Width, size.Height), animated: true);
    }
}
#endif
