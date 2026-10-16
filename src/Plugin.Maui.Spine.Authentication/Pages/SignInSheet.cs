using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Presentation;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>
/// The package's sign-in sheet: a button per provider (Apple, Google, Microsoft and the app's own) in one shape with each
/// brand's logo and colours, then email and password with steps for a new
/// account, the emailed code and a forgotten password. Returns the <see cref="SignInResult"/>; closing it cancels.
/// <see cref="ISpineAuth.SignInAsync(CancellationToken)"/> opens it.
/// </summary>
[NavigableSheet(AllowedDetents = [SheetDetent.Medium, SheetDetent.FullScreen])]
public sealed class SignInSheet : SpinePage<SignInSheetViewModel>, INavigableWithResult<SignInResult>
{
    /// <summary>Builds the sheet.</summary>
    public SignInSheet()
    {
        var apple = ProviderButton(string.Format(AuthStrings.Get("ContinueWith"), "Apple"), ProviderLook.Apple, null);
        apple.SetBinding(Button.CommandProperty, nameof(SignInSheetViewModel.AppleCommand));
        var appleRow = Row(apple, ProviderLook.Apple);
        var rows = new List<(string Provider, View Row)> { ("apple", appleRow) };

        // One button per provider the server brokers through the browser, in the order the app added them.
        var browserProviders = new VerticalStackLayout { Spacing = 12 };
        if (BindingContext is SignInSheetViewModel { BrowserProviders: var providers } model)
        {
            foreach (var (provider, displayName) in providers)
            {
                var look = ProviderLook.For(provider);
                var button = ProviderButton(string.Format(AuthStrings.Get("ContinueWith"), displayName), look, provider);
                button.SetBinding(Button.CommandProperty, nameof(SignInSheetViewModel.ProviderCommand));
                var row = Row(button, look);
                browserProviders.Children.Add(row);
                rows.Add((provider, row));
            }

            // Which buttons show depends on the step and, on "I've signed in before", on the provider being added.
            void Show()
            {
                foreach (var (provider, row) in rows)
                    row.IsVisible = model.ShowsProvider(provider);
            }
            Show();
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SignInSheetViewModel.Step)) Show();
            };
        }

        // "Is this your first time here?" after a provider account the server does not know yet.
        var createNew = new Button { Text = AuthStrings.Get("Choose.Create"), FontAttributes = FontAttributes.Bold, CornerRadius = 8, HeightRequest = 48 };
        createNew.SetBinding(Button.CommandProperty, nameof(SignInSheetViewModel.CreateNewAccountCommand));
        var signedInBefore = ProviderButton(AuthStrings.Get("Choose.Existing"), ProviderLook.For(""), null);
        signedInBefore.Padding = new Thickness(16, 0);
        signedInBefore.SetBinding(Button.CommandProperty, nameof(SignInSheetViewModel.SignedInBeforeCommand));
        var choose = new VerticalStackLayout { Spacing = 12, Children = { createNew, signedInBefore } };
        choose.SetBinding(IsVisibleProperty, static (SignInSheetViewModel vm) => vm.ShowChoice);

        var emailButton = ProviderButton(AuthStrings.Get("EmailButton"), ProviderLook.Email, null);
        emailButton.SetBinding(Button.CommandProperty, nameof(SignInSheetViewModel.ShowEmailFieldsCommand));
        var emailRow = Row(emailButton, ProviderLook.Email);
        emailRow.SetBinding(IsVisibleProperty, static (SignInSheetViewModel vm) => vm.ShowEmailButton);

        var or = new Label { Text = AuthStrings.Get("Or"), HorizontalOptions = LayoutOptions.Center, Opacity = 0.6 };
        or.SetBinding(IsVisibleProperty, static (SignInSheetViewModel vm) => vm.ShowOr);

        var email = Field(AuthStrings.Get("Email"), nameof(SignInSheetViewModel.ShowEmail), out var emailEntry);
        emailEntry.Keyboard = Keyboard.Email;
        emailEntry.SetBinding(Entry.TextProperty, static (SignInSheetViewModel vm) => vm.Email, BindingMode.TwoWay);

        var name = Field(AuthStrings.Get("Name"), nameof(SignInSheetViewModel.ShowName), out var nameEntry);
        nameEntry.SetBinding(Entry.TextProperty, static (SignInSheetViewModel vm) => vm.DisplayName, BindingMode.TwoWay);

        var code = Field(AuthStrings.Get("Code"), nameof(SignInSheetViewModel.ShowCode), out var codeEntry);
        codeEntry.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeCharacter);
        codeEntry.SetBinding(Entry.TextProperty, static (SignInSheetViewModel vm) => vm.Code, BindingMode.TwoWay);

        var password = Field(AuthStrings.Get("Password"), nameof(SignInSheetViewModel.ShowPassword), out var passwordEntry);
        passwordEntry.IsPassword = true;
        passwordEntry.SetBinding(Entry.TextProperty, static (SignInSheetViewModel vm) => vm.Password, BindingMode.TwoWay);
        passwordEntry.SetBinding(Entry.PlaceholderProperty, static (SignInSheetViewModel vm) => vm.PasswordLabel);

        var message = new Label { FontSize = 14 };
        message.SetBinding(Label.TextProperty, static (SignInSheetViewModel vm) => vm.Message);
        message.SetBinding(IsVisibleProperty, static (SignInSheetViewModel vm) => vm.HasMessage);

        var error = new Label { FontSize = 14, TextColor = Colors.Red };
        error.SetBinding(Label.TextProperty, static (SignInSheetViewModel vm) => vm.Error);
        error.SetBinding(IsVisibleProperty, static (SignInSheetViewModel vm) => vm.HasError);

        var links = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                Link(AuthStrings.Get("CreateAccount"), nameof(SignInSheetViewModel.CreateAccountCommand), nameof(SignInSheetViewModel.ShowCreateAccount)),
                Link(AuthStrings.Get("Forgot"), nameof(SignInSheetViewModel.ForgotCommand), nameof(SignInSheetViewModel.ShowForgot)),
                Link(AuthStrings.Get("Resend"), nameof(SignInSheetViewModel.ResendCommand), nameof(SignInSheetViewModel.ShowResend)),
                Link(AuthStrings.Get("BackToSignIn"), nameof(SignInSheetViewModel.BackCommand), nameof(SignInSheetViewModel.ShowBack)),
            },
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(HeaderBarConstants.PageMargin, 8, HeaderBarConstants.PageMargin, 16),
                Spacing = 12,
                Children = { message, choose, appleRow, browserProviders, emailRow, or, email, name, code, password, error, links },
            },
        };

        var proceed = new Button { Text = AuthStrings.Get("Continue"), Margin = new Thickness(0, 12) };
        proceed.SetBinding(Button.CommandProperty, nameof(SignInSheetViewModel.ContinueCommand));
        proceed.SetBinding(IsVisibleProperty, static (SignInSheetViewModel vm) => vm.ShowEmail);
        Footer = new ContentView { Padding = HeaderBarConstants.PagePadding, Content = proceed };
    }

    // The providers' buttons share one shape: 48 high, radius 8, the logo 20 wide on the left, the text centred. The
    // colours are each brand's own for the light and dark theme: Apple black or white (HIG), Google's and Microsoft's
    // from their sign-in branding, and email in Google's neutral outline so it sits with them.
    private static Button ProviderButton(string text, ProviderLook look, string? parameter)
    {
        var button = new Button
        {
            Text = text,
            CommandParameter = parameter,
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            CornerRadius = 8,
            HeightRequest = 48,
            Padding = new Thickness(48, 0),
        };
        button.SetAppThemeColor(Button.BackgroundColorProperty, look.Light.Background, look.Dark.Background);
        button.SetAppThemeColor(Button.TextColorProperty, look.Light.Text, look.Dark.Text);
        button.SetAppThemeColor(Button.BorderColorProperty, look.Light.Border ?? look.Light.Background, look.Dark.Border ?? look.Dark.Background);
        button.BorderWidth = look.Light.Border is null && look.Dark.Border is null ? 0 : 1;
        return button;
    }

    private static View Row(Button button, ProviderLook look)
    {
        var row = new Grid { Children = { button } };
        if (look.Logo is null)
            return row;

        var logo = new Image { WidthRequest = 20, HeightRequest = 20, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(16, 0, 0, 0), InputTransparent = true };
        SvgImageSource.SetEnableSvg(logo, true);
        SvgImageSource.SetPadding(logo, new Thickness(0));
        SvgImageSource.SetAdjustColorsForDark(logo, false);
        if (look.TintsLogo)
        {
            SvgImageSource.SetLightTintColor(logo, look.Light.Text);
            SvgImageSource.SetDarkTintColor(logo, look.Dark.Text);
        }
        else
        {
            SvgImageSource.SetTintColor(logo, Colors.Transparent);
        }
        SvgImageSource.SetSvg(logo, look.Logo);
        row.Children.Add(logo);
        return row;
    }

    private static View Field(string label, string visible, out Entry entry)
    {
        entry = new Entry { Placeholder = label };
        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { new Label { Text = label, FontAttributes = FontAttributes.Bold }, entry },
        };
        stack.SetBinding(IsVisibleProperty, visible);
        return stack;
    }

    private static View Link(string text, string command, string visible)
    {
        var button = new Button { Text = text, BackgroundColor = Colors.Transparent, Padding = 0, HorizontalOptions = LayoutOptions.Start };
        button.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#0A66C2"), Color.FromArgb("#64A8F0"));
        button.SetBinding(Button.CommandProperty, command);
        button.SetBinding(IsVisibleProperty, visible);
        return button;
    }
}

