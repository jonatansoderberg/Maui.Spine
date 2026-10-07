#if ANDROID
using Android.Animation;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Provider;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AView = Android.Views.View;
using Path = System.IO.Path;
using RectF = Android.Graphics.RectF;

namespace Plugin.Maui.Spine.Presentation;

public partial class Lightbox
{
    /// <remarks>MediaStore takes a picture without any permission from Android 10.</remarks>
    internal static partial bool CanSaveToPhotos => OperatingSystem.IsAndroidVersionAtLeast(29);

    private Bitmap? CurrentBitmap => (Handler as LightboxHandler)?.PlatformView.CurrentBitmap;

    private bool CurrentIsPng => CurrentItem?.Source is FileImageSource { File: { } file } && file.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private async partial Task<string?> WriteCurrentImageAsync()
    {
        if (CurrentBitmap is not { } bitmap)
            return null;

        var png = CurrentIsPng;
        var folder = Path.Combine(FileSystem.CacheDirectory, "lightbox");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName(png ? "png" : "jpg"));

        await using var stream = File.Create(path);
        await bitmap.CompressAsync(png ? Bitmap.CompressFormat.Png! : Bitmap.CompressFormat.Jpeg!, 92, stream);
        return path;
    }

    private async partial Task<SaveResult> SaveCurrentImageAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return SaveResult.Denied;

        if (CurrentBitmap is not { } bitmap || Platform.AppContext.ContentResolver is not { } resolver)
            return SaveResult.NoPicture;

        var png = CurrentIsPng;
        var values = new Android.Content.ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, FileName(png ? "png" : "jpg"));
        values.Put(MediaStore.IMediaColumns.MimeType, png ? "image/png" : "image/jpeg");
        values.Put(MediaStore.IMediaColumns.RelativePath, Android.OS.Environment.DirectoryPictures);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        // Written while pending, so the gallery never shows half a picture.
        if (resolver.Insert(MediaStore.Images.Media.ExternalContentUri!, values) is not { } uri)
            return SaveResult.Failed;

        try
        {
            await using (var stream = resolver.OpenOutputStream(uri))
            {
                if (stream is null)
                    throw new IOException("MediaStore gave no stream to write the picture to.");

                await bitmap.CompressAsync(png ? Bitmap.CompressFormat.Png! : Bitmap.CompressFormat.Jpeg!, 92, stream);
            }

            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
            return SaveResult.Saved;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[Spine] Lightbox: the picture could not be written to MediaStore: {exception.Message}");
            resolver.Delete(uri, null, null);
            return SaveResult.Failed;
        }
    }

    RectF? IZoomFocus.FocusIn(AView ancestor) => (Handler as LightboxHandler)?.PlatformView.CurrentImageRectIn(ancestor);
}

internal sealed class LightboxHandler : ViewHandler<Lightbox, LightboxView>
{
    public static readonly IPropertyMapper<Lightbox, LightboxHandler> Mapper = new PropertyMapper<Lightbox, LightboxHandler>(ViewMapper)
    {
        [nameof(Lightbox.ItemsSource)] = static (handler, view) => handler.PlatformView.SetItems(view.ItemsSource ?? []),
        [nameof(Lightbox.Position)] = static (handler, view) => handler.PlatformView.Show(view.Position),
    };

    public LightboxHandler() : base(Mapper) { }

    protected override LightboxView CreatePlatformView() => new(Context, this);
}

/// <summary>
/// A horizontal <see cref="RecyclerView"/> that snaps a page at a time, each page a
/// <see cref="ZoomImageView"/>. Pages are a gap apart, as in Photos: the pager is wider than the
/// view by the gap and each page carries half of it on either side. It also takes a drag down off
/// its pages, to close the lightbox, as <see cref="BackSwipeHostHandler"/> takes the back-swipe.
/// </summary>
internal sealed class LightboxView : FrameLayout
{
    /// <summary>The black between two images while they slide past each other, in dp.</summary>
    private const int Gap = 20;

    private readonly WeakReference<LightboxHandler> _handler;
    private readonly RecyclerView _pager;
    private readonly LinearLayoutManager _layout;
    private readonly PageAdapter _adapter;
    private readonly TextView _caption;
    private readonly float _density;
    private readonly int _gap;
    private readonly int _touchSlop;

    private IReadOnlyList<LightboxImage> _items = [];
    private int _position;

