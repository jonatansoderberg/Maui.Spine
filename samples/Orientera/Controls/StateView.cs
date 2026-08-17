using System.Windows.Input;

namespace Orientera.Controls;

/// <summary>The four states every fetching view is drawn in (P10).</summary>
public enum ViewState
{
    Loading,
    Content,
    Empty,
    Error,
}

/// <summary>
/// Draws a fetching view in one of four states, none of which is a spinner laid over something
/// else (P10).
/// </summary>
/// <remarks>
/// The state is a single value, not three bindings that happen to be false together. That is the
/// half of P10 which cannot be reached with three <c>IsVisible</c>: the test run found an empty
/// state showing while a fetch was still running, and with one enum that arrangement has no way to
/// occur.
/// <para>
/// The skeleton is supplied by the caller because it has to have the shape of the content it
/// stands in for — a skeleton of the wrong shape is just a grey box, and the page shifts when the
/// rows arrive. The empty and error states are drawn here when nothing is supplied, so the four
/// states cost a page one property instead of four layouts.
/// </para>
/// </remarks>
public sealed class StateView : ContentView
{
    public static readonly BindableProperty StateProperty =
        BindableProperty.Create(nameof(State), typeof(ViewState), typeof(StateView),
            ViewState.Content, propertyChanged: (b, _, _) => ((StateView)b).Apply());

    public static readonly BindableProperty SkeletonProperty =
        BindableProperty.Create(nameof(Skeleton), typeof(View), typeof(StateView), null,
            propertyChanged: (b, _, _) => ((StateView)b).Apply());

    public static readonly BindableProperty BodyProperty =
        BindableProperty.Create(nameof(Body), typeof(View), typeof(StateView), null,
            propertyChanged: (b, _, _) => ((StateView)b).Apply());

    public static readonly BindableProperty EmptyViewProperty =
        BindableProperty.Create(nameof(EmptyView), typeof(View), typeof(StateView), null,
            propertyChanged: (b, _, _) => ((StateView)b).Apply());

    public static readonly BindableProperty ErrorViewProperty =
        BindableProperty.Create(nameof(ErrorView), typeof(View), typeof(StateView), null,
            propertyChanged: (b, _, _) => ((StateView)b).Apply());

    public static readonly BindableProperty EmptyMessageProperty =
        BindableProperty.Create(nameof(EmptyMessage), typeof(string), typeof(StateView), string.Empty,
            propertyChanged: (b, _, _) => ((StateView)b).Apply());

    public static readonly BindableProperty EmptyHintProperty =
        BindableProperty.Create(nameof(EmptyHint), typeof(string), typeof(StateView), string.Empty,
            propertyChanged: (b, _, _) => ((StateView)b).Apply());

    public static readonly BindableProperty ErrorMessageProperty =
        BindableProperty.Create(nameof(ErrorMessage), typeof(string), typeof(StateView), string.Empty,
            propertyChanged: (b, _, _) => ((StateView)b).Apply());

    public static readonly BindableProperty ErrorDetailProperty =
        BindableProperty.Create(nameof(ErrorDetail), typeof(string), typeof(StateView), string.Empty,
            propertyChanged: (b, _, _) => ((StateView)b).Apply());

    public static readonly BindableProperty RetryCommandProperty =
        BindableProperty.Create(nameof(RetryCommand), typeof(ICommand), typeof(StateView), null,
            propertyChanged: (b, _, _) => ((StateView)b).Apply());

    private readonly Grid _stack = new();
    private readonly ContentView _skeletonSlot = new();
    private readonly ContentView _bodySlot = new();
    private readonly ContentView _emptySlot = new();
    private readonly ContentView _errorSlot = new();

    private readonly Label _emptyMessage = new() { HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label _emptyHint = new() { HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label _errorMessage = new() { HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label _errorDetail = new() { HorizontalTextAlignment = TextAlignment.Center };
    private readonly Button _retry = new() { HorizontalOptions = LayoutOptions.Center };

    public StateView()
    {
        _emptyMessage.SetDynamicResource(StyleProperty, "Heading2Label");
        _emptyHint.SetDynamicResource(StyleProperty, "BodySecondaryLabel");
        _errorMessage.SetDynamicResource(StyleProperty, "Heading2Label");
        _errorDetail.SetDynamicResource(StyleProperty, "BodySecondaryLabel");
        _retry.SetDynamicResource(StyleProperty, "SecondaryButton");
        _retry.Text = "Försök igen";

        _stack.Children.Add(_skeletonSlot);
        _stack.Children.Add(_bodySlot);
        _stack.Children.Add(_emptySlot);
        _stack.Children.Add(_errorSlot);

        Content = _stack;

        Apply();
    }

    public ViewState State
    {
        get => (ViewState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>Skeleton rows in the shape of the content they stand in for.</summary>
    public View? Skeleton
    {
        get => (View?)GetValue(SkeletonProperty);
        set => SetValue(SkeletonProperty, value);
    }

    /// <summary>The content itself. Named <c>Body</c> so it does not collide with <c>Content</c>.</summary>
    public View? Body
    {
        get => (View?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public View? EmptyView
    {
        get => (View?)GetValue(EmptyViewProperty);
        set => SetValue(EmptyViewProperty, value);
    }

    public View? ErrorView
    {
        get => (View?)GetValue(ErrorViewProperty);
        set => SetValue(ErrorViewProperty, value);
    }

    /// <summary>One sentence on why there is nothing here.</summary>
    public string EmptyMessage
    {
        get => (string)GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    /// <summary>The way onwards from an empty view.</summary>
    public string EmptyHint
    {
        get => (string)GetValue(EmptyHintProperty);
        set => SetValue(EmptyHintProperty, value);
    }

    /// <summary>What went wrong.</summary>
    public string ErrorMessage
    {
        get => (string)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    /// <summary>What still works despite it.</summary>
    public string ErrorDetail
    {
        get => (string)GetValue(ErrorDetailProperty);
        set => SetValue(ErrorDetailProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    private void Apply()
    {
        _skeletonSlot.Content = Skeleton;
        _bodySlot.Content = Body;
        _emptySlot.Content = EmptyView ?? BuiltInEmpty();
        _errorSlot.Content = ErrorView ?? BuiltInError();

        _skeletonSlot.IsVisible = State is ViewState.Loading;
        _bodySlot.IsVisible = State is ViewState.Content;
        _emptySlot.IsVisible = State is ViewState.Empty;
        _errorSlot.IsVisible = State is ViewState.Error;

        _emptyMessage.Text = EmptyMessage;
        _emptyHint.Text = EmptyHint;
        _emptyHint.IsVisible = !string.IsNullOrWhiteSpace(EmptyHint);

        _errorMessage.Text = ErrorMessage;
        _errorDetail.Text = ErrorDetail;
        _errorDetail.IsVisible = !string.IsNullOrWhiteSpace(ErrorDetail);

        _retry.Command = RetryCommand;
        _retry.IsVisible = RetryCommand is not null;
    }

    private View BuiltInEmpty() => _builtInEmpty ??= new VerticalStackLayout
    {
        Spacing = 8,
        Padding = 32,
        VerticalOptions = LayoutOptions.Center,
        Children = { _emptyMessage, _emptyHint },
    };

    private View BuiltInError() => _builtInError ??= new VerticalStackLayout
    {
        Spacing = 12,
        Padding = 32,
        VerticalOptions = LayoutOptions.Center,
        Children = { _errorMessage, _errorDetail, _retry },
    };

    private View? _builtInEmpty;
    private View? _builtInError;
}