internal sealed record ProviderColors(Color Background, Color Text, Color? Border);

/// <summary>How a provider's sign-in button looks: its logo, and its colours in the light and dark theme.</summary>
internal sealed record ProviderLook(string? Logo, bool TintsLogo, ProviderColors Light, ProviderColors Dark)
{
    private static readonly ProviderColors NeutralLight = new(Colors.White, Color.FromArgb("#1F1F1F"), Color.FromArgb("#747775"));
    private static readonly ProviderColors NeutralDark = new(Color.FromArgb("#131314"), Color.FromArgb("#E3E3E3"), Color.FromArgb("#8E918F"));

    public static ProviderLook Apple { get; } = new("signin-apple.svg", true, new(Colors.Black, Colors.White, null), new(Colors.White, Colors.Black, null));

    public static ProviderLook Google { get; } = new("signin-google.svg", false, NeutralLight, NeutralDark);

    public static ProviderLook Microsoft { get; } = new("signin-microsoft.svg", false,
        new(Colors.White, Color.FromArgb("#5E5E5E"), Color.FromArgb("#8C8C8C")), new(Color.FromArgb("#2F2F2F"), Colors.White, null));

    public static ProviderLook Email { get; } = new("signin-password.svg", true, NeutralLight, NeutralDark);

    /// <summary>A provider by its name; one Spine has no logo for gets the neutral outline without one.</summary>
    public static ProviderLook For(string provider) => provider.ToLowerInvariant() switch
    {
        "apple" => Apple,
        "google" => Google,
        "microsoft" => Microsoft,
        _ => new(null, false, NeutralLight, NeutralDark),
    };
}