    public LightboxView(Context context, LightboxHandler handler) : base(context)
    {
        _handler = new(handler);
        _density = context.Resources?.DisplayMetrics?.Density ?? 1;
        _gap = (int)(Gap * _density);
        _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 16;
        SetClipChildren(true);

        _layout = new LinearLayoutManager(context, LinearLayoutManager.Horizontal, false);
        _adapter = new PageAdapter(this);
        _pager = new RecyclerView(context);
        _pager.SetLayoutManager(_layout);
        _pager.SetAdapter(_adapter);
        _pager.SetItemAnimator(null);
        new PagerSnapHelper().AttachToRecyclerView(_pager);
        _pager.AddOnScrollListener(new ScrollListener(this));
        AddView(_pager);

        _caption = new TextView(context)
        {
            Gravity = GravityFlags.Center,
            TextSize = 15,
        };
        _caption.SetTextColor(Android.Graphics.Color.White);
        _caption.SetMaxLines(3);
        _caption.SetShadowLayer(4 * _density, 0, 0, Android.Graphics.Color.Argb(150, 0, 0, 0));
        AddView(_caption);

        handler.VirtualView.FadeChrome = opacity => _caption.Alpha = (float)opacity;
        handler.VirtualView.BottomInsetChanged += RequestLayout;
    }

    private Lightbox? VirtualView => _handler.TryGetTarget(out var handler) ? handler.VirtualView : null;

    private IMauiContext? MauiContext => _handler.TryGetTarget(out var handler) ? handler.MauiContext : null;

    private ZoomImageView? CurrentPage => (_layout.FindViewByPosition(_position) as PageFrame)?.Image;

    /// <summary>The picture of the image showing, once it has arrived.</summary>
    public Bitmap? CurrentBitmap => (CurrentPage?.Drawable as BitmapDrawable)?.Bitmap;

    /// <summary>Where the image showing is drawn, in <paramref name="ancestor"/>'s pixels, at its current zoom.</summary>
    public RectF? CurrentImageRectIn(AView ancestor)
    {
        if (CurrentPage is not { Drawable: not null } page || page.DisplayRect() is not { } rect)
            return null;

        var at = new int[2];
        var origin = new int[2];
        page.GetLocationOnScreen(at);
        ancestor.GetLocationOnScreen(origin);
        rect.Offset(at[0] - origin[0], at[1] - origin[1]);
        return rect;
    }

    public void SetItems(IReadOnlyList<LightboxImage> items)
    {
        _items = items;
        _position = Math.Clamp(_position, 0, Math.Max(0, items.Count - 1));
        _adapter.NotifyDataSetChanged();
        _layout.ScrollToPosition(_position);
        UpdateCaption();
    }

    public void Show(int position)
    {
        position = Math.Clamp(position, 0, Math.Max(0, _items.Count - 1));
        if (position == _position)
            return;

        _position = position;
        _layout.ScrollToPosition(position);
        UpdateCaption();
        ReportCurrent();
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        var width = right - left;
        var height = bottom - top;

        // Half a gap past each edge, so a page at rest fills the view and the gap shows only between pages.
        _pager.Measure(MeasureSpec.MakeMeasureSpec(width + _gap, MeasureSpecMode.Exactly), MeasureSpec.MakeMeasureSpec(height, MeasureSpecMode.Exactly));
        _pager.Layout(-_gap / 2, 0, width + _gap / 2, height);

        var margin = (int)(HeaderBarConstants.PageMargin * _density);
        var inset = (int)((VirtualView?.BottomInset ?? 0) * _density);
        _caption.Measure(MeasureSpec.MakeMeasureSpec(width - 2 * margin, MeasureSpecMode.Exactly), MeasureSpec.MakeMeasureSpec(height, MeasureSpecMode.AtMost));
        var captionBottom = height - inset - margin;
        _caption.Layout(margin, captionBottom - _caption.MeasuredHeight, width - margin, captionBottom);
    }

    private void UpdateCaption() =>
        _caption.Text = _position < _items.Count ? _items[_position].Caption : null;

    private void OnPaged()
    {
        if (_pager.Width <= 0 || _items.Count == 0)
            return;

        var position = Math.Clamp((int)Math.Round(_pager.ComputeHorizontalScrollOffset() / (double)_pager.Width), 0, _items.Count - 1);
        if (position == _position)
            return;

        _position = position;
        UpdateCaption();
        ReportCurrent();

        if (VirtualView is { } view)
            view.Position = position;
    }

