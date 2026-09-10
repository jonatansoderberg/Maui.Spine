namespace Plugin.Maui.Spine.Push;

/// <summary>When the app asks the user for permission to notify.</summary>
public enum PushPermission
{
    /// <summary>Only when the app calls <see cref="IPushService.RequestPermissionAsync"/>. The default.</summary>
    WhenAsked,

    /// <summary>
    /// Quietly at launch, iOS's provisional authorization: notifications arrive silently in the
    /// Notification Center and the user is asked to keep them after seeing one. No prompt.
    /// </summary>
    Provisional,

    /// <summary>Prompt at the first launch. Blunt, but right for an app that is useless without it.</summary>
    AtLaunch,
}

/// <summary>One Android notification channel. Ignored on other platforms.</summary>
/// <param name="Id">The channel id, which a message names in <see cref="Common.PushKeys.Channel"/>.</param>
/// <param name="Name">The name the user sees in system settings.</param>
/// <param name="Importance">How loudly it may arrive.</param>
/// <param name="Sound">
/// A sound in the app's <c>Resources/Raw</c>, by file name without extension, or <see langword="null"/>
/// for the system sound. Fixed once the channel exists: Android keeps the sound a channel was created
/// with, so changing it means a new channel id.
/// </param>
public readonly record struct PushChannel(
    string Id, string Name, PushChannelImportance Importance = PushChannelImportance.Default, string? Sound = null);

/// <summary>One button on a notification.</summary>
/// <param name="Id">What the handler receives as the action.</param>
/// <param name="Title">The button's text.</param>
public sealed record PushAction(string Id, string Title)
{
    /// <summary>
    /// Whether tapping brings the app to the front. When it does, the tap reaches
    /// <see cref="IPushHandler.OnOpenedAsync"/>, where navigating is safe. When it does not, it reaches
    /// <see cref="IPushHandler.OnActionAsync"/> in the background — for "mark as read" or "sign me up",
    /// where opening the app would be in the way.
    /// </summary>
    public bool OpensApp { get; init; } = true;

    /// <summary>Drawn as destructive on Apple platforms. Android has no such style and draws it like the rest.</summary>
    public bool Destructive { get; init; }

    /// <summary>
    /// Makes the button a reply field with this placeholder. What the user types reaches
    /// <see cref="IPushHandler.OnActionAsync"/> as its text. A reply never opens the app, whatever
    /// <see cref="OpensApp"/> says.
    /// </summary>
    public string? Reply { get; init; }

    internal bool RunsInBackground => Reply is not null || !OpensApp;
}

/// <summary>A set of buttons a notification names by id, in <see cref="Common.PushKeys.Category"/>.</summary>
/// <param name="Id">The id a notification names.</param>
/// <param name="Actions">The buttons, in the order they are shown.</param>
public sealed record PushCategory(string Id, IReadOnlyList<PushAction> Actions);

/// <summary>How loudly an Android channel may arrive.</summary>
public enum PushChannelImportance
{
    /// <summary>Silent, no banner.</summary>
    Low,

    /// <summary>A banner without a sound.</summary>
    Default,

    /// <summary>A banner with a sound.</summary>
    High,
}

/// <summary>How Spine.Push behaves in this app.</summary>
public sealed class SpinePushOptions
{
    /// <summary>
    /// The backend that serves <c>PUT</c> and <c>DELETE</c> on <c>{Backend}/installations/{id}</c> —
    /// the prefix given to <c>MapSpinePush</c> on the server.
    /// </summary>
    public Uri? Backend { get; set; }

    /// <summary>
    /// The value sent as the <c>Authorization</c> header when registering. Read on every call, so a
    /// token that rotates can be returned from here.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? AuthorizationHeader { get; set; }

    /// <summary>When to ask the user for permission.</summary>
    public PushPermission Permission { get; set; } = PushPermission.WhenAsked;

    /// <summary>The Android channels to create at startup. The first one is used when a message names none.</summary>
    public IList<PushChannel> Channels { get; } = [];

    /// <summary>
    /// The button sets a notification can name. Registered with iOS at launch — a notification whose
    /// category iOS has not been told about shows no buttons — and read by Android when it draws one.
    /// </summary>
    public IList<PushCategory> Categories { get; } = [];

    /// <summary>How long a registration is good for before the server may prune it.</summary>
    public TimeSpan Expiry { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// How long the app may go without confirming its registration when nothing has changed.
    /// </summary>
    /// <remarks>
    /// A register can lose a row — a recreated container, a pruned table, a restore from an older
    /// backup — and the app has no way to notice: nothing on the wire tells a device it is no
    /// longer registered, and it only hears from the backend when a message arrives. So a silently
    /// dropped registration reads exactly like a quiet week.
    /// <para>
    /// Confirming costs one small request on a foreground the app was making anyway. Being
    /// unregistered without knowing costs every notification until something else happens to change
    /// the fingerprint. Minutes rather than a day is the honest price of that asymmetry.
    /// </para>
    /// </remarks>
    public TimeSpan Confirm { get; set; } = TimeSpan.FromMinutes(15);

    internal Type? HandlerType { get; private set; }

    /// <summary>Registers the app's <see cref="IPushHandler"/>.</summary>
    /// <typeparam name="THandler">The handler to resolve through DI for every message.</typeparam>
    /// <returns>The same options, for chaining.</returns>
    public SpinePushOptions UseHandler<THandler>() where THandler : class, IPushHandler
    {
        HandlerType = typeof(THandler);
        return this;
    }

    /// <summary>Adds an Android notification channel.</summary>
    /// <param name="id">The channel id.</param>
    /// <param name="name">The name the user sees.</param>
    /// <param name="importance">How loudly it may arrive.</param>
    /// <param name="sound">
    /// A sound in <c>Resources/Raw</c>, by file name without extension. Fixed once the channel exists;
    /// see <see cref="PushChannel.Sound"/>.
    /// </param>
    /// <returns>The same options, for chaining.</returns>
    public SpinePushOptions AddChannel(
        string id, string name, PushChannelImportance importance = PushChannelImportance.Default, string? sound = null)
    {
        Channels.Add(new PushChannel(id, name, importance, sound));
        return this;
    }

    /// <summary>Declares a set of buttons a notification can name by <paramref name="id"/>.</summary>
    /// <param name="id">What a notification names in <see cref="Common.PushKeys.Category"/>.</param>
    /// <param name="actions">The buttons, in the order they are shown.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpinePushOptions AddCategory(string id, params PushAction[] actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfZero(actions.Length);

        Categories.Add(new PushCategory(id, actions));
        return this;
    }

    internal Uri InstallationsEndpoint(string installationId)
    {
        if (Backend is null)
            throw new InvalidOperationException("UseSpinePush: Backend is required before the app can register.");

        var prefix = Backend.ToString().TrimEnd('/');
        return new Uri($"{prefix}/installations/{Uri.EscapeDataString(installationId)}");
    }
}
