using System.ComponentModel;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// The builder registrations of the Spine packages an app references, run by
/// <see cref="SpineExtensions.UseSpine"/>.
/// </summary>
/// <remarks>
/// Filled at build time, not by scanning: each package that needs the <see cref="MauiAppBuilder"/>
/// declares a <c>&lt;SpineModule&gt;</c> item in its <c>buildTransitive</c> props, and
/// <c>Plugin.Maui.Spine.targets</c> generates a <c>[ModuleInitializer]</c> in the app that calls
/// <see cref="Add"/> once per module. See <c>docs/wiki/packages.md</c>.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SpineModules
{
    private static readonly List<Action<MauiAppBuilder>> Registrations = [];

    /// <summary>Adds a package's registration. Called by the generated module initializer.</summary>
    public static void Add(Action<MauiAppBuilder> register) => Registrations.Add(register);

    internal static void Run(MauiAppBuilder builder)
    {
        foreach (var register in Registrations)
            register(builder);
    }
}
