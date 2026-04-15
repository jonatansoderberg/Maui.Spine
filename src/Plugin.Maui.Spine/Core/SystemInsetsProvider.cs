#if !ANDROID

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Default <see cref="ISystemInsetsProvider"/> for platforms that do not require
/// manual system bar inset management (iOS, macOS, Windows).
/// Always returns <see cref="Thickness.Zero"/>.
/// </summary>
internal sealed class SystemInsetsProvider : ISystemInsetsProvider
{
    /// <inheritdoc/>
    public Thickness SystemBarInsets => Thickness.Zero;

    /// <inheritdoc/>
    public event Action? InsetsChanged { add { } remove { } }
}

#endif
