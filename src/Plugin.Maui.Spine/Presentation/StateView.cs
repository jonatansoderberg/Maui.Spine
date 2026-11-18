using System.ComponentModel;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Shows its content, or the loading, error or empty state of a <see cref="TaskState"/> in its
/// place. The content is an ordinary child with the page's binding context; bind it to the state's
/// <c>Value</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Loading</b> (and not started): <see cref="LoadingTemplate"/>, or the content when the state
/// has a placeholder, so a skeleton can be drawn over it. The same content view then shows the result;
/// nothing is rebuilt.</item>
/// <item><b>Refreshing</b> and <b>Success</b>: the content.</item>
/// <item><b>Empty</b>: <see cref="EmptyTemplate"/>. <b>Error</b>: <see cref="ErrorTemplate"/>.</item>
/// </list>
/// The state templates get the <see cref="TaskState"/> as binding context (<c>ErrorMessage</c>,
/// <c>LoadCommand</c>). A template not set on the view is taken from the application resource
/// <c>DefaultStateViewLoadingTemplate</c>, <c>DefaultStateViewErrorTemplate</c> or
/// <c>DefaultStateViewEmptyTemplate</c>, and otherwise drawn by Spine: a spinner in the app's
/// accent, the error's message with a retry button, and a line of text.
/// </remarks>
/// <example>
/// <code>
/// &lt;StateView State="{Binding Competitions}"&gt;
///     &lt;CollectionView ItemsSource="{Binding Competitions.Value}" /&gt;
/// &lt;/StateView&gt;
/// </code>
/// </example>
public class StateView : ContentView
{
    /// <summary>The load whose state is shown.</summary>
    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State), typeof(TaskState), typeof(StateView),
        propertyChanged: static (bindable, oldValue, newValue) => ((StateView)bindable).OnStateChanged((TaskState?)oldValue, (TaskState?)newValue));

    /// <summary>What is shown while there is nothing to show yet.</summary>
    public static readonly BindableProperty LoadingTemplateProperty = BindableProperty.Create(
        nameof(LoadingTemplate), typeof(DataTemplate), typeof(StateView),
        propertyChanged: static (bindable, _, _) => ((StateView)bindable).Rebuild(ref ((StateView)bindable)._loadingView));

    /// <summary>What is shown when the load failed with nothing earlier to show.</summary>
    public static readonly BindableProperty ErrorTemplateProperty = BindableProperty.Create(
        nameof(ErrorTemplate), typeof(DataTemplate), typeof(StateView),
        propertyChanged: static (bindable, _, _) => ((StateView)bindable).Rebuild(ref ((StateView)bindable)._errorView));

    /// <summary>What is shown when the result is empty.</summary>
    public static readonly BindableProperty EmptyTemplateProperty = BindableProperty.Create(
        nameof(EmptyTemplate), typeof(DataTemplate), typeof(StateView),
        propertyChanged: static (bindable, _, _) => ((StateView)bindable).Rebuild(ref ((StateView)bindable)._emptyView));

    /// <summary>The text of Spine's own empty state.</summary>
    public static readonly BindableProperty EmptyTextProperty = BindableProperty.Create(
        nameof(EmptyText), typeof(string), typeof(StateView),
        propertyChanged: static (bindable, _, _) => ((StateView)bindable).ApplyEmptyText());

    /// <summary>The load whose state is shown.</summary>
    public TaskState? State
    {
        get => (TaskState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>What is shown while there is nothing to show yet, with the <see cref="TaskState"/> as binding context.</summary>
    public DataTemplate? LoadingTemplate
    {
        get => (DataTemplate?)GetValue(LoadingTemplateProperty);
        set => SetValue(LoadingTemplateProperty, value);
    }

    /// <summary>
    /// What is shown when the load failed with nothing earlier to show, with the <see cref="TaskState"/>
    /// as binding context: <c>ErrorMessage</c> for the text, <c>LoadCommand</c> for a retry button.
    /// </summary>
    public DataTemplate? ErrorTemplate
    {
        get => (DataTemplate?)GetValue(ErrorTemplateProperty);
        set => SetValue(ErrorTemplateProperty, value);
    }

    /// <summary>What is shown when the result is empty, with the <see cref="TaskState"/> as binding context.</summary>
    public DataTemplate? EmptyTemplate
    {
        get => (DataTemplate?)GetValue(EmptyTemplateProperty);
        set => SetValue(EmptyTemplateProperty, value);
    }

    /// <summary>
    /// The text of Spine's own empty state, when there is no <see cref="EmptyTemplate"/>. Default
    /// <c>Spine.State.Empty.Title</c> from the strings.
    /// </summary>
    public string? EmptyText
    {
        get => (string?)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    private ContentPresenter? _presenter;
    private ContentView? _stateHost;
    private View? _loadingView;
    private View? _errorView;
    private View? _emptyView;
    private Label? _defaultEmptyLabel;
    private TaskState? _subscribed;

    /// <summary>Creates the view.</summary>
    public StateView()
    {
        ControlTemplate = new ControlTemplate(() =>
        {
            // A reapplied template must not leave a state view parented to the old host.
            _stateHost?.Content = null;

            _presenter = new ContentPresenter();
            _stateHost = new ContentView { BindingContext = State };
            Update();

            return new Grid { Children = { _presenter, _stateHost } };
        });

        Loaded += (_, _) => Subscribe(State);
        Unloaded += (_, _) => Subscribe(null);
    }

    private void OnStateChanged(TaskState? oldValue, TaskState? newValue)
    {
        if (_subscribed is not null || IsLoaded)
            Subscribe(newValue);

        _stateHost?.BindingContext = newValue;
        Update();
    }

    private void Subscribe(TaskState? state)
    {
        if (ReferenceEquals(_subscribed, state))
            return;

        _subscribed?.PropertyChanged -= OnStatePropertyChanged;
        _subscribed = state;
        _subscribed?.PropertyChanged += OnStatePropertyChanged;
        Update();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TaskState.Status) or null or ""))
            return;

        if (Dispatcher is { IsDispatchRequired: true } dispatcher)
            dispatcher.Dispatch(Update);
        else
            Update();
    }

    private void Rebuild(ref View? view)
    {
        view = null;
        Update();
    }

    private void Update()
    {
        if (_presenter is null || _stateHost is null)
            return;

        var state = State;
        var showContent = state?.Status switch
        {
            null or TaskStateStatus.Success or TaskStateStatus.Refreshing => true,
            TaskStateStatus.NotStarted or TaskStateStatus.Loading => state.HasPlaceholder,
            _ => false,
        };

        var stateView = showContent ? null : state!.Status switch
        {
            TaskStateStatus.Empty => _emptyView ??= Create(EmptyTemplate, "DefaultStateViewEmptyTemplate", CreateDefaultEmpty),
            TaskStateStatus.Error => _errorView ??= Create(ErrorTemplate, "DefaultStateViewErrorTemplate", CreateDefaultError),
            _ => _loadingView ??= Create(LoadingTemplate, "DefaultStateViewLoadingTemplate", CreateDefaultLoading),
        };

        _presenter.IsVisible = showContent;

        if (!ReferenceEquals(_stateHost.Content, stateView))
            _stateHost.Content = stateView;

        _stateHost.IsVisible = stateView is not null;
    }

    private static View Create(DataTemplate? template, string resourceKey, Func<View> fallback)
    {
        template ??= Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
            ? resource as DataTemplate
            : null;

        return template?.CreateContent() as View ?? fallback();
    }

    private static View CreateDefaultLoading()
    {
        var indicator = new ActivityIndicator
        {
            IsRunning = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(24),
        };

        indicator.SetBinding(SemanticProperties.DescriptionProperty, SpineString("Spine.State.Loading"));
        ApplyAccent(indicator);
        SpineTheme.Track(indicator, () => ApplyAccent(indicator));

        return indicator;
    }

    private static void ApplyAccent(ActivityIndicator indicator)
    {
        var theme = Application.Current?.RequestedTheme == AppTheme.Dark ? AppTheme.Dark : AppTheme.Light;

        if (SpineTheme.GetAccent(theme) is { } accent)
            indicator.Color = accent;
    }

    private static View CreateDefaultError()
    {
        var title = new Label
        {
            FontSize = PlatformValue.IsMacIdiom ? 13 : 17,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
        };
        title.SetBinding(Label.TextProperty, SpineString("Spine.State.Error.Title"));

        var message = new Label
        {
            FontSize = 14,
            Opacity = 0.6,
            HorizontalTextAlignment = TextAlignment.Center,
        };
        message.SetBinding(Label.TextProperty, nameof(TaskState.ErrorMessage));
        message.SetBinding(IsVisibleProperty, nameof(TaskState.HasError));

        var retry = new Button
        {
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };
        retry.SetBinding(Button.TextProperty, SpineString("Spine.State.Error.Retry"));
        retry.SetBinding(Button.CommandProperty, nameof(TaskState.LoadCommand));

        return new VerticalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(24),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { title, message, retry },
        };
    }

    private View CreateDefaultEmpty()
    {
        _defaultEmptyLabel = new Label
        {
            FontSize = 15,
            Opacity = 0.6,
            Margin = new Thickness(24),
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        ApplyEmptyText();

        return _defaultEmptyLabel;
    }

    private void ApplyEmptyText()
    {
        if (_defaultEmptyLabel is not { } label)
            return;

        if (EmptyText is { } text)
        {
            label.RemoveBinding(Label.TextProperty);
            label.Text = text;
        }
        else
        {
            label.SetBinding(Label.TextProperty, SpineString("Spine.State.Empty.Title"));
        }
    }

    private static BindingBase SpineString(string key) => new StringExtension { Key = key }.ProvideValue(null!);
}
