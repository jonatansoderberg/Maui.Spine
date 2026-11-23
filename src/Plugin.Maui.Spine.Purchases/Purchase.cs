using System.Windows.Input;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Purchases;

/// <summary>
/// Purchases on a view: <see cref="RequiresProperty"/> names the purchase the view needs, and <see cref="CommandProperty"/>
/// runs a command only once it is active, after the paywall when it is not. For a plain binding, which a
/// <see cref="RequiresPurchaseAttribute"/> on the command does not reach.
/// </summary>
/// <remarks>
/// On a <see cref="SpineRow"/>, <see cref="RequiresProperty"/> also shows the purchase's name as a mark in the row's accessory
/// while it is not active. The row's tap goes through the paywall only with <see cref="CommandProperty"/>: a bound
/// <c>Command</c> cannot be wrapped, because setting the property from code would drop the binding.
/// </remarks>
/// <example>
/// <code>
/// &lt;Button Text="Export" Purchase.Command="{Binding ExportCommand}" Purchase.Requires="{x:Static app:AppPurchase.Pro}" /&gt;
///
/// &lt;SpineRow Title="Statistics" Purchase.Command="{Binding OpenStatisticsCommand}" Purchase.Requires="{x:Static app:AppPurchase.Pro}" /&gt;
/// </code>
/// </example>
public static class Purchase
{
    /// <summary>
    /// Attached property: the purchase the view needs, a member of the app's registered purchase enum. It is the purchase
    /// <see cref="CommandProperty"/> asks for, and on a <see cref="SpineRow"/> a mark while it is not active.
    /// </summary>
    public static readonly BindableProperty RequiresProperty =
        BindableProperty.CreateAttached("Requires", typeof(object), typeof(Purchase), null, propertyChanged: OnRequiresChanged);

    /// <summary>
    /// Attached property: the command that runs once <see cref="RequiresProperty"/> is active, after the paywall when it is not.
    /// It sets <see cref="Button.Command"/> on a button, <see cref="SpineRow.Command"/> on a row and <see cref="Tap.CommandProperty"/>
    /// on any other view.
    /// </summary>
    public static readonly BindableProperty CommandProperty =
        BindableProperty.CreateAttached("Command", typeof(ICommand), typeof(Purchase), null, propertyChanged: OnCommandChanged);

    private static readonly BindableProperty MarkProperty =
        BindableProperty.CreateAttached("Mark", typeof(RowMark), typeof(Purchase), null);

    /// <summary>Gets the purchase <paramref name="view"/> needs.</summary>
    public static object? GetRequires(BindableObject view) => view.GetValue(RequiresProperty);

    /// <summary>Sets the purchase <paramref name="view"/> needs.</summary>
    public static void SetRequires(BindableObject view, object? value) => view.SetValue(RequiresProperty, value);

    /// <summary>Gets the command of <paramref name="view"/>.</summary>
    public static ICommand? GetCommand(BindableObject view) => (ICommand?)view.GetValue(CommandProperty);

    /// <summary>Sets the command that runs on <paramref name="view"/> once its purchase is active.</summary>
    public static void SetCommand(BindableObject view, ICommand? value) => view.SetValue(CommandProperty, value);

    private static AppPurchases? Service => IPlatformApplication.Current?.Services.GetService<AppPurchases>();

    private static void OnCommandChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var command = newValue is ICommand inner ? new PurchaseCommand(bindable, inner) : null;

        switch (bindable)
        {
            case Button button:
                button.Command = command;
                break;
            case SpineRow row:
                row.Command = command;
                break;
            default:
                Tap.SetCommand(bindable, command);
                break;
        }
    }

    private static void OnRequiresChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not SpineRow row)
            return;

        (row.GetValue(MarkProperty) as RowMark)?.Detach();
        row.SetValue(MarkProperty, newValue is null || Service is not { } purchases ? null : new RowMark(row, purchases, purchases.Catalog.Purchase(newValue).Value));
    }

    private sealed class PurchaseCommand(BindableObject view, ICommand inner) : ICommand
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
                if (GetRequires(view) is not { } required)
                    throw new InvalidOperationException("Spine purchases: Purchase.Command needs Purchase.Requires on the same view.");

                if (Service is { } purchases && await purchases.RequireAsync(purchases.Catalog.Purchase(required).Value) && inner.CanExecute(parameter))
                    inner.Execute(parameter);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[Spine] Purchase.Command: {e}");
            }
            finally
            {
                _pending = false;
            }
        }
    }

    /// <summary>The purchase's name in a row's accessory while it is not active; the row's own accessory otherwise.</summary>
    private sealed class RowMark
    {
        private readonly SpineRow _row;
        private readonly AppPurchases _purchases;
        private readonly Enum _purchase;
        private readonly View _badge;
        private View? _accessory;
        private bool _applied;

        public RowMark(SpineRow row, AppPurchases purchases, Enum purchase)
        {
            _row = row;
            _purchases = purchases;
            _purchase = purchase;
            _badge = Badge(PurchasesStrings.PurchaseName(purchase));

            // Only while on screen, so the singleton's event does not keep the page alive.
            row.Loaded += OnLoaded;
            row.Unloaded += OnUnloaded;
            if (row.IsLoaded)
                purchases.Changed += OnChanged;
            row.PropertyChanged += OnRowChanged;
            Apply();
        }

        public void Detach()
        {
            _purchases.Changed -= OnChanged;
            _row.Loaded -= OnLoaded;
            _row.Unloaded -= OnUnloaded;
            _row.PropertyChanged -= OnRowChanged;
            Restore();
        }

        private void OnChanged(object? sender, PurchasesChangedEventArgs e) => Apply();

        private void OnLoaded(object? sender, EventArgs e)
        {
            _purchases.Changed -= OnChanged;
            _purchases.Changed += OnChanged;
            Apply();
        }

        private void OnUnloaded(object? sender, EventArgs e) => _purchases.Changed -= OnChanged;

        // An accessory set after Purchase.Requires (a later attribute) is the row's own: keep it behind the mark.
        private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_applied && e.PropertyName == SpineRow.AccessoryProperty.PropertyName && !ReferenceEquals(_row.Accessory, _badge))
            {
                _accessory = _row.Accessory;
                _row.Accessory = _badge;
            }
        }

        private void Apply()
        {
            if (_purchases.State(_purchase).IsActive)
            {
                Restore();
                return;
            }

            if (_applied)
                return;

            _applied = true;
            _accessory = _row.Accessory;
            _row.Accessory = _badge;
        }

        private void Restore()
        {
            if (!_applied)
                return;

            _applied = false;
            _row.Accessory = _accessory;
        }

        private static View Badge(string text)
        {
            var label = new Label { Text = text, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
            return new Border
            {
                Content = label,
                Padding = new Thickness(8, 2),
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb("#FF2D55"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 9 },
                VerticalOptions = LayoutOptions.Center,
            };
        }
    }
}