    private void OnSettled()
    {
        // A page scrolled away is back at its full view when it returns, as in Photos.
        for (var i = 0; i < _layout.ChildCount; i++)
        {
            if (_layout.GetChildAt(i) is PageFrame frame && _layout.GetPosition(frame) != _position)
                frame.Image.ZoomToFit(animate: false);
        }
    }

    private void ReportCurrent()
    {
        if (VirtualView is not { } view)
            return;

        if (_layout.FindViewByPosition(_position) is PageFrame { IsLoading: true })
            view.OnCurrentImageLoading();
        else
            view.OnCurrentImageReady();
    }

    private async void Load(PageFrame frame, int index)
    {
        var token = frame.Begin();
        var source = _items[index].Source;

        Drawable? drawable = null;
        try
        {
            if (MauiContext is { } context && source is IImageSource imageSource)
                drawable = (await imageSource.GetPlatformImageAsync(context))?.Value;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[Spine] Lightbox: image {index} did not load: {exception.Message}");
        }

        if (!frame.Show(token, drawable))
            return;

        if (drawable is null)
            Console.WriteLine($"[Spine] Lightbox: image {index} ({source}) has no picture.");

        if (index == _position)
            ReportCurrent();
    }

    // The drag down to close: where it began, whether it has been taken, and how fast it goes.
    private float _downX, _downY;
    private Microsoft.Maui.Graphics.Point _start;
    private bool _dismissing;
    private VelocityTracker? _velocity;

    public override bool OnInterceptTouchEvent(MotionEvent? e)
    {
        if (e is null)
            return false;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = e.RawX;
                _downY = e.RawY;
                _dismissing = false;
                break;

            case MotionEventActions.Move when !_dismissing && e.PointerCount == 1:
            {
                var dx = e.RawX - _downX;
                var dy = e.RawY - _downY;

                // Down, more than sideways, on an image at its full view: a pinch, a pan of a
                // zoomed image and a swipe to the next image are not the drag.
                if (dy > _touchSlop && dy > Math.Abs(dx) && CurrentPage is not { IsZoomedIn: true } && VirtualView?.CanDismissByDrag == true)
                {
                    _dismissing = true;

                    // It starts where it was taken, so the image does not jump by the slop.
                    _downX = e.RawX;
                    _downY = e.RawY;
                    _velocity = VelocityTracker.Obtain();
                    _velocity?.AddMovement(e);
                    _start = new Microsoft.Maui.Graphics.Point(e.GetX() / _density, e.GetY() / _density);
                    VirtualView?.OnDismissDrag(GestureStatus.Started, _start, 0, 0, 0);
                    return true;
                }

                break;
            }
        }

        return false;
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null || !_dismissing)
            return base.OnTouchEvent(e);

        _velocity?.AddMovement(e);
        var x = (e.RawX - _downX) / _density;
        var y = (e.RawY - _downY) / _density;

        switch (e.ActionMasked)
        {
            case MotionEventActions.Move:
                VirtualView?.OnDismissDrag(GestureStatus.Running, _start, x, y, 0);
                break;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _velocity?.ComputeCurrentVelocity(1000);
                var velocityY = (_velocity?.YVelocity ?? 0) / _density;
                _velocity?.Recycle();
                _velocity = null;
                _dismissing = false;

                var status = e.ActionMasked == MotionEventActions.Up ? GestureStatus.Completed : GestureStatus.Canceled;
                VirtualView?.OnDismissDrag(status, _start, x, y, velocityY);
                break;
        }

        return true;
    }

    private sealed class ScrollListener(LightboxView owner) : RecyclerView.OnScrollListener
    {
        public override void OnScrolled(RecyclerView recyclerView, int dx, int dy) => owner.OnPaged();

        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState)
        {
            if (newState == RecyclerView.ScrollStateIdle)
                owner.OnSettled();
        }
    }

    private sealed class PageAdapter(LightboxView owner) : RecyclerView.Adapter
    {
        public override int ItemCount => owner._items.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var frame = new PageFrame(parent.Context!, owner)
            {
                LayoutParameters = new RecyclerView.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent),
            };
            frame.SetPadding(owner._gap / 2, 0, owner._gap / 2, 0);
            return new Holder(frame);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var frame = (PageFrame)holder.ItemView;
            frame.Image.ZoomToFit(animate: false);
            owner.Load(frame, position);
        }

        private sealed class Holder(AView view) : RecyclerView.ViewHolder(view);
    }

    /// <summary>One page: the image, inset by half the gap on either side.</summary>
    private sealed class PageFrame : FrameLayout
    {
        private int _token;

        public PageFrame(Context context, LightboxView owner) : base(context)
        {
            Image = new ZoomImageView(context) { Tapped = () => owner.VirtualView?.OnTapped() };
            AddView(Image, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        }

        public ZoomImageView Image { get; }

        public bool IsLoading { get; private set; }

        public int Begin()
        {
            _token++;
            IsLoading = true;
            Image.SetImageDrawable(null);
            return _token;
        }

        public bool Show(int token, Drawable? drawable)
        {
            if (token != _token)
                return false;

            IsLoading = false;
            Image.SetImageDrawable(drawable);
            Image.ZoomToFit(animate: false);
            return true;
        }
    }
}

