using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Gives any view the platform's own context menu: a long press on touch, a right click with a
/// mouse. iOS and iPadOS lift the view, rounded like a <see cref="Border"/>'s shape, under a
/// <c>UIContextMenuInteraction</c>; Mac Catalyst shows the compact menu; Android a <c>PopupMenu</c>
/// anchored to the view; Windows a <c>ContextFlyout</c>.
/// </summary>
/// <remarks>
/// The menu is a <see cref="MenuItems"/>, the same model as <see cref="MenuButton"/> and
/// <see cref="PageAction.Menu"/>, so a row and its detail page's header can share one instance.
/// It is built each time it opens: a list row costs one interaction, and a recycled row shows
/// its current item. Hide rows that do not apply with <see cref="MenuAction.IsVisible"/>.
/// <see cref="Tap.CommandProperty"/> on the same view still runs on a plain tap. On a
/// <see cref="Button"/> that also has <see cref="MenuButton.ItemsProperty"/>, the menu button wins.
/// Nothing to register.
/// </remarks>
/// <example>
/// <code>
/// &lt;Grid ContextMenu.Items="{Binding RowMenu, Source={RelativeSource AncestorType={x:Type vm:ListViewModel}}}"
///       ContextMenu.CommandParameter="{Binding .}"&gt;
///     ...
/// &lt;/Grid&gt;
/// </code>
/// </example>
public static class ContextMenu
{
    /// <summary>Attached property holding the menu.</summary>
    public static readonly BindableProperty ItemsProperty =
        BindableProperty.CreateAttached(
            "Items",
            typeof(MenuItems),
            typeof(ContextMenu),
            null,
            propertyChanged: OnItemsChanged);

    /// <summary>
    /// Attached property: passed to a picked action's command when the action has no
    /// <see cref="MenuAction.CommandParameter"/> of its own, typically the row's item.
    /// </summary>
    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.CreateAttached(
            "CommandParameter",
            typeof(object),
            typeof(ContextMenu),
            null);

    static readonly BindableProperty StateProperty =
        BindableProperty.CreateAttached("State", typeof(ContextMenuState), typeof(ContextMenu), null);

    /// <summary>Gets the context menu of <paramref name="view"/>.</summary>
    public static MenuItems? GetItems(BindableObject view) => (MenuItems?)view.GetValue(ItemsProperty);

    /// <summary>Sets the context menu of <paramref name="view"/>.</summary>
    public static void SetItems(BindableObject view, MenuItems? value) => view.SetValue(ItemsProperty, value);

    /// <summary>Gets the fallback parameter of the context menu of <paramref name="view"/>.</summary>
    public static object? GetCommandParameter(BindableObject view) => view.GetValue(CommandParameterProperty);

    /// <summary>Sets the fallback parameter of the context menu of <paramref name="view"/>.</summary>
    public static void SetCommandParameter(BindableObject view, object? value) => view.SetValue(CommandParameterProperty, value);

    /// <summary>
    /// Opens the context menu of <paramref name="view"/> from code, for a view whose own long press
    /// never reaches the platform's: on Android a view with MAUI gesture recognizers takes the touch
    /// before the long click. Android shows the menu with the long-press haptic, Windows shows its
    /// flyout. iOS and Mac Catalyst open a context menu only from the system's own gesture, so there
    /// it returns <see langword="false"/>, as it does for a view without a menu or with nothing visible.
    /// </summary>
    public static bool Show(View view) => GetState(view)?.Show() ?? false;

    internal static ContextMenuState? GetState(BindableObject view) => (ContextMenuState?)view.GetValue(StateProperty);

    static void OnItemsChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not View view)
            return;

        var state = GetState(view);

        if (newValue is null)
        {
            if (state is not null)
            {
                state.Dispose();
                view.ClearValue(StateProperty);
            }

            return;
        }

        if (state is null)
            view.SetValue(StateProperty, new ContextMenuState(view));
        else
            state.Refresh();
    }
}

/// <summary>
/// The per-view side of <see cref="ContextMenu"/>: follows the view's handler and owns the
/// platform's interaction or listener.
/// </summary>
internal sealed partial class ContextMenuState : IDisposable
{
    readonly View _view;
    bool _connected;

    public ContextMenuState(View view)
    {
        _view = view;
        view.HandlerChanging += OnHandlerChanging;
        view.HandlerChanged += OnHandlerChanged;

        if (view.Handler is not null)
            Connect();
    }

    public View View => _view;

    public MenuItems? Items => ContextMenu.GetItems(_view);

    public object? Parameter => ContextMenu.GetCommandParameter(_view);

    /// <summary>Whether a long press opens a menu right now: the view is enabled and has a visible row.</summary>
    public bool CanOpen => _view.IsEnabled && Items is { } items && HasVisibleRow(items);

    public void Refresh()
    {
        if (_connected)
            UpdatePlatform();
    }

    public bool Show()
    {
        var shown = false;
        if (_connected && CanOpen)
            ShowPlatform(ref shown);
        return shown;
    }

    public void Dispose()
    {
        Disconnect();
        _view.HandlerChanging -= OnHandlerChanging;
        _view.HandlerChanged -= OnHandlerChanged;
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

    void Connect()
    {
        if (_connected || _view.Handler?.PlatformView is not { } platformView)
            return;

        _connected = true;
        ConnectPlatform(platformView);
        UpdatePlatform();
    }

    void Disconnect()
    {
        if (!_connected)
            return;

        _connected = false;
        DisconnectPlatform();
    }

    static bool HasVisibleRow(IEnumerable<MenuElement> elements) => elements.Any(element => element switch
    {
        MenuAction action => action.IsVisible,
        SubMenu subMenu => subMenu.IsVisible && HasVisibleRow(subMenu.Items),
        MenuSection section => HasVisibleRow(section.Items),
        MenuPicker picker => picker.Items.Any(static a => a.IsVisible),
        _ => false,
    });

    partial void ConnectPlatform(object platformView);

    partial void DisconnectPlatform();

    partial void UpdatePlatform();

    partial void ShowPlatform(ref bool shown);
}
