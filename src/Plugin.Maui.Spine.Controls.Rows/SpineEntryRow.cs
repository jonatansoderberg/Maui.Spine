using System.Windows.Input;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A text field as a row of a <see cref="SpineSection"/>: no frame of its own, since the section's group is the frame,
/// with the rows' insets, height, fonts and separators. <see cref="IsPassword"/> hides what is typed and adds an eye
/// button that shows it.
/// </summary>
/// <remarks>
/// The field fills the row, so a tap anywhere on it focuses the field. Fonts and colours come from
/// <see cref="SpineRowStyleOptions"/>: the text in the title's, the placeholder in the detail's.
/// </remarks>
/// <example>
/// <code>
/// &lt;SpineSection Header="Account"&gt;
///     &lt;SpineEntryRow Text="{Binding Email}" Placeholder="Email" Keyboard="Email"
///                    IsSpellCheckEnabled="False" IsTextPredictionEnabled="False" ReturnType="Next" /&gt;
///     &lt;SpineEntryRow Text="{Binding Password}" Placeholder="Password" IsPassword="True"
///                    ReturnType="Go" ReturnCommand="{Binding SignInCommand}" /&gt;
/// &lt;/SpineSection&gt;
/// </code>
/// </example>
public class SpineEntryRow : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(SpineEntryRow), null, BindingMode.TwoWay);

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(SpineEntryRow), null);

    public static readonly BindableProperty KeyboardProperty = BindableProperty.Create(
        nameof(Keyboard), typeof(Keyboard), typeof(SpineEntryRow), Keyboard.Default);

    public static readonly BindableProperty IsPasswordProperty = BindableProperty.Create(
        nameof(IsPassword), typeof(bool), typeof(SpineEntryRow), false,
        propertyChanged: static (b, _, _) => ((SpineEntryRow)b).ApplyPassword());

    public static readonly BindableProperty IsPasswordShownProperty = BindableProperty.Create(
        nameof(IsPasswordShown), typeof(bool), typeof(SpineEntryRow), false, BindingMode.TwoWay,
        propertyChanged: static (b, _, _) => ((SpineEntryRow)b).ApplyPassword());

    public static readonly BindableProperty IsSpellCheckEnabledProperty = BindableProperty.Create(
        nameof(IsSpellCheckEnabled), typeof(bool), typeof(SpineEntryRow), true);

    public static readonly BindableProperty IsTextPredictionEnabledProperty = BindableProperty.Create(
        nameof(IsTextPredictionEnabled), typeof(bool), typeof(SpineEntryRow), true);

    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength), typeof(int), typeof(SpineEntryRow), int.MaxValue);

    public static readonly BindableProperty ReturnTypeProperty = BindableProperty.Create(
        nameof(ReturnType), typeof(ReturnType), typeof(SpineEntryRow), ReturnType.Default);

    public static readonly BindableProperty ReturnCommandProperty = BindableProperty.Create(
        nameof(ReturnCommand), typeof(ICommand), typeof(SpineEntryRow), null);

    public static readonly BindableProperty ReturnCommandParameterProperty = BindableProperty.Create(
        nameof(ReturnCommandParameter), typeof(object), typeof(SpineEntryRow), null);

    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.Create(
        nameof(StyleOptions), typeof(SpineRowStyleOptions), typeof(SpineEntryRow), null,
        propertyChanged: static (b, _, _) => ((SpineEntryRow)b).Repaint());

    /// <summary>What is typed in the field. Binds two-way by default.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Shown while the field is empty; also what a screen reader names the field by.</summary>
    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>The keyboard the field raises: <see cref="Keyboard.Email"/>, <see cref="Keyboard.Numeric"/> and so on.</summary>
    public Keyboard Keyboard
    {
        get => (Keyboard)GetValue(KeyboardProperty);
        set => SetValue(KeyboardProperty, value);
    }

    /// <summary>
    /// Hides what is typed, with an eye button at the end of the row that shows it (<see cref="IsPasswordShown"/>) and a
    /// screen-reader name, Spine's localised Show password or Hide password.
    /// </summary>
    public bool IsPassword
    {
        get => (bool)GetValue(IsPasswordProperty);
        set => SetValue(IsPasswordProperty, value);
    }

    /// <summary>
    /// With <see cref="IsPassword"/>, whether the password is shown: the eye button flips it. Set it back to
    /// <see langword="false"/> to hide the password again, for example after signing in.
    /// </summary>
    public bool IsPasswordShown
    {
        get => (bool)GetValue(IsPasswordShownProperty);
        set => SetValue(IsPasswordShownProperty, value);
    }

    /// <summary>Whether misspelt words are marked. Turn it off for addresses, user names and codes.</summary>
    public bool IsSpellCheckEnabled
    {
        get => (bool)GetValue(IsSpellCheckEnabledProperty);
        set => SetValue(IsSpellCheckEnabledProperty, value);
    }

    /// <summary>Whether the keyboard suggests and corrects words. Turn it off for addresses, user names and codes.</summary>
    public bool IsTextPredictionEnabled
    {
        get => (bool)GetValue(IsTextPredictionEnabledProperty);
        set => SetValue(IsTextPredictionEnabledProperty, value);
    }

    /// <summary>The most characters the field takes.</summary>
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>What the keyboard's return key says: <see cref="ReturnType.Next"/>, <see cref="ReturnType.Go"/> and so on.</summary>
    public ReturnType ReturnType
    {
        get => (ReturnType)GetValue(ReturnTypeProperty);
        set => SetValue(ReturnTypeProperty, value);
    }

    /// <summary>The command the keyboard's return key runs.</summary>
    public ICommand? ReturnCommand
    {
        get => (ICommand?)GetValue(ReturnCommandProperty);
        set => SetValue(ReturnCommandProperty, value);
    }

    public object? ReturnCommandParameter
    {
        get => GetValue(ReturnCommandParameterProperty);
        set => SetValue(ReturnCommandParameterProperty, value);
    }

    public SpineRowStyleOptions? StyleOptions
    {
        get => (SpineRowStyleOptions?)GetValue(StyleOptionsProperty);
        set => SetValue(StyleOptionsProperty, value);
    }

    /// <summary>Raised when the return key is pressed, after <see cref="ReturnCommand"/>.</summary>
    public event EventHandler? Completed;

    // The eye glyphs, in Spine's icon style, embedded in this package and prefixed so an app's own icons never shadow them.
    const string EyeIcon = "spineroweye.svg";
    const string EyeOffIcon = "spineroweyeoff.svg";

    readonly Grid _grid;
    readonly Entry _entry;
    readonly ImageButton _eye;

    static SpineEntryRow() => RowsStrings.EnsureRegistered();

    public SpineEntryRow()
    {
        _entry = new Entry { VerticalOptions = LayoutOptions.Fill, VerticalTextAlignment = TextAlignment.Center };
        _entry.SetBinding(Entry.TextProperty, new Binding(nameof(Text), BindingMode.TwoWay, source: this));
        _entry.SetBinding(Entry.PlaceholderProperty, new Binding(nameof(Placeholder), source: this));
        _entry.SetBinding(InputView.KeyboardProperty, new Binding(nameof(Keyboard), source: this));
        _entry.SetBinding(InputView.IsSpellCheckEnabledProperty, new Binding(nameof(IsSpellCheckEnabled), source: this));
        _entry.SetBinding(InputView.IsTextPredictionEnabledProperty, new Binding(nameof(IsTextPredictionEnabled), source: this));
        _entry.SetBinding(InputView.MaxLengthProperty, new Binding(nameof(MaxLength), source: this));
        _entry.SetBinding(Entry.ReturnTypeProperty, new Binding(nameof(ReturnType), source: this));
        _entry.SetBinding(Entry.ReturnCommandProperty, new Binding(nameof(ReturnCommand), source: this));
        _entry.SetBinding(Entry.ReturnCommandParameterProperty, new Binding(nameof(ReturnCommandParameter), source: this));
        _entry.Completed += (_, _) => Completed?.Invoke(this, EventArgs.Empty);
        _entry.HandlerChanged += (_, _) => RemovePlatformFrame();

        _eye = new ImageButton
        {
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 10,
            BackgroundColor = Colors.Transparent,
            Command = new Command(() => IsPasswordShown = !IsPasswordShown),
        };

        _grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { _entry, _eye },
        };
        Grid.SetColumn(_eye, 1);

        Content = _grid;

        ApplyPassword();
        Repaint();
        SpineTheme.Track(this, Repaint);
    }

    /// <summary>Puts the cursor in the field and raises the keyboard.</summary>
    public bool FocusField() => _entry.Focus();

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == IsEnabledProperty.PropertyName)
            _grid.Opacity = IsEnabled ? 1 : SpineRowStyleOptions.Resolve(StyleOptions).DisabledOpacity;
    }

    void ApplyPassword()
    {
        _entry.IsPassword = IsPassword && !IsPasswordShown;
        _eye.IsVisible = IsPassword;
        SvgImageSource.SetSvg(_eye, IsPasswordShown ? EyeOffIcon : EyeIcon);
        SemanticProperties.SetDescription(_eye, SpineStrings.Current[IsPasswordShown ? "Spine.Row.HidePassword" : "Spine.Row.ShowPassword"]);
    }

    // The section's group is the frame: the platform's own border, underline or box would draw a second one inside it.
    void RemovePlatformFrame()
    {
#if IOS || MACCATALYST
        if (_entry.Handler?.PlatformView is UIKit.UITextField field)
        {
            field.BorderStyle = UIKit.UITextBorderStyle.None;
            field.BackgroundColor = UIKit.UIColor.Clear;
            // The Mac draws a focus ring round the field, outside the row.
            field.FocusEffect = null;
        }
#elif ANDROID
        if (_entry.Handler?.PlatformView is Android.Widget.EditText text)
        {
            text.Background = null;
            text.SetPadding(0, 0, 0, 0);
        }
#elif WINDOWS
        if (_entry.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox box)
        {
            var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            box.Padding = new Microsoft.UI.Xaml.Thickness(0);
            box.Background = clear;
            // The focused and pointer-over states draw their own box and underline from these resources.
            foreach (var key in new[] { "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
                box.Resources[key] = clear;
            box.Resources["TextControlBorderThemeThicknessFocused"] = new Microsoft.UI.Xaml.Thickness(0);
        }
#endif
    }

    void Repaint()
    {
        var style = SpineRowStyleOptions.Resolve(StyleOptions);

        // No vertical padding: the field fills the row, so a tap anywhere on it lands in the field.
        _grid.Padding = new Thickness(style.Padding.Left, 0, style.Padding.Right, 0);
        _grid.MinimumHeightRequest = style.MinimumHeight;
        _grid.Opacity = IsEnabled ? 1 : style.DisabledOpacity;
        // The glyph's own margin, taken back so it sits at the row's padding like a chevron.
        _eye.Margin = new Thickness(style.Spacing - 10, 0, -10, 0);

        _entry.TextColor = style.TitleColor;
        _entry.PlaceholderColor = style.DetailColor;
        _entry.FontSize = style.TitleFontSize;
        _entry.FontFamily = style.FontFamily;

        SvgImageSource.SetTintColor(_eye, style.DetailColor!);
    }
}
