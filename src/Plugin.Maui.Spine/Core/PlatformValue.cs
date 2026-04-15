namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Entry point for fluent platform-specific value selection.
/// Start a chain with any <c>For…</c> method, add further platforms, then call
/// <see cref="OsPlatformBuilder{T}.Fallback"/> or <see cref="IdiomPlatformBuilder{T}.Fallback"/>
/// to resolve the value.
/// </summary>
/// <example>
/// <code>
/// // OS-based
/// var margin = PlatformValue.ForAndroid(new Thickness(8)).ForWindows(new Thickness(16)).Fallback(new Thickness(0));
///
/// // Idiom-based
/// var size = PlatformValue.ForPhone(14).ForDesktop(12).Fallback(13);
///
/// // Works identically for enums — no extra overload needed
/// var placement = PlatformValue.ForIOS(LayoutAlignment.Start).Fallback(LayoutAlignment.Center);
/// </code>
/// </example>
public static class PlatformValue
{
    /// <summary>Starts an OS chain with a value for Android.</summary>
    public static OsPlatformBuilder<T> ForAndroid<T>(T value) => new OsPlatformBuilder<T>().ForAndroid(value);

    /// <summary>Starts an OS chain with a value for iOS.</summary>
    public static OsPlatformBuilder<T> ForIOS<T>(T value) => new OsPlatformBuilder<T>().ForIOS(value);

    /// <summary>Starts an OS chain with a value for Windows.</summary>
    public static OsPlatformBuilder<T> ForWindows<T>(T value) => new OsPlatformBuilder<T>().ForWindows(value);

    /// <summary>Starts an OS chain with a value for macOS/Mac Catalyst.</summary>
    public static OsPlatformBuilder<T> ForMacCatalyst<T>(T value) => new OsPlatformBuilder<T>().ForMacCatalyst(value);

    /// <summary>Starts an idiom chain with a value for phones.</summary>
    public static IdiomPlatformBuilder<T> ForPhone<T>(T value) => new IdiomPlatformBuilder<T>().ForPhone(value);

    /// <summary>Starts an idiom chain with a value for tablets.</summary>
    public static IdiomPlatformBuilder<T> ForTablet<T>(T value) => new IdiomPlatformBuilder<T>().ForTablet(value);

    /// <summary>Starts an idiom chain with a value for desktops.</summary>
    public static IdiomPlatformBuilder<T> ForDesktop<T>(T value) => new IdiomPlatformBuilder<T>().ForDesktop(value);
}

/// <summary>
/// Fluent builder that maps operating systems to a value of type <typeparamref name="T"/>.
/// Call <see cref="Fallback"/> to resolve the value for the current OS.
/// </summary>
/// <typeparam name="T">The type to select. Works for reference types, structs, and enums.</typeparam>
public sealed class OsPlatformBuilder<T>
{
    private (bool HasValue, T Value) _android;
    private (bool HasValue, T Value) _ios;
    private (bool HasValue, T Value) _windows;
    private (bool HasValue, T Value) _macCatalyst;

    /// <summary>Registers a value for Android.</summary>
    public OsPlatformBuilder<T> ForAndroid(T value) { _android = (true, value); return this; }

    /// <summary>Registers a value for iOS.</summary>
    public OsPlatformBuilder<T> ForIOS(T value) { _ios = (true, value); return this; }

    /// <summary>Registers a value for Windows.</summary>
    public OsPlatformBuilder<T> ForWindows(T value) { _windows = (true, value); return this; }

    /// <summary>Registers a value for macOS/Mac Catalyst.</summary>
    public OsPlatformBuilder<T> ForMacCatalyst(T value) { _macCatalyst = (true, value); return this; }

    /// <summary>
    /// Resolves and returns the registered value for the current OS,
    /// or <paramref name="defaultValue"/> if no matching platform was registered.
    /// </summary>
    public T Fallback(T defaultValue = default!)
    {
        if (OperatingSystem.IsAndroid() && _android.HasValue) return _android.Value;
        if (OperatingSystem.IsIOS() && _ios.HasValue) return _ios.Value;
        if (OperatingSystem.IsWindows() && _windows.HasValue) return _windows.Value;
        if (OperatingSystem.IsMacCatalyst() && _macCatalyst.HasValue) return _macCatalyst.Value;
        return defaultValue;
    }
}

/// <summary>
/// Fluent builder that maps device idioms to a value of type <typeparamref name="T"/>.
/// Call <see cref="Fallback"/> to resolve the value for the current idiom.
/// </summary>
/// <typeparam name="T">The type to select. Works for reference types, structs, and enums.</typeparam>
public sealed class IdiomPlatformBuilder<T>
{
    private (bool HasValue, T Value) _phone;
    private (bool HasValue, T Value) _tablet;
    private (bool HasValue, T Value) _desktop;

    /// <summary>Registers a value for phones.</summary>
    public IdiomPlatformBuilder<T> ForPhone(T value) { _phone = (true, value); return this; }

    /// <summary>Registers a value for tablets.</summary>
    public IdiomPlatformBuilder<T> ForTablet(T value) { _tablet = (true, value); return this; }

    /// <summary>Registers a value for desktops.</summary>
    public IdiomPlatformBuilder<T> ForDesktop(T value) { _desktop = (true, value); return this; }

    /// <summary>
    /// Resolves and returns the registered value for the current device idiom,
    /// or <paramref name="defaultValue"/> if no matching idiom was registered.
    /// </summary>
    public T Fallback(T defaultValue = default!)
    {
        if (DeviceInfo.Idiom == DeviceIdiom.Phone && _phone.HasValue) return _phone.Value;
        if (DeviceInfo.Idiom == DeviceIdiom.Tablet && _tablet.HasValue) return _tablet.Value;
        if (DeviceInfo.Idiom == DeviceIdiom.Desktop && _desktop.HasValue) return _desktop.Value;
        return defaultValue;
    }
}