/// <summary>The steps of <see cref="SignInSheet"/>.</summary>
public enum SignInStep
{
    /// <summary>Email and password, and Apple.</summary>
    SignIn,

    /// <summary>A new account.</summary>
    SignUp,

    /// <summary>The code from the verification email.</summary>
    Code,

    /// <summary>The email to send a reset code to.</summary>
    Forgot,

    /// <summary>The reset code and a new password.</summary>
    Reset,

    /// <summary>A provider account the server does not know: a new account, or one the user had before.</summary>
    Choose,

    /// <summary>"I've signed in before": the other ways to sign in, and the pending account is added after.</summary>
    Prove,
}

/// <summary>The view model of <see cref="SignInSheet"/>.</summary>
public sealed partial class SignInSheetViewModel : ViewModelBase
{
    private readonly ISpineAuth _auth;
    private readonly SpineAuthenticationOptions _options;
    private readonly INavigationService _navigation;

    /// <summary>Starts at <see cref="SignInStep.SignIn"/>.</summary>
    public SignInSheetViewModel(ISpineAuth auth, SpineAuthenticationOptions options, INavigationService navigation)
    {
        _auth = auth;
        _options = options;
        _navigation = navigation;
        Title = AuthStrings.Get("Title.SignIn");
    }

    /// <summary>The step on screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowApple), nameof(ShowChoice), nameof(ShowProviders), nameof(ShowEmailButton), nameof(ShowOr), nameof(ShowEmail), nameof(ShowName), nameof(ShowCode), nameof(ShowPassword),
        nameof(PasswordLabel), nameof(ShowCreateAccount), nameof(ShowForgot), nameof(ShowResend), nameof(ShowBack))]
    public partial SignInStep Step { get; set; }

    /// <summary>The email.</summary>
    [ObservableProperty]
    public partial string Email { get; set; } = "";

    /// <summary>The password, or the new password on <see cref="SignInStep.Reset"/>.</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = "";

    /// <summary>The name for a new account.</summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; } = "";

    /// <summary>The emailed code.</summary>
    [ObservableProperty]
    public partial string Code { get; set; } = "";

    /// <summary>What the last step said, such as where a code went.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    /// <summary>Why the last step failed, for the user.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    // The provider account the server asked about, while the user chooses (SignInStep.Choose and Prove).
    private SpinePendingIdentity? _pending;

    // "I've signed in before" was chosen: the next sign-in, through any step (an emailed code too), adds _pending.
    private bool _proving;

    private bool SigningIn => Step is SignInStep.SignIn or SignInStep.Prove;

    /// <summary>Apple's button shows: its own sheet on Apple platforms, the browser through the server elsewhere.</summary>
    public bool ShowApple => ShowsProvider("apple");

    /// <summary>
    /// A provider's button shows: on the first step, and on "I've signed in before" unless it is the provider being added.
    /// </summary>
    /// <param name="provider">The provider's name.</param>
    public bool ShowsProvider(string provider) =>
        SigningIn && (provider != "apple" || _options.Apple) && !(Step == SignInStep.Prove && provider == _pending?.Provider);

    /// <summary>"Create a new account" and "I've signed in before" show.</summary>
    public bool ShowChoice => Step == SignInStep.Choose;

    /// <summary>The providers the server brokers through the browser, as (name, display name).</summary>
    public IReadOnlyList<(string Name, string DisplayName)> BrowserProviders =>
        _options.BrowserProviders.Select(p => (p.Name, _options.DisplayNames.GetValueOrDefault(p.Name, p.Name))).ToList();

    /// <summary>The browser providers' buttons show.</summary>
    public bool ShowProviders => SigningIn && _options.BrowserProviders.Count > 0;

    /// <summary>The fields were opened with "Continue with email".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmailButton), nameof(ShowOr), nameof(ShowEmail), nameof(ShowName), nameof(ShowCode), nameof(ShowPassword),
        nameof(ShowCreateAccount), nameof(ShowForgot))]
    public partial bool EmailOpened { get; set; }

    private bool HasProviderButtons => _options.Apple || _options.BrowserProviders.Count > 0;

    /// <summary>"Continue with email" shows: next to the providers' buttons, until it opens the fields.</summary>
    public bool ShowEmailButton => SigningIn && _options.EmailPassword is not null && HasProviderButtons && !EmailOpened;

    /// <summary>The "or" between the providers and the fields shows.</summary>
    public bool ShowOr => SigningIn && (ShowApple || ShowProviders) && ShowEmail;

    /// <summary>The email field shows: at once without providers, after "Continue with email" with them.</summary>
    public bool ShowEmail => _options.EmailPassword is not null && Step != SignInStep.Choose && (EmailOpened || !HasProviderButtons || !SigningIn);

    /// <summary>The name field shows.</summary>
    public bool ShowName => ShowEmail && Step == SignInStep.SignUp;

    /// <summary>The code field shows.</summary>
    public bool ShowCode => ShowEmail && Step is SignInStep.Code or SignInStep.Reset;

    /// <summary>The password field shows.</summary>
    public bool ShowPassword => ShowEmail && Step is SignInStep.SignIn or SignInStep.SignUp or SignInStep.Reset or SignInStep.Prove;

    /// <summary>"Password", or "New password" on a reset.</summary>
    public string PasswordLabel => AuthStrings.Get(Step == SignInStep.Reset ? "NewPassword" : "Password");

    /// <summary>"Create account" shows.</summary>
    public bool ShowCreateAccount => ShowEmail && Step == SignInStep.SignIn && _options.EmailPassword is { AllowSignUp: true };

    /// <summary>"Forgot password?" shows.</summary>
    public bool ShowForgot => ShowEmail && Step == SignInStep.SignIn && _options.EmailPassword is { AllowReset: true };

    /// <summary>"Send a new code" shows.</summary>
    public bool ShowResend => Step == SignInStep.Code;

    /// <summary>"Back to sign in" shows.</summary>
    public bool ShowBack => Step != SignInStep.SignIn;

    /// <summary>A message shows.</summary>
    public bool HasMessage => !string.IsNullOrEmpty(Message);

    /// <summary>An error shows.</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    partial void OnStepChanged(SignInStep value)
    {
        Title = AuthStrings.Get(value switch
        {
            SignInStep.SignUp => "Title.SignUp",
            SignInStep.Code => "Title.Code",
            SignInStep.Forgot or SignInStep.Reset => "Title.Forgot",
            SignInStep.Choose => "Title.Choose",
            SignInStep.Prove => "Title.Prove",
            _ => "Title.SignIn",
        });
        Error = null;
    }

    [RelayCommand]
    private async Task Apple() => await FinishAsync(await _auth.SignInAsync("apple"));

    [RelayCommand]
    private async Task Provider(string name) => await FinishAsync(await _auth.SignInAsync(name));

    [RelayCommand]
    private async Task Continue()
    {
        Error = null;
        var email = Email.Trim();

        var result = Step switch
        {
            SignInStep.SignIn or SignInStep.Prove => await _auth.SignInWithPasswordAsync(email, Password),
            SignInStep.SignUp => await _auth.SignUpAsync(email, Password, string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim()),
            SignInStep.Code => await _auth.VerifyEmailAsync(email, Code),
            SignInStep.Forgot => await _auth.ForgotPasswordAsync(email),
            _ => await _auth.ResetPasswordAsync(email, Code, Password),
        };

        if (result.Status == SignInStatus.CodeSent)
        {
            Code = "";
            Message = string.Format(AuthStrings.Get(Step == SignInStep.Forgot ? "ResetSent" : "CodeSent"), email);
            if (Step == SignInStep.Forgot) Password = "";
            Step = Step == SignInStep.Forgot ? SignInStep.Reset : SignInStep.Code;
            Error = null;
            return;
        }

        await FinishAsync(result);
    }

    [RelayCommand]
    private Task ShowEmailFields()
    {
        EmailOpened = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task CreateAccount()
    {
        Message = null;
        Step = SignInStep.SignUp;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task Forgot()
    {
        Message = null;
        Step = SignInStep.Forgot;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Resend()
    {
        await _auth.ResendCodeAsync(Email.Trim());
        Message = string.Format(AuthStrings.Get("CodeSent"), Email.Trim());
    }

    [RelayCommand]
    private Task Back()
    {
        Code = "";
        if (Step == SignInStep.Prove)
        {
            Choose(_pending!);
            return Task.CompletedTask;
        }

        Message = null;
        _pending = null;
        _proving = false;
        Step = SignInStep.SignIn;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CreateNewAccount()
    {
        if (_pending is not { } pending) return;
        await FinishAsync(await _auth.CreateAccountAsync(pending));
    }

    [RelayCommand]
    private Task SignedInBefore()
    {
        _proving = true;
        Message = string.Format(AuthStrings.Get("Prove.Message"), ProviderName(_pending!.Provider));
        Step = SignInStep.Prove;
        return Task.CompletedTask;
    }

    private void Choose(SpinePendingIdentity pending)
    {
        _pending = pending;
        _proving = false;
        var account = pending.Email ?? pending.DisplayName;
        Message = string.Format(AuthStrings.Get("Choose.Message"), ProviderName(pending.Provider))
            + (account is null ? "" : $"\n{account}");
        Step = SignInStep.Choose;
    }

    private string ProviderName(string provider) =>
        provider == "apple" ? "Apple" : _options.DisplayNames.GetValueOrDefault(provider, provider);

    private async Task FinishAsync(SignInResult result)
    {
        switch (result.Status)
        {
            // "I've signed in before": the user is in the account they had, which now takes the pending one. If that
            // fails, they are still signed in to it, so the sheet closes with the sign-in and the reason in Error.
            case SignInStatus.SignedIn when _proving && _pending is { } pending:
                var linked = await _auth.LinkPendingAsync(pending);
                await _navigation.ReturnAsync(linked.Status == SignInStatus.SignedIn ? linked : result with { Error = linked.Error });
                break;
            case SignInStatus.SignedIn:
                await _navigation.ReturnAsync(result);
                break;
            case SignInStatus.ChooseAccount when _proving:
                Choose(_pending!);
                Error = AuthStrings.Get("AlsoNew");
                break;
            case SignInStatus.ChooseAccount:
                Choose(result.Pending!);
                break;
            case SignInStatus.Failed:
                Error = AuthStrings.Error(result.Error);
                break;
        }
    }
}
