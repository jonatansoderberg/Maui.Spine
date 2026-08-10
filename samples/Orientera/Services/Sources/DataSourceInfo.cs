namespace Orientera.Services.Sources;

/// <summary>
/// Which data source the app was started against. On screen, so a demo run is never mistaken
/// for a live one — the two look alike and behave differently.
/// </summary>
public sealed record DataSourceInfo(string? BackendAddress)
{
    public bool IsLive => !string.IsNullOrWhiteSpace(BackendAddress);

    public string Description => IsLive
        ? $"Kör mot backend ({BackendAddress}). Tidsmaskinen flyttar \"nu\", vilket bara påverkar appens vy av tiden."
        : "Kör på fake-data — demo- och testläget. Tidsmaskinen flyttar \"nu\" genom tävlingsresan; designsystemet visar tokens och komponenter.";
}
