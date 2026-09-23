using CommunityToolkit.Mvvm.ComponentModel;
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
/// Declare one with <see cref="PageActionAttribute"/> on a command, or add instances to
/// <see cref="ViewModelBase.PageActions"/>. Every property that describes the button can be
/// changed while the page is showing and the header bar follows.
/// </summary>
/// <example>
/// <code>
/// [PageAction("Save")]
/// [RelayCommand]
/// private Task SaveAsync() { ... }
///
/// // Or by hand, e.g. from a constructor:
/// PageActions.Add(new PageAction(null, OpenSettingsCommand) { Svg = "settings.svg" });
///
/// // Later, while the page is showing:
/// _filter.Text = $"Filter ({count})";
/// _filter.Badge = count > 0 ? count.ToString() : null;
/// </code>
/// </example>
public sealed partial class PageAction : ObservableObject
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

    /// <summary>Label text displayed on the button. <see langword="null"/> for icon-only buttons.</summary>
    [ObservableProperty]
    public partial string? Text { get; set; }

    /// <summary>
    /// Optional svg resource name, e.g. <c>"settings.svg"</c>.
    /// When set the button renders the SVG icon instead of the <see cref="Text"/>.
    /// </summary>
    [ObservableProperty]
    public partial string? Svg { get; set; }

    /// <summary>The command executed when the button is tapped.</summary>
    public ICommand Command { get; }

    /// <summary>
    /// The async relay command, when the action was created with an <see cref="IAsyncRelayCommand"/>.
    /// <see langword="null"/> when constructed with a plain <see cref="ICommand"/>.
    /// </summary>
    public IAsyncRelayCommand? AsyncCommand { get; }

    /// <summary>Optional parameter forwarded to <see cref="Command"/> when it is executed.</summary>
    [ObservableProperty]
    public partial object? CommandParameter { get; set; }

    /// <summary>
    /// Where in the header bar the button is placed.
    /// Defaults to <see cref="PageActionPlacement.Secondary"/> (trailing/right side).
    /// </summary>
    public PageActionPlacement Placement { get; init; } = PageActionPlacement.Secondary;

    /// <summary>Whether this action is currently shown in the header bar. Defaults to <see langword="true"/>.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    /// <summary>Whether the button responds to taps. Defaults to <see langword="true"/>.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Short text shown in a small pill over the button, such as a count. <see langword="null"/> hides the pill.
    /// </summary>
    [ObservableProperty]
    public partial string? Badge { get; set; }
}
