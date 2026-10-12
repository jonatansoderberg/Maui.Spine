using System.Reflection;
using AsyncAwaitBestPractices;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary>
/// The app lock and the privacy shield. Covers the app with <see cref="LockScreen"/> at launch and when it
/// comes back after <see cref="AppLockOptions.LockAfter"/>, keeps <see cref="PrivacyShieldAttribute"/> pages
/// out of capture and the app switcher, and forgets the unlock when the window stops.
/// </summary>
/// <remarks>
/// It starts on the first <see cref="SpineScreen.ShownPagesChanged"/>, when the window exists and its first
/// page goes in: the launch cover goes up in the same turn, so the page never shows uncovered.
/// </remarks>
internal sealed partial class AppLockService(UnlockService unlock, SpineBiometricsOptions options, TimeProvider time) : IMauiInitializeService
{
    private readonly AppLockClock _clock = new(options, time);
    private readonly Dictionary<Type, (PrivacyShieldAttribute? Shield, RequiresUnlockAttribute? Unlock)> _attributes = [];
    private Window? _window;
    private bool _active;
    private bool _askOnActivate;
    private bool _asking;
    private IDisposable? _cover;
    private string _reason = "";
    private TaskCompletionSource? _uncovered;

    public void Initialize(IServiceProvider services)
    {
        SpineScreen.ShownPagesChanged += OnShownPagesChanged;
        unlock.Unlocked += (_, _) => _clock.Unlocked();
    }

    /// <summary>
    /// Covers the app until the user unlocks, for a guarded root page that has nothing to fall back to.
    /// <see langword="false"/> when <paramref name="cancellationToken"/> ends the wait first.
    /// </summary>
    public async Task<bool> HoldAsync(string reason, CancellationToken cancellationToken)
    {
        var ownCover = _cover is null;
        Cover(reason, back: null);

        var cancelled = new TaskCompletionSource();
        using (cancellationToken.Register(() => cancelled.TrySetResult()))
        {
            if (await Task.WhenAny(_uncovered!.Task, cancelled.Task) == _uncovered.Task)
                return true;
        }

        if (ownCover)
            Uncover();

        return false;
    }

    private void OnShownPagesChanged(object? sender, EventArgs e)
    {
        var pages = SpineScreen.ShownPages;
        SpineScreen.ExcludeFromCapture = pages.Any(p => AttributesOf(p.Page).Shield is not null);
        UpdateCaptureCover(pages.Any(p => AttributesOf(p.Page).Shield is { CoverWhileCaptured: true }));

        // On a cold start the first page goes in inside CreateWindow, before the app lists the window.
        if (_window is null && (pages.Select(p => p.Page.Window).FirstOrDefault(w => w is not null) ?? Application.Current?.Windows.FirstOrDefault()) is { } window)
            Start(window);
    }

    private void Start(Window window)
    {
        _window = window;
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;
        window.Stopped += OnStopped;
        window.Resumed += OnResumed;

        if (window.Handler is not null)
        {
            Launch();
            return;
        }

        // ...and before the window has its platform window, which the cover needs.
        void OnHandlerChanged(object? sender, EventArgs e)
        {
            window.HandlerChanged -= OnHandlerChanged;
            Launch();
        }

        window.HandlerChanged += OnHandlerChanged;
    }

    private void Launch()
    {
        WatchMinimised(_window!);
        UpdateRecents(_clock.IsAppLockOn);

        if (!_clock.LocksOnLaunch)
            return;

        Cover(AppReason(), back: null);
        AskWhenReadyAsync().SafeFireAndForget(Log);
    }

