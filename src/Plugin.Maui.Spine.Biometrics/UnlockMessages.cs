namespace Plugin.Maui.Spine.Biometrics;

/// <summary>The unlock's alerts, on the app's window.</summary>
internal sealed class UnlockMessages : IUnlockMessages
{
    public Task ShowLockedOutAsync() => ShowAsync("Spine.Biometrics.LockedOut.Title", "Spine.Biometrics.LockedOut.Message");

    public Task ShowUnavailableAsync() => ShowAsync("Spine.Biometrics.Unavailable.Title", "Spine.Biometrics.Unavailable.Message");

    private static Task ShowAsync(string title, string message) =>
        MainThread.InvokeOnMainThreadAsync(() => Page()?.DisplayAlertAsync(
            BiometricsStrings.Get(title), BiometricsStrings.Get(message), BiometricsStrings.Get("Spine.Biometrics.Ok")) ?? Task.CompletedTask);

    internal static Page? Page() => Application.Current?.Windows.FirstOrDefault()?.Page;
}
