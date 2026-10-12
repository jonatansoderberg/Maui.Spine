using System.Windows.Input;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary>
/// Asks for an unlock before a button or a tappable view runs its command. For a command bound with a plain
/// binding, which a <see cref="RequiresUnlockAttribute"/> on the command does not reach.
/// </summary>
/// <remarks>
/// On a <see cref="Button"/> it sets <see cref="Button.Command"/>; on any other view it sets
/// <see cref="Tap.CommandProperty"/>, with Tap's press feedback. <c>Button.CommandParameter</c> and
/// <c>Tap.CommandParameter</c> go to the command as usual.
/// </remarks>
/// <example>
/// <code>
/// &lt;Button Text="Show PIN" spine:Unlock.Command="{Binding ShowPinCommand}" spine:Unlock.Reason="Show the PIN" /&gt;
/// </code>
/// </example>
public static class Unlock
{
    /// <summary>Attached property: the command that runs after an unlock.</summary>
    public static readonly BindableProperty CommandProperty =
        BindableProperty.CreateAttached("Command", typeof(ICommand), typeof(Unlock), null, propertyChanged: OnChanged);

    /// <summary>Attached property: why the app asks, shown in the prompt (text, or the key of a Spine string).</summary>
    public static readonly BindableProperty ReasonProperty =
        BindableProperty.CreateAttached("Reason", typeof(string), typeof(Unlock), null);

    /// <summary>Attached property: ask every time, even right after an unlock.</summary>
    public static readonly BindableProperty FreshProperty =
        BindableProperty.CreateAttached("Fresh", typeof(bool), typeof(Unlock), false);

    /// <summary>Gets the command of <paramref name="view"/>.</summary>
    public static ICommand? GetCommand(BindableObject view) => (ICommand?)view.GetValue(CommandProperty);

    /// <summary>Sets the command that runs on <paramref name="view"/> after an unlock.</summary>
    public static void SetCommand(BindableObject view, ICommand? value) => view.SetValue(CommandProperty, value);

    /// <summary>Gets the reason of <paramref name="view"/>.</summary>
    public static string? GetReason(BindableObject view) => (string?)view.GetValue(ReasonProperty);

    /// <summary>Sets the reason shown in the prompt for <paramref name="view"/>.</summary>
    public static void SetReason(BindableObject view, string? value) => view.SetValue(ReasonProperty, value);

    /// <summary>Gets whether <paramref name="view"/> asks every time.</summary>
    public static bool GetFresh(BindableObject view) => (bool)view.GetValue(FreshProperty);

    /// <summary>Sets whether <paramref name="view"/> asks every time.</summary>
    public static void SetFresh(BindableObject view, bool value) => view.SetValue(FreshProperty, value);

    private static void OnChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var command = newValue is ICommand inner ? new UnlockCommand(bindable, inner) : null;

        if (bindable is Button button)
            button.Command = command;
        else
            Tap.SetCommand(bindable, command);
    }

    private sealed class UnlockCommand(BindableObject view, ICommand inner) : ICommand
    {
        private bool _pending;

        public event EventHandler? CanExecuteChanged
        {
            add => inner.CanExecuteChanged += value;
            remove => inner.CanExecuteChanged -= value;
        }

        public bool CanExecute(object? parameter) => inner.CanExecute(parameter);

        public async void Execute(object? parameter)
        {
            if (_pending)
                return;

            _pending = true;
            try
            {
                var unlock = IPlatformApplication.Current!.Services.GetRequiredService<IUnlock>();
                var reason = BiometricsStrings.Reason(GetReason(view) ?? BiometricsStrings.Get("Spine.Biometrics.Unlock"));

                if (await unlock.RequireAsync(reason, GetFresh(view)) && inner.CanExecute(parameter))
                    inner.Execute(parameter);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[Spine] Unlock.Command: {e}");
            }
            finally
            {
                _pending = false;
            }
        }
    }
}
