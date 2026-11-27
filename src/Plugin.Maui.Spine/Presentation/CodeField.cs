using System.Text;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>How a <see cref="CodeField"/> looks: being typed, being checked, accepted or turned down.</summary>
public enum CodeFieldState
{
    /// <summary>Being typed.</summary>
    Editing,

    /// <summary>Sent and waiting for an answer: the cells pulse and take no typing.</summary>
    Checking,

    /// <summary>Accepted: the cells light up in the accent one after the other, with a success haptic.</summary>
    Accepted,

    /// <summary>Turned down: the row shakes and the cells turn red, with an error haptic. The next edit is the app's to clear.</summary>
    Rejected,
}

/// <summary>
/// A short code typed by hand into one cell per character, in groups with a dash between them as it is printed or sent:
/// <c>HM7K-Q2X9-PD4M</c>, an emailed <c>KMZG-HR4T</c>, a one-time <c>482913</c>.
/// <para>
/// One hidden <see cref="Entry"/> owns the keyboard and the text, so backspace, a hardware keyboard and the system's own
/// editing work as in any field; the cells only draw it. Each typed character is upper-cased, run through
/// <see cref="Substitutions"/> and kept only if <see cref="Alphabet"/> has it; one that is not shakes its cell. Tapping a
/// cell puts the caret there, and a character typed inside the code replaces the one under the caret, so a misread
/// character is fixed with one tap and one key. An alphabet of digits only gets the numeric keyboard and, on iOS, the
/// one-time-code suggestion from Messages and Mail.
/// </para>
/// <para>
/// <see cref="CompletedCommand"/> runs when typing fills the last cell, or changes a full code; a code set through
/// <see cref="Code"/> from outside (a scan, a paste, a link) fills the cells one after the other and leaves submitting to
/// whoever set it. <see cref="State"/> drives the feedback (<see cref="CodeFieldState"/>), and <see cref="IsActive"/> the
/// keyboard.
/// </para>
/// </summary>
/// <example>
/// <code>
/// &lt;CodeField Length="12" GroupSize="4" Alphabet="0123456789ABCDEFGHJKMNPQRSTVWXYZ" Substitutions="O0 I1 L1"
///            Code="{Binding Code}" State="{Binding CodeState}" IsActive="{Binding IsTyping}"
///            CompletedCommand="{Binding RedeemCommand}" /&gt;
/// </code>
/// </example>
public sealed class CodeField : ContentView
{
    /// <summary>The digits, <see cref="Alphabet"/>'s default.</summary>
    public const string Digits = "0123456789";

    private const string PulseAnimation = "Spine.CodeField.Pulse";
    private const double MaxCellWidth = 48;

    /// <summary>Identifies the <see cref="Code"/> bindable property.</summary>
    public static readonly BindableProperty CodeProperty = BindableProperty.Create(
        nameof(Code), typeof(string), typeof(CodeField), "", BindingMode.TwoWay,
        propertyChanged: (b, o, n) => ((CodeField)b).OnCodeChanged((string?)o ?? "", (string?)n ?? ""));

    /// <summary>Identifies the <see cref="Length"/> bindable property.</summary>
    public static readonly BindableProperty LengthProperty = BindableProperty.Create(
        nameof(Length), typeof(int), typeof(CodeField), 6, validateValue: (_, v) => (int)v is > 0 and <= 32,
        propertyChanged: (b, _, _) => ((CodeField)b).Rebuild());

    /// <summary>Identifies the <see cref="GroupSize"/> bindable property.</summary>
    public static readonly BindableProperty GroupSizeProperty = BindableProperty.Create(
        nameof(GroupSize), typeof(int), typeof(CodeField), 0, validateValue: (_, v) => (int)v >= 0,
        propertyChanged: (b, _, _) => ((CodeField)b).Rebuild());

    /// <summary>Identifies the <see cref="Alphabet"/> bindable property.</summary>
    public static readonly BindableProperty AlphabetProperty = BindableProperty.Create(
        nameof(Alphabet), typeof(string), typeof(CodeField), Digits,
        propertyChanged: (b, _, _) => ((CodeField)b).OnAlphabetChanged());