    private async Task AskWhenReadyAsync()
    {
        await SpineScreen.WhenReady;
        await AskAsync();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        _active = true;
        HideShield();

        if (_askOnActivate)
        {
            _askOnActivate = false;
            AskAsync().SafeFireAndForget(Log);
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        _active = false;
        UpdateRecents(_clock.IsAppLockOn);

        // The Face ID sheet makes the app inactive too; covering the app behind it would only flash.
        if (_cover is null && !unlock.IsPrompting && (_clock.IsAppLockOn || SpineScreen.ShownPages.Any(p => AttributesOf(p.Page).Shield is not null)))
            ShowShield();
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        unlock.Lock();
        _clock.Stopped();
    }

    private void OnResumed(object? sender, EventArgs e)
    {
        var relock = RelockPage();

        // Android's device-credential screen is an activity of its own: the app stops and comes back for it.
        if (_clock.Resumed(relock is not null) && _cover is null && !unlock.IsPrompting)
        {
            if (_clock.IsAppLockOn)
                Cover(AppReason(), back: null);
            else if (relock is { } page)
                Cover(BiometricsStrings.Reason(page.Unlock.Reason), page.Shown.CanGoBack ? () => GoBackAsync(page.Shown.Page).SafeFireAndForget(Log) : null);
        }

        if (_cover is null)
            return;

        // Windows may activate the window before it reports it resumed.
        if (_active)
            AskAsync().SafeFireAndForget(Log);
        else
            _askOnActivate = true;
    }

    private void Cover(string reason, Action? back)
    {
        if (_cover is not null)
            return;

        _reason = reason;
        _uncovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _cover = SpineOverlay.Show(new LockScreen(reason, () => AskAsync().SafeFireAndForget(Log), back), back ?? LeaveApp);
    }

    /// <summary>Android's back on the app's lock screen: to the background, as on the system's own lock screen.</summary>
    private static void LeaveApp()
    {
#if ANDROID
        Platform.CurrentActivity?.MoveTaskToBack(true);
#endif
    }

    private void Uncover()
    {
        _cover?.Dispose();
        _cover = null;
        _uncovered?.TrySetResult();
    }

    /// <summary>Asks once; a refusal leaves the cover with its Unlock button.</summary>
    private async Task AskAsync()
    {
        if (_asking || _cover is null)
            return;

        _asking = true;
        try
        {
            if (await unlock.RequireAsync(_reason))
                Uncover();
        }
        finally
        {
            _asking = false;
        }
    }

    /// <summary>Leaves a relocked page without unlocking it; the cover stays until the page has gone.</summary>
    private async Task GoBackAsync(View page)
    {
        await IPlatformApplication.Current!.Services.GetRequiredService<INavigationService>().BackAsync();

        if (!SpineScreen.ShownPages.Any(p => ReferenceEquals(p.Page, page)))
            Uncover();
    }

    private (ShownPage Shown, RequiresUnlockAttribute Unlock)? RelockPage() =>
        SpineScreen.ShownPages
            .Select(p => (Shown: p, AttributesOf(p.Page).Unlock))
            .LastOrDefault(p => p.Unlock is { Relock: true }) is { Unlock: { } attribute } found
            ? (found.Shown, attribute)
            : null;

    private (PrivacyShieldAttribute? Shield, RequiresUnlockAttribute? Unlock) AttributesOf(View page)
    {
        var type = page.GetType();
        if (!_attributes.TryGetValue(type, out var attributes))
        {
            attributes = (type.GetCustomAttribute<PrivacyShieldAttribute>(), type.GetCustomAttributes<RequiresUnlockAttribute>().FirstOrDefault());
            _attributes[type] = attributes;
        }

        return attributes;
    }

    private string AppReason() => BiometricsStrings.Reason(options.Lock.Reason ?? "Spine.Biometrics.AppLock.Reason");

    private static void Log(Exception e) => System.Diagnostics.Debug.WriteLine($"[Spine] App lock: {e}");

    /// <summary>iOS: covers the window before the app-switcher snapshot.</summary>
    partial void ShowShield();

    partial void HideShield();

    /// <summary>iOS 17 and later: covers the window while the screen is captured and <paramref name="wanted"/>.</summary>
    partial void UpdateCaptureCover(bool wanted);

    /// <summary>Android 13 and later: keeps the app out of the app switcher's thumbnails while the app lock is on.</summary>
    partial void UpdateRecents(bool hide);

    /// <summary>
    /// The desktop: treats a minimised (or hidden) window as stopped and its return as resumed, which MAUI
    /// does not report on Mac Catalyst. A second report from MAUI is harmless.
    /// </summary>
    partial void WatchMinimised(Window window);
}
