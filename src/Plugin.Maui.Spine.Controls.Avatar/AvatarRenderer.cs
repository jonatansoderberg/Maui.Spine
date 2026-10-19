using System.Diagnostics.CodeAnalysis;
using Plugin.Maui.Spine.Controls.Avatar;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Draws the representations it knows. <see cref="AvatarView"/> asks the app's renderers (<see cref="SpineAvatarOptions.Renderers"/>)
/// and then the built-in ones, for the package's representations best first.
/// </summary>
[Experimental(DiagnosticId)]
public abstract class AvatarRenderer
{
    /// <summary>The diagnostic of the renderer door, experimental until 3D on Android and Windows is settled (spike S2).</summary>
    public const string DiagnosticId = "SPINEAVATAR001";

    /// <summary>The renderer id it answers to in a manifest, such as <c>native3d</c>.</summary>
    public abstract string Renderer { get; }

    /// <summary>Whether it can draw <paramref name="representation"/> on this platform.</summary>
    public abstract bool CanDraw(AvatarRepresentation representation);

    /// <summary>A surface for one view, created on the UI thread. The package caches what it parses, so a second view of the same avatar is cheap.</summary>
    public abstract AvatarSurface CreateSurface(AvatarPackage package, AvatarRepresentation representation);
}

/// <summary>What draws one view's frames. The view owns it and disposes it when the avatar changes or the view goes.</summary>
[Experimental(AvatarRenderer.DiagnosticId)]
public abstract class AvatarSurface : IDisposable
{
    /// <summary>The view that shows the avatar.</summary>
    public abstract View View { get; }

    /// <summary>A short name for diagnostics, such as <c>skia (Metal)</c>.</summary>
    public abstract string RendererName { get; }

    /// <summary>Shows <paramref name="frame"/>. The frame belongs to the scheduler: read it now, keep nothing.</summary>
    /// <param name="frame">The poses, clips, levels, blink and gaze to show.</param>
    /// <param name="dark">Whether the app is in dark mode.</param>
    /// <param name="accent">The colour for the avatar's <c>accent</c> slot, or null for its own.</param>
    public abstract void Render(AvatarRenderFrame frame, bool dark, Color? accent);

    /// <summary>
    /// Colours for theme slots by name, over the package's own light and dark ones (<see cref="AvatarView.SetThemeColor"/>).
    /// A renderer that colours by slot reads it when it draws; one that cannot, ignores it.
    /// </summary>
    public IReadOnlyDictionary<string, Color> ThemeColors { get; internal set; } = new Dictionary<string, Color>();

    internal AvatarFrameStats Stats { get; } = new();

    /// <summary>Releases the renderer's resources.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the renderer's resources when <paramref name="disposing"/>.</summary>
    protected virtual void Dispose(bool disposing) { }
}
