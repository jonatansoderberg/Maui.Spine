namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Implement this interface to own the lifecycle of Spine shortcuts:
/// declare them at startup and handle invocations at runtime.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Configure"/> is a <c>static abstract</c> member so that Spine can call it
/// during app startup — before the DI container is fully built — without needing an instance.
/// Place only static, configuration-time logic here (ids, titles, tray visibility).
/// </para>
/// <para>
/// <see cref="InvokeAsync"/> is called at runtime through the DI container, so constructor
/// injection (e.g. <c>INavigationService</c>) is available as usual.
/// </para>
/// </remarks>
public interface IShortcutHandler
{
    /// <summary>
    /// Declare the shortcuts that Spine should register with the platform.
    /// Called once at startup before the DI container is built.
    /// </summary>
    static abstract void Configure(IShortcutBuilder builder);

    /// <summary>
    /// Invoked when the user activates a shortcut identified by <paramref name="shortcutId"/>.
    /// </summary>
    Task InvokeAsync(string shortcutId);
}
