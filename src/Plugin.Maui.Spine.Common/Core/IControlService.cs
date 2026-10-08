namespace Plugin.Maui.Spine.Common;

/// <summary>Refreshes controls from the app, and on Android asks the user to add one.</summary>
public interface IControlService
{
    /// <summary>
    /// Whether the current platform shows Spine controls: iOS 18 and later, and Android 7 and later, when the
    /// build declared at least one <c>&lt;SpineControl&gt;</c>.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>The control kinds discovered from the Spine assemblies.</summary>
    IReadOnlyList<string> Kinds { get; }

    /// <summary>Asks the provider of <paramref name="kind"/> for its state and shows it.</summary>
    Task RefreshAsync(string kind, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the control provided by <typeparamref name="TProvider"/>.</summary>
    Task RefreshAsync<TProvider>(CancellationToken cancellationToken = default) where TProvider : IControlProvider;

    /// <summary>Refreshes every control. Also runs at launch and when the app moves to the background.</summary>
    Task RefreshAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the system to offer the control to the user: on Android 13 and later a prompt to add the tile to
    /// Quick Settings. Returns <see langword="true"/> when the tile was added or already was there, and
    /// <see langword="false"/> when the user declined or the platform has no such prompt — iOS has none; the
    /// user adds a control from Control Center's edit mode.
    /// </summary>
    Task<bool> RequestAddAsync(string kind);
}
