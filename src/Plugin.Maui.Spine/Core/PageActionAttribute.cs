using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Declares a header-bar button for a command on a <see cref="ViewModelBase"/>, so the page needs
/// no code to populate <see cref="ViewModelBase.PageActions"/>. Spine adds the
/// <see cref="PageAction"/> once, before the page first appears.
/// </summary>
/// <remarks>
/// Put it on a <c>[RelayCommand]</c> method, where the generated command property is found by the
/// toolkit's naming rule (<c>SaveAsync</c> and <c>Save</c> both give <c>SaveCommand</c>), or on a
/// property of type <see cref="System.Windows.Input.ICommand"/>. Set <see cref="Command"/> to
/// name the command property explicitly.
/// </remarks>
/// <example>
/// <code>
/// [PageAction("Save", Role = PageActionRole.Confirm)]
/// [RelayCommand]
/// private Task SaveAsync() { ... }
///
/// [PageAction(Svg = "settings.svg", Placement = PageActionPlacement.Secondary)]
/// [RelayCommand]
/// private Task OpenSettingsAsync() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PageActionAttribute : Attribute
{
    /// <summary>Declares an icon-only or text-less action; set <see cref="Svg"/>.</summary>
    public PageActionAttribute() { }

    /// <summary>Declares an action with a label <paramref name="text"/>.</summary>
    public PageActionAttribute(string text)
    {
        Text = text;
    }

    /// <summary>Label text, or <see langword="null"/> for an icon-only button.</summary>
    public string? Text { get; }

    /// <summary>SVG resource name rendered instead of the text, e.g. <c>"settings.svg"</c>.</summary>
    public string? Svg { get; init; }

    /// <summary>
    /// Which header-bar slot the button takes. Defaults to <see cref="PageActionPlacement.Secondary"/>, or
    /// <see cref="PageActionPlacement.Primary"/> for a <see cref="PageActionRole.Cancel"/>.
    /// </summary>
    public PageActionPlacement Placement
    {
        get => _placement ?? (Role == PageActionRole.Cancel ? PageActionPlacement.Primary : PageActionPlacement.Secondary);
        init => _placement = value;
    }

    private readonly PageActionPlacement? _placement;

    /// <summary>
    /// What the action does: <see cref="PageActionRole.Confirm"/> for a checkmark, <see cref="PageActionRole.Cancel"/>
    /// for an X, each with a screen-reader description; the text, if given, is what a screen reader says.
    /// </summary>
    /// <example>
    /// <code>
    /// [PageAction(Role = PageActionRole.Confirm)]
    /// [RelayCommand]
    /// private Task SaveAsync() { ... }
    /// </code>
    /// </example>
    public PageActionRole Role { get; init; }

    /// <summary>Order among the page's declared actions; lower comes first. Defaults to 0.</summary>
    public int Order { get; init; }

    /// <summary>Initial <see cref="PageAction.Badge"/> text, or <see langword="null"/>.</summary>
    public string? Badge { get; init; }

    /// <summary>Initial <see cref="PageAction.IsVisible"/>. Defaults to <see langword="true"/>.</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>
    /// Whether <see cref="PageAction.IsBusy"/> follows the command while it runs, so the button dims and takes no
    /// taps until the work is done. Needs an async command (<c>[RelayCommand]</c> on a method returning a
    /// <see cref="Task"/>). Defaults to <see langword="false"/>. Add <see cref="SpinsWhenBusy"/> for an icon that
    /// turns as well, such as a refresh.
    /// </summary>
    /// <example>
    /// <code>
    /// [PageAction("Save", Role = PageActionRole.Confirm, ShowsBusy = true)]
    /// [RelayCommand]
    /// private Task SaveAsync() => StoreAsync();
    /// </code>
    /// </example>
    public bool ShowsBusy { get; init; }

    /// <summary>
    /// Initial <see cref="PageAction.SpinsWhenBusy"/>: whether the icon turns while the action is busy. Defaults to
    /// <see langword="false"/>, so a busy action only dims.
    /// </summary>
    /// <example>
    /// <code>
    /// [PageAction(Svg = "Reload.svg", Placement = PageActionPlacement.Primary, ShowsBusy = true, SpinsWhenBusy = true)]
    /// [RelayCommand]
    /// private Task RefreshAsync() => LoadAsync();
    /// </code>
    /// </example>
    public bool SpinsWhenBusy { get; init; }

    /// <summary>Initial <see cref="PageAction.Haptic"/>: the haptic a tap plays. Defaults to <see cref="Haptic.None"/>.</summary>
    public Haptic Haptic { get; init; }

    /// <summary>
    /// Name of the command property on the view model, when it does not follow the
    /// <c>[RelayCommand]</c> naming rule or the attribute sits on a method without one.
    /// </summary>
    public string? Command { get; init; }
}
