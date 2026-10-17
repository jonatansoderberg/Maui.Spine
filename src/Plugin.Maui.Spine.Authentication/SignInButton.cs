using System.Windows.Input;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// A provider's button in the shape the sign-in sheet uses: 48 high, radius 8, the provider's logo on the left and the
/// text centred, in the provider's own colours for the light and dark theme. Apple, Google and Microsoft get their logos
/// and brand colours (Apple black or white, Google white or #131314 with its outline, Microsoft white or #2F2F2F),
/// <c>password</c> and <c>passkey</c> their symbols in the neutral outline, and any other provider the neutral outline without a logo.
/// </summary>
/// <example>
/// <code>
/// &lt;SignInButton Provider="google" Command="{Binding SignInCommand}" CommandParameter="google" /&gt;
/// &lt;SignInButton Provider="microsoft" Text="Link Microsoft" Command="{Binding LinkCommand}" CommandParameter="microsoft" /&gt;
/// </code>
/// </example>
public sealed class SignInButton : ContentView
{
    /// <summary>Identifies <see cref="Provider"/>.</summary>
    public static readonly BindableProperty ProviderProperty = BindableProperty.Create(nameof(Provider), typeof(string), typeof(SignInButton), "",
        propertyChanged: static (b, _, _) => ((SignInButton)b).Apply());

    /// <summary>Identifies <see cref="Text"/>.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(SignInButton), null,
        propertyChanged: static (b, _, _) => ((SignInButton)b).Apply());

    /// <summary>Identifies <see cref="Command"/>.</summary>
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SignInButton), null,
        propertyChanged: static (b, _, value) => ((SignInButton)b)._button.Command = (ICommand?)value);

    /// <summary>Identifies <see cref="CommandParameter"/>.</summary>
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(SignInButton), null,
        propertyChanged: static (b, _, value) => ((SignInButton)b)._button.CommandParameter = value);

    private readonly Button _button;
    private readonly Image _logo;

    /// <summary>Builds the button; set <see cref="Provider"/>.</summary>
    public SignInButton()
    {
        _button = new Button { FontAttributes = FontAttributes.Bold, FontSize = 16, CornerRadius = 8, HeightRequest = 48 };
        _button.Clicked += (_, e) => Clicked?.Invoke(this, e);

        _logo = new Image
        {
            WidthRequest = 20,
            HeightRequest = 20,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(16, 0, 0, 0),
            InputTransparent = true,
        };
        AutomationProperties.SetIsInAccessibleTree(_logo, false);
        SvgImageSource.SetEnableSvg(_logo, true);
        SvgImageSource.SetPadding(_logo, new Thickness(0));
        SvgImageSource.SetAdjustColorsForDark(_logo, false);

        Content = new Grid { Children = { _button, _logo } };
        Apply();
    }

    /// <summary>The provider's name, such as <c>apple</c>, <c>google</c>, <c>microsoft</c>, <c>password</c> or <c>passkey</c>; it picks the logo and colours.</summary>
    public string Provider
    {
        get => (string)GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    /// <summary>The text; "Continue with Apple" and the like ("Continue with email" for <c>password</c>, "Sign in with a passkey" for <c>passkey</c>) when not set.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Runs when the button is tapped.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>Passed to <see cref="Command"/>.</summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Raised when the button is tapped.</summary>
    public event EventHandler? Clicked;

    private void Apply()
    {
        var provider = (Provider ?? "").ToLowerInvariant();
        var look = ProviderLook.For(provider);

        _button.Text = Text ?? provider switch
        {
            "password" or "email" => AuthStrings.Get("EmailButton"),
            "passkey" => AuthStrings.Get("PasskeyButton"),
            _ => string.Format(AuthStrings.Get("ContinueWith"), ProviderLook.BrandName(provider)),
        };
        _button.Padding = new Thickness(look.Logo is null ? 16 : 48, 0);
        _button.SetAppThemeColor(Button.BackgroundColorProperty, look.Light.Background, look.Dark.Background);
        _button.SetAppThemeColor(Button.TextColorProperty, look.Light.Text, look.Dark.Text);
        _button.SetAppThemeColor(Button.BorderColorProperty, look.Light.Border ?? look.Light.Background, look.Dark.Border ?? look.Dark.Background);
        _button.BorderWidth = look.Light.Border is null && look.Dark.Border is null ? 0 : 1;

        _logo.IsVisible = look.Logo is not null;
        if (look.Logo is null)
            return;

        if (look.TintsLogo)
        {
            SvgImageSource.SetTintColor(_logo, null!);
            SvgImageSource.SetLightTintColor(_logo, look.Light.Text);
            SvgImageSource.SetDarkTintColor(_logo, look.Dark.Text);
        }
        else
        {
            SvgImageSource.SetTintColor(_logo, Colors.Transparent);
        }
        SvgImageSource.SetSvg(_logo, look.Logo);
    }
}

internal sealed record ProviderColors(Color Background, Color Text, Color? Border);

/// <summary>
/// How a provider's button looks: its logo, and its colours in the light and dark theme. Apple black or white (HIG),
/// Google's and Microsoft's from their sign-in branding, and email in Google's neutral outline so it sits with them.
/// </summary>
internal sealed record ProviderLook(string? Logo, bool TintsLogo, ProviderColors Light, ProviderColors Dark)
{
    private static readonly ProviderColors NeutralLight = new(Colors.White, Color.FromArgb("#1F1F1F"), Color.FromArgb("#747775"));
    private static readonly ProviderColors NeutralDark = new(Color.FromArgb("#131314"), Color.FromArgb("#E3E3E3"), Color.FromArgb("#8E918F"));

    public static ProviderLook Apple { get; } = new("signin-apple.svg", true, new(Colors.Black, Colors.White, null), new(Colors.White, Colors.Black, null));

    public static ProviderLook Google { get; } = new("signin-google.svg", false, NeutralLight, NeutralDark);

    public static ProviderLook Microsoft { get; } = new("signin-microsoft.svg", false,
        new(Colors.White, Color.FromArgb("#1F1F1F"), Color.FromArgb("#8C8C8C")), new(Color.FromArgb("#2F2F2F"), Colors.White, null));

    public static ProviderLook Email { get; } = new("signin-password.svg", true, NeutralLight, NeutralDark);

    public static ProviderLook Passkey { get; } = new("signin-passkey.svg", true, NeutralLight, NeutralDark);

    public static ProviderLook Neutral { get; } = new(null, false, NeutralLight, NeutralDark);

    /// <summary>A provider by its name; one Spine has no logo for gets the neutral outline without one.</summary>
    public static ProviderLook For(string provider) => provider.ToLowerInvariant() switch
    {
        "apple" => Apple,
        "google" => Google,
        "microsoft" => Microsoft,
        "password" or "email" => Email,
        "passkey" => Passkey,
        _ => Neutral,
    };

    /// <summary>How the brand writes its own name; another provider's name as it is.</summary>
    public static string BrandName(string provider) => provider.ToLowerInvariant() switch
    {
        "apple" => "Apple",
        "google" => "Google",
        "microsoft" => "Microsoft",
        _ => provider,
    };
}
