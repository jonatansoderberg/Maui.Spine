using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>An image a <see cref="Lightbox"/> shows.</summary>
/// <param name="Source">The full-size image.</param>
/// <param name="Tag">
/// The <see cref="Transition.TagProperty"/> of its thumbnail, so the lightbox grows out of the
/// thumbnail and shrinks back into it; <see langword="null"/> to fade in and out.
/// </param>
/// <param name="Caption">Text shown under the image, or <see langword="null"/>.</param>
public sealed record LightboxImage(ImageSource Source, string? Tag = null, string? Caption = null);

/// <summary>
/// Full-screen images to page through sideways, each zoomed with a pinch or a double tap. Put it on
/// a <see cref="NavigableLightboxAttribute"/> page: the page then grows out of the thumbnail of the
/// image it opens on, shrinks into the thumbnail of the image showing when it closes, and closes
/// when the image is dragged down.
/// </summary>
/// <example>
/// <code>
/// &lt;Lightbox ItemsSource="{Binding Photos}" Position="{Binding Index}" /&gt;
/// </code>
/// </example>
public partial class Lightbox : View, IZoomFocus
{
    /// <summary>Identifies the <see cref="ItemsSource"/> bindable property.</summary>
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IReadOnlyList<LightboxImage>), typeof(Lightbox), null,
        propertyChanged: (bindable, _, _) => ((Lightbox)bindable).OnCurrentChanged());

    /// <summary>Identifies the <see cref="Position"/> bindable property.</summary>
    public static readonly BindableProperty PositionProperty = BindableProperty.Create(
        nameof(Position), typeof(int), typeof(Lightbox), 0, BindingMode.TwoWay,
        propertyChanged: (bindable, _, _) => ((Lightbox)bindable).OnCurrentChanged());

    /// <summary>Identifies the <see cref="IsChromeVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsChromeVisibleProperty = BindableProperty.Create(
        nameof(IsChromeVisible), typeof(bool), typeof(Lightbox), true, BindingMode.TwoWay,
        propertyChanged: (bindable, _, value) => ((Lightbox)bindable).Region()?.ShowChrome((bool)value));

    private static readonly BindablePropertyKey CurrentItemPropertyKey = BindableProperty.CreateReadOnly(
        nameof(CurrentItem), typeof(LightboxImage), typeof(Lightbox), null);

    /// <summary>Identifies the <see cref="CurrentItem"/> bindable property.</summary>
    public static readonly BindableProperty CurrentItemProperty = CurrentItemPropertyKey.BindableProperty;

    /// <summary>The images, in the order they are paged through.</summary>
    public IReadOnlyList<LightboxImage>? ItemsSource
    {
        get => (IReadOnlyList<LightboxImage>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>The index of the image showing; set it to open on another image. Two-way.</summary>
    public int Position
    {
        get => (int)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>The image showing, or <see langword="null"/> when there is none.</summary>
    public LightboxImage? CurrentItem => (LightboxImage?)GetValue(CurrentItemProperty);

    /// <summary>
    /// Whether the header bar, the title, the caption and the status bar show over the image. A tap
    /// on the image hides them and the next brings them back; bind to it to fade something of the
    /// page's own with them. Two-way.
    /// </summary>
    public bool IsChromeVisible
    {
        get => (bool)GetValue(IsChromeVisibleProperty);
        set => SetValue(IsChromeVisibleProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnParentSet()
    {
        base.OnParentSet();
        OnCurrentChanged();
    }

    /// <remarks>
    /// The lightbox page and the lightbox itself carry the tag of the image showing: the page so
    /// that it zooms, the lightbox so that it is the page's focus, which it hands on as the image.
    /// </remarks>
    private void OnCurrentChanged()
    {
        var items = ItemsSource;
        var current = items is { Count: > 0 } && Position >= 0 && Position < items.Count ? items[Position] : null;
        SetValue(CurrentItemPropertyKey, current);

        Transition.SetTag(this, current?.Tag);

        if (Page() is { } page)
            Transition.SetTag(page, current?.Tag);
    }

    /// <summary>The <see cref="NavigableLightboxAttribute"/> page this lightbox is on, if it is on one.</summary>
    private Element? Page()
    {
        for (var element = Parent; element is not null; element = element.Parent)
        {
            if (element is INavigable)
                return element.GetType().IsDefined(typeof(NavigableLightboxAttribute), inherit: false) ? element : null;
        }

        return null;
    }

    internal NavigationRegion? Region()
    {
        for (var element = Parent; element is not null; element = element.Parent)
        {
            if (element is NavigationRegion region)
                return region;
        }

        return null;
    }

    /// <summary>Whether a drag down may close the page: it is a lightbox page with a page under it.</summary>
    internal bool CanDismissByDrag => Page() is not null && Region() is { CanDismissByDrag: true };

    /// <summary>
    /// The drag down to close, as the platform view sees it, with <paramref name="start"/> where
    /// the finger first came down on the lightbox; see <see cref="NavigationRegion.OnDismissDrag"/>.
    /// </summary>
    internal void OnDismissDrag(GestureStatus status, Point start, double x, double y, double velocityY) =>
        Region()?.OnDismissDrag(status, start, x, y, velocityY);

    /// <summary>Called by the platform view on a tap that is not a double tap.</summary>
    internal void OnTapped() => IsChromeVisible = !IsChromeVisible;

    /// <summary>
    /// Fades what the platform view draws over the image (the caption) to <paramref name="opacity"/>,
    /// with the header bar and the title. Set by the platform view.
    /// </summary>
    internal Action<double>? FadeChrome { get; set; }

    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Called by the platform view once the image showing has its picture and its place, or has none to wait for.</summary>
    internal void OnCurrentImageReady() => _ready.TrySetResult();

    /// <summary>Called by the platform view when another image starts loading as the one showing.</summary>
    internal void OnCurrentImageLoading()
    {
        if (_ready.Task.IsCompleted)
            _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    Task IZoomFocus.WhenReadyAsync() => _ready.Task;

    /// <summary>
    /// How far up from the bottom of the lightbox its chrome starts: the system's bottom inset and
    /// the toolbar above it. The caption sits above that. Set by the region, which owns both: the
    /// platform view's own safe area is zero under Spine.
    /// </summary>
    internal double BottomInset
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            BottomInsetChanged?.Invoke();
        }
    }

    internal event Action? BottomInsetChanged;

    /// <summary>Shares the image showing through the system's share sheet, anchored to <paramref name="anchor"/> where the sheet is a popover.</summary>
    internal async Task ShareAsync(View? anchor)
    {
        if (await WriteCurrentImageAsync() is not { } path)
        {
            Console.WriteLine($"[Spine] Lightbox: image {Position} has no picture to share yet.");
            return;
        }

        await ShareFileAsync(path, anchor is null ? Rect.Zero : BoundsInWindow(anchor));
    }

    /// <summary>Opens the share sheet for <paramref name="path"/>, the image showing written to a file.</summary>
    private partial Task ShareFileAsync(string path, Rect anchor);

    /// <summary>Saves the image showing to the photo library, and says how it went: a haptic on success, an alert otherwise.</summary>
    /// <returns>Whether the image was saved.</returns>
    internal async Task<bool> SaveAsync()
    {
        var result = await SaveCurrentImageAsync();
        if (result is SaveResult.Saved)
        {
            Haptics.Success();
            return true;
        }

        Haptics.Error();
        Console.WriteLine($"[Spine] Lightbox: image {Position} was not saved to the photo library: {result}.");

        var (title, message) = result is SaveResult.Denied
            ? (SpineStrings.Current["Spine.Lightbox.SaveDenied.Title"], SpineStrings.Current["Spine.Lightbox.SaveDenied.Message"])
            : (SpineStrings.Current["Spine.Lightbox.SaveFailed.Title"], string.Empty);

        if (Window?.Page is { } page)
            await page.DisplayAlertAsync(title, message, "OK");

        return false;
    }

    private static Rect BoundsInWindow(View view)
    {
        var (x, y) = (view.X, view.Y);
        for (var parent = view.Parent as VisualElement; parent is not null; parent = parent.Parent as VisualElement)
        {
            x += parent.X + parent.TranslationX;
            y += parent.Y + parent.TranslationY;
        }

        return new Rect(x, y, view.Width, view.Height);
    }

    internal enum SaveResult { Saved, Denied, NoPicture, Failed }

    /// <summary>
    /// Whether the app can save to the photo library: on iOS and Mac Catalyst only when its
    /// Info.plist says why (<c>NSPhotoLibraryAddUsageDescription</c>), or the system would end it.
    /// </summary>
    internal static partial bool CanSaveToPhotos { get; }

    /// <summary>Whether the image showing can be written to a file to share: not yet on Windows.</summary>
    internal static bool CanShare =>
#if IOS || MACCATALYST || ANDROID
        true;
#else
        false;
#endif

    /// <summary>Writes the image showing to a file in the cache, or <see langword="null"/> when it has no picture yet.</summary>
    private partial Task<string?> WriteCurrentImageAsync();

    private partial Task<SaveResult> SaveCurrentImageAsync();

    /// <summary>A file name for the image showing: its caption, or its place in the set.</summary>
    private string FileName(string extension)
    {
        var name = CurrentItem?.Caption is { Length: > 0 } caption
            ? string.Concat(caption.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim()
            : $"Image {Position + 1}";

        return $"{(name.Length > 60 ? name[..60] : name)}.{extension}";
    }

#if !IOS && !MACCATALYST
    private partial Task ShareFileAsync(string path, Rect anchor)
    {
        var type = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return Share.Default.RequestAsync(new ShareFileRequest(CurrentItem?.Caption ?? string.Empty, new ShareFile(path, type))
        {
            PresentationSourceBounds = anchor,
        });
    }
#endif

#if !IOS && !MACCATALYST && !ANDROID
    internal static partial bool CanSaveToPhotos => false;
    private partial Task<string?> WriteCurrentImageAsync() => Task.FromResult<string?>(null);
    private partial Task<SaveResult> SaveCurrentImageAsync() => Task.FromResult(SaveResult.Failed);
#endif

#if IOS || MACCATALYST
    UIKit.UIView? IZoomFocus.FocusView => (Handler as LightboxHandler)?.PlatformView.CurrentImageView;
#endif
}
