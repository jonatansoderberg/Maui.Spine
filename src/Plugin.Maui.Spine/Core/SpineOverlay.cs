namespace Plugin.Maui.Spine.Core;

/// <summary>
/// A view over the whole window: above the header bar, the tab bar and an open sheet. A sheet that opens
/// while it shows goes under it. The view gets the app's resources and styles through the window's page.
/// </summary>
/// <example>
/// <code>
/// var overlay = SpineOverlay.Show(new LockView());
/// …
/// overlay.Dispose();
/// </code>
/// </example>
public static partial class SpineOverlay
{
    private static readonly List<Handle> _shown = [];

    /// <summary>
    /// Shows <paramref name="content"/> over the window until the returned object is disposed. Call it on the
    /// main thread once the app's window exists, for example from a page or after <see cref="SpineScreen.WhenReady"/>.
    /// </summary>
    /// <param name="content">The view, laid out to fill the window. Give it a background to hide what is under it.</param>
    /// <param name="back">What Android's back button or gesture does while the view shows; <see langword="null"/> ignores it.</param>
    public static IDisposable Show(View content, Action? back = null)
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        var context = window?.Handler?.MauiContext
            ?? throw new InvalidOperationException("SpineOverlay.Show needs the app's window; call it once the window is created, for example after SpineScreen.WhenReady.");

        window.Page?.AddLogicalChild(content);

        var handle = new Handle(window, content, back);
        handle.Attach(context);
        _shown.Add(handle);
        return handle;
    }

    private sealed partial class Handle(Window window, View content, Action? back) : IDisposable
    {
        private readonly Window _window = window;
        private readonly View _content = content;
        private readonly Action? _back = back;

        public void Dispose()
        {
            if (!_shown.Remove(this))
                return;

            Detach();
            _window.Page?.RemoveLogicalChild(_content);
        }

        public partial void Attach(IMauiContext context);

        private partial void Detach();
    }
}
