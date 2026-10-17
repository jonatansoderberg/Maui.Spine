using Plugin.Maui.Spine.Controls.Avatar;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// What a view shows when the platform has no renderer for any of the package's representations: the package's poster
/// for the theme, and the state as text, so the avatar still says what it is doing.
/// </summary>
internal sealed class PosterAvatarSurface : AvatarSurface
{
    private readonly AvatarPackage _package;
    private readonly Image _image = new() { Aspect = Aspect.AspectFit, InputTransparent = true };
    private readonly Label _state = new() { HorizontalTextAlignment = TextAlignment.Center, FontSize = 13, Opacity = 0.7, InputTransparent = true };
    private bool? _dark;

    public PosterAvatarSurface(AvatarPackage package)
    {
        _package = package;
        var grid = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto)], InputTransparent = true, CascadeInputTransparent = true };
        grid.Add(_image, 0, 0);
        grid.Add(_state, 0, 1);
        View = grid;
    }

    public override View View { get; }

    public override string RendererName => "poster";

    /// <summary>The state written under the poster; the view sets it, a frame does not carry it.</summary>
    public AvatarState State
    {
        set => _state.Text = AvatarVocabulary.Name(value);
    }

    public override void Render(AvatarRenderFrame frame, bool dark, Color? accent)
    {
        if (_dark == dark)
            return;
        _dark = dark;
        var path = dark ? _package.Manifest.Posters.Dark : _package.Manifest.Posters.Light;
        var bytes = _package.GetFile(path);
        _image.Source = ImageSource.FromStream(() => new MemoryStream(bytes.ToArray(), writable: false));
    }
}