    /// <summary>Identifies the <see cref="Substitutions"/> bindable property.</summary>
    public static readonly BindableProperty SubstitutionsProperty = BindableProperty.Create(
        nameof(Substitutions), typeof(string), typeof(CodeField), "",
        propertyChanged: (b, _, _) => ((CodeField)b)._map = null);

    /// <summary>Identifies the <see cref="State"/> bindable property.</summary>
    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State), typeof(CodeFieldState), typeof(CodeField), CodeFieldState.Editing,
        propertyChanged: (b, _, n) => ((CodeField)b).OnStateChanged((CodeFieldState)n));

    /// <summary>Identifies the <see cref="IsActive"/> bindable property.</summary>
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive), typeof(bool), typeof(CodeField), false,
        propertyChanged: (b, _, n) => ((CodeField)b).OnIsActiveChanged((bool)n));

    /// <summary>Identifies the <see cref="CompletedCommand"/> bindable property.</summary>
    public static readonly BindableProperty CompletedCommandProperty = BindableProperty.Create(
        nameof(CompletedCommand), typeof(ICommand), typeof(CodeField));

    /// <summary>Identifies the <see cref="AccentColor"/> bindable property.</summary>
    public static readonly BindableProperty AccentColorProperty = BindableProperty.Create(
        nameof(AccentColor), typeof(Color), typeof(CodeField), propertyChanged: (b, _, _) => ((CodeField)b).Update());

    /// <summary>Identifies the <see cref="OnAccentColor"/> bindable property.</summary>
    public static readonly BindableProperty OnAccentColorProperty = BindableProperty.Create(
        nameof(OnAccentColor), typeof(Color), typeof(CodeField), propertyChanged: (b, _, _) => ((CodeField)b).Update());

    /// <summary>Identifies the <see cref="CellColor"/> bindable property.</summary>
    public static readonly BindableProperty CellColorProperty = BindableProperty.Create(
        nameof(CellColor), typeof(Color), typeof(CodeField), propertyChanged: (b, _, _) => ((CodeField)b).Update());

    /// <summary>Identifies the <see cref="TextColor"/> bindable property.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(CodeField), propertyChanged: (b, _, _) => ((CodeField)b).Update());

    /// <summary>Identifies the <see cref="ErrorColor"/> bindable property.</summary>
    public static readonly BindableProperty ErrorColorProperty = BindableProperty.Create(
        nameof(ErrorColor), typeof(Color), typeof(CodeField), propertyChanged: (b, _, _) => ((CodeField)b).Update());

    /// <summary>Identifies the <see cref="FontFamily"/> bindable property.</summary>
    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily), typeof(string), typeof(CodeField), propertyChanged: (b, _, _) => ((CodeField)b).ApplyFont());

    /// <summary>The code as typed so far, without dashes: up to <see cref="Length"/> characters of <see cref="Alphabet"/>.</summary>
    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>How many characters the code has: one cell each. 6 by default.</summary>
    public int Length
    {
        get => (int)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    /// <summary>Characters per group, with a dash between groups; 0 (the default) for one group.</summary>
    public int GroupSize
    {
        get => (int)GetValue(GroupSizeProperty);
        set => SetValue(GroupSizeProperty, value);
    }

    /// <summary>The characters a code can have, upper case; <see cref="Digits"/> by default, which brings the numeric keyboard.</summary>
    public string Alphabet
    {
        get => (string)GetValue(AlphabetProperty);
        set => SetValue(AlphabetProperty, value);
    }

    /// <summary>
    /// Characters read as others, as pairs separated by spaces: <c>"O0 I1 L1"</c> for an alphabet without O, I and L that
    /// forgives them for 0 and 1. Applied after upper-casing.
    /// </summary>
    public string Substitutions
    {
        get => (string)GetValue(SubstitutionsProperty);
        set => SetValue(SubstitutionsProperty, value);
    }

    /// <summary>How the cells look and whether they take typing; the app moves it as the code is checked.</summary>
    public CodeFieldState State
    {
        get => (CodeFieldState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>True: the keyboard is up for the cells. Becoming true raises it, becoming false lowers it.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Run with the code when typing fills the last cell, or changes a full code.</summary>
    public ICommand? CompletedCommand
    {
        get => (ICommand?)GetValue(CompletedCommandProperty);
        set => SetValue(CompletedCommandProperty, value);
    }

    /// <summary>The caret, the cell it is at and the lit cells of an accepted code; the app's <c>Accent</c> resource by default.</summary>
    public Color? AccentColor
    {
        get => (Color?)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    /// <summary>A lit cell's character; black or white, whichever reads on <see cref="AccentColor"/>, by default.</summary>
    public Color? OnAccentColor
    {
        get => (Color?)GetValue(OnAccentColorProperty);
        set => SetValue(OnAccentColorProperty, value);
    }

    /// <summary>A cell's fill; the system's grouped fill by default.</summary>
    public Color? CellColor
    {
        get => (Color?)GetValue(CellColorProperty);
        set => SetValue(CellColorProperty, value);
    }

    /// <summary>The characters; the system label colour by default.</summary>
    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    /// <summary>A turned-down code's cells; the system red by default.</summary>
    public Color? ErrorColor
    {
        get => (Color?)GetValue(ErrorColorProperty);
        set => SetValue(ErrorColorProperty, value);
    }

    /// <summary>The cells' font; the platform's bold by default.</summary>
    public string? FontFamily
    {
        get => (string?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    private readonly Entry _entry;
    private readonly Grid _row;
    private readonly BoxView _caret;
    private readonly BoxView _touch;
    private Border[] _cells = [];
    private Label[] _labels = [];
    private int[] _columns = [];
    private BoxView[] _dashes = [];
    private IDispatcherTimer? _blink;
    private Func<char, char?>? _map;
    private double _maxRowWidth = double.PositiveInfinity;

    private int _caretAt;
    private int _shown = int.MaxValue;
    private int _lit;
    private bool _syncing;
    private bool _focused;

    /// <summary>Six digits in one group, until <see cref="Length"/>, <see cref="GroupSize"/> and <see cref="Alphabet"/> say otherwise.</summary>
    public CodeField()
    {
        _row = new Grid { ColumnSpacing = 3, HorizontalOptions = LayoutOptions.Center };
        _row.SizeChanged += (_, _) => Fit();

        // Every BoxView here clears its BackgroundColor: an app's implicit BoxView style (the MAUI template has one) would fill it.
        _caret = new BoxView
        {
            BackgroundColor = Colors.Transparent,
            WidthRequest = 2.5,
            CornerRadius = 1.25,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            IsVisible = false,
        };

        // Behind the cells: never seen, never touched, only typed into.
        _entry = new Entry
        {
            Opacity = 0,
            TextColor = Colors.Transparent,
            BackgroundColor = Colors.Transparent,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            ReturnType = ReturnType.Done,
        };
        AutomationProperties.SetIsInAccessibleTree(_entry, false);
        _entry.TextChanged += OnEntryTextChanged;
        _entry.Focused += (_, _) => SetFocused(true);
        _entry.Unfocused += (_, _) => SetFocused(false);
        _entry.Completed += (_, _) => Submit(Code);
#if IOS
        _entry.HandlerChanged += (_, _) => ApplyContentType();
#endif

        // Over everything, so a tap between two cells still lands on the nearest one.
        _touch = new BoxView { Color = Colors.Transparent, BackgroundColor = Colors.Transparent };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) => OnTapped(e.GetPosition(_row));
        _touch.GestureRecognizers.Add(tap);
        SemanticProperties.SetHint(_touch, SpineStrings.Current["Spine.CodeField.Hint"]);

        Content = new Grid { Children = { _entry, _row, _touch } };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        OnAlphabetChanged();
        Rebuild();
    }

    private void Rebuild()
    {
        if (_row is null)
            return;
        var length = Length;
        var group = GroupSize;
        _row.Children.Clear();
        _row.ColumnDefinitions.Clear();
        _cells = new Border[length];
        _labels = new Label[length];
        _columns = new int[length];
        var dashes = new List<BoxView>();
        var column = 0;
        for (var i = 0; i < length; i++)
        {
            if (group > 0 && i > 0 && i % group == 0)
            {
                _row.ColumnDefinitions.Add(new ColumnDefinition(10));
                var dash = new BoxView { BackgroundColor = Colors.Transparent, HeightRequest = 2.5, CornerRadius = 1.25, VerticalOptions = LayoutOptions.Center };
                dashes.Add(dash);
                _row.Add(dash, column++);
            }
            _row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _labels[i] = new Label
            {
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            };
            _cells[i] = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 7 },
                StrokeThickness = 1.5,
                Padding = 0,
                Content = _labels[i],
            };
            _columns[i] = column;
            _row.Add(_cells[i], column++);
        }
        _dashes = [.. dashes];
        _row.Add(_caret, _columns[0]);

        // As wide as the cells need, at most MaxCellWidth each: six digits do not stretch across a tablet.
        _maxRowWidth = length * MaxCellWidth + (length - 1) * _row.ColumnSpacing + _dashes.Length * (10 + _row.ColumnSpacing);
        SizeRow(Width);
        _entry.MaxLength = int.MaxValue;
        if (Code.Length > length)
            Code = Code[..length];
        ApplyFont();
        Fit();
        Update();
    }

    /// <inheritdoc/>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        SizeRow(width);
    }

    // Star columns ask for no width of their own: the row asks for MaxCellWidth a cell, and takes less where there is less.
    private void SizeRow(double width)
    {
        var row = width > 0 ? Math.Min(width, _maxRowWidth) : _maxRowWidth;
        if (row > 0 && Math.Abs(_row.WidthRequest - row) > 0.5)
            _row.WidthRequest = row;
    }

    // A cell about as high as it is wide, the character about two thirds of that: the same room around it every way.
    private void Fit()
    {
        if (_cells.Length == 0 || _row.Width <= 0)
            return;
        var gaps = (_cells.Length - 1) * _row.ColumnSpacing + _dashes.Length * (10 + _row.ColumnSpacing);
        var cell = Math.Max(16, (_row.Width - gaps) / _cells.Length);
        var height = Math.Round(Math.Min(cell * 1.08, MaxCellWidth * 1.15));
        if (Math.Abs(_row.HeightRequest - height) > 0.5)
            _row.HeightRequest = height;
        foreach (var label in _labels)
            label.FontSize = Math.Round(height * 0.64);
        _caret.HeightRequest = Math.Round(height * 0.57);
    }

    private void ApplyFont()
    {
        foreach (var label in _labels)
        {
            label.FontFamily = FontFamily;
            label.FontAttributes = FontFamily is null ? FontAttributes.Bold : FontAttributes.None;
        }
    }

    private void OnAlphabetChanged()
    {
        _map = null;
        var digitsOnly = Alphabet.Length > 0 && Alphabet.All(char.IsAsciiDigit);
        _entry.Keyboard = digitsOnly ? Keyboard.Numeric : Keyboard.Create(KeyboardFlags.CapitalizeCharacter);
#if IOS
        ApplyContentType();
#endif
    }

#if IOS
    // A code of digits is most likely a one-time code: iOS offers the one from Messages or Mail above the keyboard.
    private void ApplyContentType()
    {
        // The binding takes no null, so a field that was digits and is no longer keeps the hint; Alphabet is set once in practice.
        if (_entry.Handler?.PlatformView is UIKit.UITextField field && Alphabet.All(char.IsAsciiDigit))
            field.TextContentType = UIKit.UITextContentType.OneTimeCode;
    }
#endif

    /// <summary>A typed character as it is kept, or null when the alphabet does not have it.</summary>
    private char? Map(char c)
    {
        _map ??= BuildMap(Alphabet, Substitutions);
        return _map(c);
    }

    private static Func<char, char?> BuildMap(string alphabet, string substitutions)
    {
        var allowed = alphabet.ToUpperInvariant().ToHashSet();
        var swap = new Dictionary<char, char>();
        foreach (var pair in substitutions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Length == 2)
                swap[char.ToUpperInvariant(pair[0])] = char.ToUpperInvariant(pair[1]);
        return c =>
        {
            var upper = char.ToUpperInvariant(c);
            if (swap.TryGetValue(upper, out var swapped))
                upper = swapped;
            return allowed.Contains(upper) ? upper : null;
        };
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
            app.RequestedThemeChanged += OnThemeChanged;
        _blink = Dispatcher.CreateTimer();
        _blink.Interval = TimeSpan.FromMilliseconds(530);
        _blink.Tick += (_, _) => _caret.Opacity = _caret.Opacity > 0.5 ? 0 : 1;
        _blink.Start();
        Update();
        if (IsActive)
            FocusSoon();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (Application.Current is { } app)
            app.RequestedThemeChanged -= OnThemeChanged;
        _blink?.Stop();
        _blink = null;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Update();

    private void OnIsActiveChanged(bool active)
    {
        if (active)
            FocusSoon();
        else
            _entry.Unfocus();
    }

    // A sheet still sliding up, or a view just made visible, does not take the keyboard yet.
    private void FocusSoon() => Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(350), () =>
    {
        if (IsActive && Handler is not null && IsVisible)
            FocusAt(Code.Length);
    });

    private void FocusAt(int index)
    {
        _caretAt = Math.Clamp(index, 0, Code.Length);
        if (!_entry.IsFocused)
            _entry.Focus();
        SetCaret(_caretAt);
        Update();
    }

    private void SetCaret(int index)
    {
        _syncing = true;
        try
        {
            _entry.CursorPosition = index;
            _entry.SelectionLength = 0;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SetFocused(bool focused)
    {
        _focused = focused;
        Update();
    }

    private void OnTapped(Point? at)
    {
        if (State is CodeFieldState.Checking or CodeFieldState.Accepted)
            return;
        var index = Code.Length;
        if (at is { } point)
        {
            var nearest = double.MaxValue;
            for (var i = 0; i < _cells.Length; i++)
            {
                var distance = Math.Abs(_cells[i].Frame.Center.X - point.X);
                if (distance < nearest)
                {
                    nearest = distance;
                    index = i;
                }
            }
        }
        FocusAt(Math.Min(index, Code.Length));
    }

    private void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        var old = Code;
        var raw = e.NewTextValue ?? "";
        // iOS reports a text set from code once more, later; it is no edit and must not move the caret.
        if (_syncing || raw == old)
            return;
        var length = Length;
        var caret = Math.Clamp(_caretAt, 0, old.Length);

        // Where the text changed: at the caret when it is plain typing, else wherever the two texts differ.
        int start, removed;
        string inserted;
        if (raw.Length > old.Length && raw.StartsWith(old[..caret], StringComparison.Ordinal)
            && raw.EndsWith(old[caret..], StringComparison.Ordinal))
        {
            start = caret;
            removed = 0;
            inserted = raw.Substring(caret, raw.Length - old.Length);
        }
        else
        {
            start = 0;
            while (start < old.Length && start < raw.Length && old[start] == raw[start])
                start++;
            var end = 0;
            while (end < old.Length - start && end < raw.Length - start && old[^(end + 1)] == raw[^(end + 1)])
                end++;
            removed = old.Length - start - end;
            inserted = raw[start..^end];
        }

        var typed = new StringBuilder();
        var dropped = false;
        foreach (var c in inserted)
        {
            if (c is '-' or ' ')
                continue;
            if (Map(c) is { } mapped)
                typed.Append(mapped);
            else
                dropped = true;
        }

        string next;
        if (typed.Length >= length)
            next = typed.ToString(0, length);           // a whole code pasted over whatever was there
        else if (removed == 0 && typed.Length > 0)
            next = old[..start] + typed + old[Math.Min(old.Length, start + typed.Length)..];   // typed inside: replace
        else
            next = old[..start] + typed + old[(start + removed)..];
        if (next.Length > length)
            next = next[..length];
        var nextCaret = Math.Min(typed.Length >= length ? length : start + typed.Length, next.Length);

        Apply(next, nextCaret);
        if (dropped)
            Shake(_cells[Math.Min(nextCaret, length - 1)]);
        else if (typed.Length > 0 && typed.Length < length)
            Pop(_cells[Math.Max(0, nextCaret - 1)]);
        if (next.Length == length && next != old)
            Submit(next);
    }

    private void Apply(string code, int caret)
    {
        _caretAt = caret;
        _syncing = true;
        try
        {
            if (_entry.Text != code)
                _entry.Text = code;
            Code = code;
        }
        finally
        {
            _syncing = false;
        }
        SetCaret(caret);
        // iOS moves its own caret after a text set from code (a dropped character, an O made 0); put ours back once it has.
        Dispatcher.Dispatch(() => SetCaret(_caretAt));
        _caret.Opacity = 1;
        Update();
    }

    private void Submit(string code)
    {
        if (code.Length == Length && State is CodeFieldState.Editing or CodeFieldState.Rejected
            && CompletedCommand is { } command && command.CanExecute(code))
            command.Execute(code);
    }

    private void OnCodeChanged(string old, string code)
    {
        if (_syncing || _row is null)
            return;
        // Set from outside: keep only what a code can hold, then fill the new cells one after the other.
        var clean = new StringBuilder();
        foreach (var c in code)
            if (Map(c) is { } mapped && clean.Length < Length)
                clean.Append(mapped);
        if (clean.ToString() != code)
        {
            Code = clean.ToString();
            return;
        }
        _syncing = true;
        try
        {
            _entry.Text = code;
        }
        finally
        {
            _syncing = false;
        }
        _caretAt = code.Length;
        if (_entry.IsFocused)
            SetCaret(_caretAt);
        if (code.Length - old.Length > 1 && code.StartsWith(old, StringComparison.Ordinal))
            _ = Cascade(old.Length, code.Length);
        else
            Update();
    }

    private async Task Cascade(int from, int to)
    {
        _shown = from;
        Update();
        var still = ReducedMotion.IsOn;
        for (var i = from; i < to && i < _cells.Length; i++)
        {
            if (Code.Length < i + 1)
                break;
            _shown = i + 1;
            Update();
            if (!still)
            {
                Pop(_cells[i]);
                await Task.Delay(35);
            }
        }
        _shown = int.MaxValue;
        Update();
    }

    private void OnStateChanged(CodeFieldState state)
    {
        this.AbortAnimation(PulseAnimation);
        SetCellsOpacity(1);
        switch (state)
        {
            case CodeFieldState.Checking:
                _entry.Unfocus();
                Update();
                if (!ReducedMotion.IsOn)
                {
                    var pulse = new Animation
                    {
                        { 0, 0.5, new Animation(SetCellsOpacity, 1, 0.45, Easing.SinInOut) },
                        { 0.5, 1, new Animation(SetCellsOpacity, 0.45, 1, Easing.SinInOut) },
                    };
                    pulse.Commit(this, PulseAnimation, length: 900, repeat: () => State == CodeFieldState.Checking,
                        finished: (_, _) => SetCellsOpacity(1));
                }
                break;
            case CodeFieldState.Accepted:
                _entry.Unfocus();
                _ = Accept();
                break;
            case CodeFieldState.Rejected:
                Haptics.Error();
                Update();
                Shake(_row);
                break;
            default:
                Update();
                break;
        }
    }

    private void SetCellsOpacity(double opacity)
    {
        foreach (var cell in _cells)
            cell.Opacity = opacity;
    }

    private async Task Accept()
    {
        _lit = 0;
        if (ReducedMotion.IsOn)
        {
            _lit = _cells.Length;
            Update();
            Haptics.Success();
            return;
        }
        // The accent goes on cell by cell, from the left.
        for (var i = 0; i < _cells.Length && State == CodeFieldState.Accepted; i++)
        {
            _lit = i + 1;
            Update();
            Pop(_cells[i], 1.16);
            await Task.Delay(40);
        }
        Haptics.Success();
    }

    private static void Pop(VisualElement view, double scale = 1.12)
    {
        if (ReducedMotion.IsOn)
            return;
        view.AbortAnimation("Spine.CodeField.Pop");
        view.Animate("Spine.CodeField.Pop", new Animation
        {
            { 0, 0.4, new Animation(v => view.Scale = v, 1, scale, Easing.CubicOut) },
            { 0.4, 1, new Animation(v => view.Scale = v, scale, 1, Easing.CubicIn) },
        }, length: 200, finished: (_, _) => view.Scale = 1);
    }

    private static void Shake(VisualElement view)
    {
        if (ReducedMotion.IsOn)
            return;
        view.AbortAnimation("Spine.CodeField.Shake");
        view.Animate("Spine.CodeField.Shake", new Animation(t => view.TranslationX = Math.Sin(t * Math.PI * 6) * 8 * (1 - t)),
            length: 420, finished: (_, _) => view.TranslationX = 0);
    }

    private void Update()
    {
        if (_row is null || _cells.Length == 0)
            return;
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var accent = AccentColor ?? Resource("Accent") ?? Color.FromArgb(dark ? "#0A84FF" : "#007AFF");
        var onAccent = OnAccentColor ?? SpineAccent.TextOn(accent);
        var text = TextColor ?? (dark ? Colors.White : Colors.Black);
        var fill = CellColor ?? Color.FromArgb(dark ? "#1C1C1E" : "#F2F2F7");
        var line = text.WithAlpha(0.12f);
        var filledLine = text.WithAlpha(0.3f);
        var error = ErrorColor ?? Color.FromArgb(dark ? "#FF453A" : "#FF3B30");

        var code = Code;
        var editing = State is CodeFieldState.Editing or CodeFieldState.Rejected;
        var active = _focused && editing ? Math.Min(_caretAt, _cells.Length - 1) : -1;
        var lit = State == CodeFieldState.Accepted ? _lit : 0;

        for (var i = 0; i < _cells.Length; i++)
        {
            var shown = i < code.Length && i < _shown;
            _labels[i].Text = shown ? code[i].ToString() : "";
            Color stroke, background, ink;
            if (i < lit)
                (background, stroke, ink) = (accent, accent, onAccent);
            else if (State == CodeFieldState.Rejected)
                (background, stroke, ink) = (fill, error, error);
            else if (i == active)
                (background, stroke, ink) = (fill, accent, text);
            else
                (background, stroke, ink) = (fill, shown ? filledLine : line, text);
            _cells[i].BackgroundColor = background;
            _cells[i].Stroke = new SolidColorBrush(stroke);
            _labels[i].TextColor = ink;
        }
        foreach (var dash in _dashes)
            dash.Color = text.WithAlpha(0.45f);

        // The caret blinks in the empty cell it is at; on a filled cell the accent edge says it will be replaced.
        var caretVisible = active >= 0 && _caretAt < _cells.Length && _caretAt >= code.Length;
        _caret.Color = accent;
        _caret.IsVisible = caretVisible;
        if (caretVisible)
            Grid.SetColumn(_caret, _columns[_caretAt]);

        SemanticProperties.SetDescription(_touch, code.Length == 0
            ? string.Format(SpineStrings.Current["Spine.CodeField.Empty"], _cells.Length)
            : string.Format(SpineStrings.Current["Spine.CodeField.Value"], string.Join(' ', code.ToCharArray())));
    }

    private static Color? Resource(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as Color : null;
}
