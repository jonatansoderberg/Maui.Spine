using CommunityToolkit.Mvvm.Messaging;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Sheets;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Shared bottom-sheet presentation logic for both hosts: registers the
/// <see cref="ShowBottomSheetMessage"/> handler, tracks whether a sheet is active, and presents
/// the sheet region natively over the host page. Only the host currently installed in
/// <see cref="SpineHostProvider"/> responds, so a swapped-out host never presents.
/// </summary>
internal sealed class BottomSheetCoordinator : IDisposable
{
    private readonly ISpineHost _host;
    private readonly NavigationRegion _sheetRegion;
    private readonly SpineHostProvider _hostProvider;

    /// <summary>Whether a bottom sheet is currently presented.</summary>
    public bool IsSheetActive { get; private set; }

    private TaskCompletionSource? _closed;

    /// <summary>Completes once no sheet is presented: at once when none is, else when the open one has gone.</summary>
    public Task WhenClosed => _closed?.Task ?? Task.CompletedTask;

    /// <summary>Backdrop material applied to the sheet surface on Windows.</summary>
    public WindowBackdrop SheetBackdrop { get; set; }

    public BottomSheetCoordinator(ISpineHost host, NavigationRegion sheetRegion, SpineHostProvider hostProvider)
    {
        _host = host;
        _sheetRegion = sheetRegion;
        _hostProvider = hostProvider;

        WeakReferenceMessenger.Default.Register(
            this,
            (MessageHandler<object, ShowBottomSheetMessage>)(async (recipient, message) =>
            {
                if (!ReferenceEquals(_hostProvider.Current, _host))
                    return;

                IsSheetActive = true;
                var closed = _closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                if (_sheetRegion.BindingContext is not NavigationRegionViewModel vm)
                    return;

                if (message.Content is null)
                    return;

                await vm.ResetAsync(message.Content);

#if WINDOWS || ANDROID || IOS || MACCATALYST
                var bottomSheetTask = _host.HostPage.DisplayBottomSheet(
                    () => _sheetRegion,
                    (b) =>
                    {
                        foreach (var detent in message.AllowedDetents)
                            b.AddDetent(detent);
                        b.SetSelectedDetent(message.SelectedDetent);
                        b.SetBackgroundPageOverlay(message.BackgroundPageOverlay);
                        b.SetSheetBackdrop(SheetBackdrop);
                    });

                message.Reply(bottomSheetTask);

                await bottomSheetTask;
                IsSheetActive = false;

                // The keyboard went with the sheet; its page's search ends with it too.
                if (vm.CurrentRegionViewModel?.Search is { IsActive: true } search)
                    MainThread.BeginInvokeOnMainThread(() => search.IsActive = false);

                closed.TrySetResult();
#else
                await Task.CompletedTask;
#endif
            }));
    }

    /// <inheritdoc/>
    public void Dispose() => WeakReferenceMessenger.Default.UnregisterAll(this);
}
