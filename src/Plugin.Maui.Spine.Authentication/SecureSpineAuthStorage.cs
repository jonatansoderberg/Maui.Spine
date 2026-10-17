namespace Plugin.Maui.Spine.Authentication;

/// <summary>The session in the Keychain (Apple), the Keystore-backed preferences (Android) or the data protection API (Windows).</summary>
internal sealed class SecureSpineAuthStorage(string key) : ISpineAuthStorage
{
    public Task<string?> LoadAsync() => SecureStorage.Default.GetAsync(key);

    public Task SaveAsync(string value) => SecureStorage.Default.SetAsync(key, value);

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}
