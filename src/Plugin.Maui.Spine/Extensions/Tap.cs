using System.Windows.Input;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Makes any view a tap target: the whole view runs <see cref="CommandProperty"/>, shows the
/// platform's press feedback (a highlight on iOS and Mac Catalyst, a ripple on Android, hover and
/// pressed fills on Windows) and reads as a button to a screen reader when its semantics are merged
/// (<see cref="Semantic.MergeProperty"/>).
/// </summary>
/// <remarks>
/// Controls inside the view keep their own touches: a switch in a tappable row toggles, it does not
/// run the row's command, and an inner tap target wins over the outer one. The command runs only
/// while the view is enabled and <see cref="ICommand.CanExecute"/> is true. Nothing to register.
/// </remarks>
/// <example>
/// <code>
/// &lt;Border Tap.Command="{Binding OpenCommand}" Tap.CommandParameter="{Binding .}" Semantic.Merge="True"&gt;
///     ...
/// &lt;/Border&gt;
/// </code>
/// </example>
public static class Tap
{
    /// <summary>Attached property: the command a tap anywhere on the view runs.</summary>
    public static readonly BindableProperty CommandProperty =
        BindableProperty.CreateAttached(
            "Command",
            typeof(ICommand),
            typeof(Tap),
            null,
            propertyChanged: OnCommandChanged);

    /// <summary>Attached property: the parameter passed to <see cref="CommandProperty"/>.</summary>
    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.CreateAttached(
            "CommandParameter",
            typeof(object),
            typeof(Tap),
            null,
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Refresh());

    /// <summary>
    /// Attached property: the press colour. <see langword="null"/> = the platform's own
    /// (<c>systemFill</c> on iOS, the theme's ripple colour on Android, a subtle fill on Windows).
    /// Give it some transparency: it is drawn over the view's content.
    /// </summary>
    public static readonly BindableProperty HighlightColorProperty =
        BindableProperty.CreateAttached(
            "HighlightColor",
            typeof(Color),
            typeof(Tap),
            null,
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Refresh());

    static readonly BindableProperty StateProperty =
        BindableProperty.CreateAttached("State", typeof(TapState), typeof(Tap), null);

    /// <summary>Gets the command a tap on <paramref name="view"/> runs.</summary>
    public static ICommand? GetCommand(BindableObject view) => (ICommand?)view.GetValue(CommandProperty);

    /// <summary>Sets the command a tap on <paramref name="view"/> runs.</summary>
    public static void SetCommand(BindableObject view, ICommand? value) => view.SetValue(CommandProperty, value);

    /// <summary>Gets the parameter passed to the command of <paramref name="view"/>.</summary>
    public static object? GetCommandParameter(BindableObject view) => view.GetValue(CommandParameterProperty);

    /// <summary>Sets the parameter passed to the command of <paramref name="view"/>.</summary>
    public static void SetCommandParameter(BindableObject view, object? value) => view.SetValue(CommandParameterProperty, value);

    /// <summary>Gets the press colour of <paramref name="view"/>.</summary>
    public static Color? GetHighlightColor(BindableObject view) => (Color?)view.GetValue(HighlightColorProperty);

    /// <summary>Sets the press colour of <paramref name="view"/>.</summary>
    public static void SetHighlightColor(BindableObject view, Color? value) => view.SetValue(HighlightColorProperty, value);

    internal static TapState? GetState(BindableObject view) => (TapState?)view.GetValue(StateProperty);

    static void OnCommandChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not View view)
            return;

        var state = GetState(view);

        if (newValue is not ICommand command)
        {
            if (state is not null)
            {
                state.Dispose();
                view.ClearValue(StateProperty);
            }

            Semantic.Refresh(view);
            return;
        }

        if (state is null)
        {
            state = new TapState(view);
            view.SetValue(StateProperty, state);
        }

        state.SetCommand(command);
        Semantic.Refresh(view);
    }
}

/// <summary>
/// The per-view side of <see cref="Tap"/>: follows the view's handler, owns the platform touch
/// handling and decides whether a tap may run the command.
/// </summary>
internal sealed partial class TapState : IDisposable
{
    readonly View _view;
    ICommand? _command;
    bool _connected;

    public TapState(View view)
    {
        _view = view;
        view.HandlerChanging += OnHandlerChanging;
        view.HandlerChanged += OnHandlerChanged;
        view.PropertyChanged += OnViewPropertyChanged;

        if (view.Handler is not null)
            Connect();
    }

    public View View => _view;

    /// <summary>Whether a tap would run the command right now.</summary>
    public bool CanExecute =>
        _view.IsEnabled
        && _command is not null
        && _command.CanExecute(Tap.GetCommandParameter(_view));

    public Color? HighlightColor => Tap.GetHighlightColor(_view);

    public void SetCommand(ICommand command)
    {
        if (ReferenceEquals(command, _command))
            return;

        if (_connected && _command is not null)
            _command.CanExecuteChanged -= OnCanExecuteChanged;

        _command = command;

        if (_connected)
            _command.CanExecuteChanged += OnCanExecuteChanged;

        Refresh();
    }

    /// <summary>Runs the command when the view is enabled and the command can execute.</summary>
    public void Execute()
    {
        if (!CanExecute)
            return;

        _command!.Execute(Tap.GetCommandParameter(_view));
    }

    public void Refresh()
    {
        if (!_connected)
            return;

        UpdatePlatform();
        Semantic.Refresh(_view);
    }

    public void Dispose()
    {
        Disconnect();
        _view.HandlerChanging -= OnHandlerChanging;
        _view.HandlerChanged -= OnHandlerChanged;
        _view.PropertyChanged -= OnViewPropertyChanged;
        _command = null;
    }

    void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (e.OldHandler is not null)
            Disconnect();
    }

    void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (_view.Handler is not null)
            Connect();
    }

    void OnViewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == VisualElement.IsEnabledProperty.PropertyName)
            Refresh();
    }

    void OnCanExecuteChanged(object? sender, EventArgs e) => Refresh();

    void Connect()
    {
        if (_connected || _view.Handler?.PlatformView is not { } platformView)
            return;

        _connected = true;

        if (_command is not null)
            _command.CanExecuteChanged += OnCanExecuteChanged;

        ConnectPlatform(platformView);
        UpdatePlatform();
        Semantic.Refresh(_view);
    }

    void Disconnect()
    {
        if (!_connected)
            return;

        _connected = false;

        if (_command is not null)
            _command.CanExecuteChanged -= OnCanExecuteChanged;

        DisconnectPlatform();
    }

    /// <summary>The corner radius the press highlight follows: a <see cref="Border"/>'s rounded shape.</summary>
    double CornerRadius() =>
        _view is Border { StrokeShape: Microsoft.Maui.Controls.Shapes.RoundRectangle shape } ? shape.CornerRadius.TopLeft : 0;

    partial void ConnectPlatform(object platformView);

    partial void DisconnectPlatform();

    partial void UpdatePlatform();
}