/// <summary>
/// An image fitted to the view and centred at its smallest zoom: a pinch zooms it about the
/// fingers, a double tap zooms in about the tap or back out, a drag pans a zoomed image and a fling
/// carries it on. A pan that reaches the image's edge lets the pager take the touch.
/// </summary>
internal sealed class ZoomImageView : ImageView
{
    /// <summary>How far a double tap zooms in, and the most a pinch may, beyond the fit.</summary>
    private const float DoubleTapZoom = 2.5f, MaxZoom = 4f;

    // The fit, and the zoom and pan on top of it.
    private readonly Matrix _fit = new();
    private readonly Matrix _zoom = new();
    private readonly Matrix _draw = new();
    private readonly float[] _values = new float[9];
    private readonly ScaleGestureDetector _scale;
    private readonly GestureDetector _gestures;
    private readonly OverScroller _scroller;
    private ValueAnimator? _animation;

    public ZoomImageView(Context context) : base(context)
    {
        SetScaleType(ScaleType.Matrix);
        _scale = new ScaleGestureDetector(context, new ScaleListener(this));
        _gestures = new GestureDetector(context, new GestureListener(this));
        _scroller = new OverScroller(context);
    }

    /// <summary>A single tap, once it is clear it is not the first of a double tap.</summary>
    public Action? Tapped { get; init; }

    private float Zoom
    {
        get
        {
            _zoom.GetValues(_values);
            return _values[Matrix.MscaleX];
        }
    }

    public bool IsZoomedIn => Zoom > 1.01f;

    /// <summary>Where the image is drawn in this view's pixels, or <see langword="null"/> without a picture.</summary>
    public RectF? DisplayRect()
    {
        if (Drawable is not { } drawable)
            return null;

        var rect = new RectF(0, 0, drawable.IntrinsicWidth, drawable.IntrinsicHeight);
        DrawMatrix().MapRect(rect);
        return rect;
    }

    private Matrix DrawMatrix()
    {
        _draw.Set(_fit);
        _draw.PostConcat(_zoom);
        return _draw;
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        ZoomToFit(animate: false);
    }

    /// <summary>Fits the image to the view, at its smallest zoom.</summary>
    public void ZoomToFit(bool animate)
    {
        _animation?.Cancel();
        _scroller.ForceFinished(true);

        if (Drawable is { IntrinsicWidth: > 0, IntrinsicHeight: > 0 } drawable && Width > 0 && Height > 0)
        {
            var src = new RectF(0, 0, drawable.IntrinsicWidth, drawable.IntrinsicHeight);
            var dst = new RectF(PaddingLeft, PaddingTop, Width - PaddingRight, Height - PaddingBottom);
            _fit.SetRectToRect(src, dst, Matrix.ScaleToFit.Center);
        }

        if (animate && IsZoomedIn)
        {
            AnimateZoom(1, Width / 2f, Height / 2f);
            return;
        }

        _zoom.Reset();
        ImageMatrix = DrawMatrix();
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null || Drawable is null)
            return base.OnTouchEvent(e);

        if (e.ActionMasked == MotionEventActions.Down)
        {
            _scroller.ForceFinished(true);

            // A zoomed image keeps the touch from the pager until a pan reaches its edge.
            Parent?.RequestDisallowInterceptTouchEvent(IsZoomedIn);
        }

