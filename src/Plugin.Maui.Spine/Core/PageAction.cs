using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Specifies where a <see cref="PageAction"/> button is placed in the header bar.
/// </summary>
public enum PageActionPlacement
{
    /// <summary>
    /// The action is placed on the primary (leading / left) side of the header bar.
    /// Typically used for navigation actions such as back or menu.
    /// </summary>
    Primary,

    /// <summary>
    /// The action is placed on the secondary (trailing / right) side of the header bar.
    /// Typically used for page-specific actions such as Save or Settings.
    /// </summary>
    Secondary
}

/// <summary>
/// Represents a button that appears in Spine's header bar.
/// Add instances to <see cref="ViewModelBase.PageActions"/> inside
/// <see cref="ViewModelBase.OnAppearingAsync"/> to surface them in the UI.
/// </summary>
/// <example>
/// <code>
/// // Text button
/// PageActions.Add(new PageAction("Save", SaveCommand));
///
/// // Icon-only button
/// PageActions.Add(new PageAction(null, OpenSettingsCommand) { Svg = "settings.svg" });
/// </code>
/// </example>
public sealed class PageAction
{
    /// <summary>
    /// Initializes a new <see cref="PageAction"/> with a label text and a synchronous command.
    /// </summary>
    /// <param name="text">Label displayed on the button, or <see langword="null"/> for icon-only buttons.</param>
    /// <param name="command">The command executed when the button is tapped.</param>
    public PageAction(string? text, ICommand command)
    {
        Text = text;
        Command = command ?? throw new ArgumentNullException(nameof(command));
    }

    /// <summary>
    /// Initializes a new <see cref="PageAction"/> with a label text and an async relay command.
    /// The async command is also exposed via <see cref="AsyncCommand"/> so the UI can bind to it directly.
    /// </summary>
    /// <param name="text">Label displayed on the button, or <see langword="null"/> for icon-only buttons.</param>
    /// <param name="command">The async relay command executed when the button is tapped.</param>
    public PageAction(string? text, IAsyncRelayCommand command)
        : this(text, (ICommand)command)
    {
        AsyncCommand = command;
    }

    /// <summary>Label text displayed on the button. Set to <see langword="null"/> for icon-only buttons.</summary>
    public string? Text { get; }

    /// <summary>
    /// Optional svg resource name, e.g. <c>"settings.svg"</c>.
    /// When set the button renders the SVG icon instead of (or alongside) the <see cref="Text"/>.
    /// </summary>
    public string? Svg { get; init; }

    /// <summary>The command executed when the button is tapped.</summary>
    public ICommand Command { get; }

    /// <summary>
    /// The async relay command, when the action was created with an <see cref="IAsyncRelayCommand"/>.
    /// <see langword="null"/> when constructed with a plain <see cref="ICommand"/>.
    /// </summary>
    public IAsyncRelayCommand? AsyncCommand { get; }

    /// <summary>Optional parameter forwarded to <see cref="Command"/> when it is executed.</summary>
    public object? CommandParameter { get; init; }

    /// <summary>
    /// Where in the header bar the button is placed.
    /// Defaults to <see cref="PageActionPlacement.Secondary"/> (trailing/right side).
    /// </summary>
    public PageActionPlacement Placement { get; init; } = PageActionPlacement.Secondary;

    /// <summary>
    /// Whether this action is currently visible in the header bar.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool IsVisible { get; init; } = true;
}