        _scale.OnTouchEvent(e);
        _gestures.OnTouchEvent(e);
        return true;
    }

    private void ScaleBy(float factor, float focusX, float focusY)
    {
        var zoom = Zoom;
        factor = Math.Clamp(zoom * factor, 1f, MaxZoom) / zoom;
        _zoom.PostScale(factor, factor, focusX, focusY);
        Settle();
    }

    /// <summary>Pans by a distance, as far as the image's edges allow; whether it went all the way.</summary>
    private bool PanBy(float dx, float dy)
    {
        _zoom.PostTranslate(dx, dy);
        var (adjustX, _) = Settle();
        return Math.Abs(adjustX) < 0.5f;
    }

    /// <summary>
    /// Keeps the image over the view: centred along an axis where it is smaller, against the edges
    /// where it is larger. Returns how far it had to move it.
    /// </summary>
    private (float X, float Y) Settle()
    {
        var rect = DisplayRect() ?? new RectF();
        float dx = 0, dy = 0;

        if (rect.Width() <= Width)
            dx = (Width - rect.Width()) / 2 - rect.Left;
        else if (rect.Left > 0)
            dx = -rect.Left;
        else if (rect.Right < Width)
            dx = Width - rect.Right;

        if (rect.Height() <= Height)
            dy = (Height - rect.Height()) / 2 - rect.Top;
        else if (rect.Top > 0)
            dy = -rect.Top;
        else if (rect.Bottom < Height)
            dy = Height - rect.Bottom;

        _zoom.PostTranslate(dx, dy);
        ImageMatrix = DrawMatrix();
        return (dx, dy);
    }

    private void AnimateZoom(float target, float focusX, float focusY)
    {
        _animation?.Cancel();
        var from = Zoom;
        _animation = ValueAnimator.OfFloat(0, 1);
        _animation!.SetDuration(250);
        _animation.SetInterpolator(new Android.Views.Animations.DecelerateInterpolator());
        _animation.Update += (_, args) =>
        {
            var progress = (float)args.Animation.AnimatedValue!;
            var zoom = from + (target - from) * progress;
            ScaleBy(zoom / Zoom, focusX, focusY);
        };
        _animation.Start();
    }

    private void Fling(float velocityX, float velocityY)
    {
        if (DisplayRect() is not { } rect)
            return;

        var startX = (int)Math.Round(-rect.Left);
        var startY = (int)Math.Round(-rect.Top);
        var maxX = (int)Math.Max(0, rect.Width() - Width);
        var maxY = (int)Math.Max(0, rect.Height() - Height);

        _scroller.Fling(startX, startY, (int)-velocityX, (int)-velocityY, 0, maxX, 0, maxY);
        var lastX = startX;
        var lastY = startY;

        void Step()
        {
            if (!_scroller.ComputeScrollOffset())
                return;

            PanBy(lastX - _scroller.CurrX, lastY - _scroller.CurrY);
            lastX = _scroller.CurrX;
            lastY = _scroller.CurrY;
            PostOnAnimation(new Java.Lang.Runnable(Step));
        }

        PostOnAnimation(new Java.Lang.Runnable(Step));
    }

    private sealed class ScaleListener(ZoomImageView owner) : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        public override bool OnScale(ScaleGestureDetector detector)
        {
            owner.ScaleBy(detector.ScaleFactor, detector.FocusX, detector.FocusY);
            owner.Parent?.RequestDisallowInterceptTouchEvent(true);
            return true;
        }
    }

    private sealed class GestureListener(ZoomImageView owner) : GestureDetector.SimpleOnGestureListener
    {
        public override bool OnDown(MotionEvent e) => true;

        public override bool OnSingleTapConfirmed(MotionEvent e)
        {
            owner.Tapped?.Invoke();
            return true;
        }

        public override bool OnDoubleTap(MotionEvent e)
        {
            if (owner.IsZoomedIn)
                owner.AnimateZoom(1, owner.Width / 2f, owner.Height / 2f);
            else
                owner.AnimateZoom(DoubleTapZoom, e.GetX(), e.GetY());

            return true;
        }

        public override bool OnScroll(MotionEvent? e1, MotionEvent e2, float distanceX, float distanceY)
        {
            if (!owner.IsZoomedIn || owner._scale.IsInProgress)
                return false;

            // At the image's edge, a pan on in the same direction is the pager's.
            if (!owner.PanBy(-distanceX, -distanceY))
                owner.Parent?.RequestDisallowInterceptTouchEvent(false);

            return true;
        }

        public override bool OnFling(MotionEvent? e1, MotionEvent e2, float velocityX, float velocityY)
        {
            if (owner.IsZoomedIn)
                owner.Fling(velocityX, velocityY);

            return true;
        }
    }
}
#endif
