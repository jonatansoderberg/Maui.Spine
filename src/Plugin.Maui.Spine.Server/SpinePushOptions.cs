using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>Credentials and defaults for the APNs transport.</summary>
public sealed class ApplePushOptions
{
    /// <summary>The ten-character Apple Developer team id.</summary>
    public string? TeamId { get; set; }

    /// <summary>The ten-character id of the <c>.p8</c> auth key.</summary>
    public string? KeyId { get; set; }

    /// <summary>The contents of the <c>.p8</c> file, PEM and all.</summary>
    public string? PrivateKey { get; set; }

    /// <summary>The app's bundle id, which is also the default <c>apns-topic</c>.</summary>
    public string? BundleId { get; set; }

    /// <summary>
    /// Which APNs host to use. <see cref="ApnsEnvironment.PerInstallation"/>, the default, trusts
    /// what each installation reported when it registered.
    /// </summary>
    public ApnsEnvironment Environment { get; set; } = ApnsEnvironment.PerInstallation;

    /// <summary>
    /// The environment a channel request or a broadcast goes to. A device push reads it from the
    /// installation; these have none, so it is the one asked for, or <see cref="Environment"/>.
    /// </summary>
    internal ApnsEnvironment ChannelEnvironment(ApnsEnvironment? requested) => (requested ?? Environment) switch
    {
        ApnsEnvironment.PerInstallation when requested is null => throw new InvalidOperationException(
            "A channel belongs to one APNs environment and Environment is PerInstallation, so say which: pass Sandbox or Production."),
        ApnsEnvironment.PerInstallation => throw new ArgumentException(
            "A channel belongs to Sandbox or Production, not PerInstallation.", nameof(requested)),
        var environment => environment,
    };

    internal void Validate()
    {
        Require(TeamId, nameof(TeamId));
        Require(KeyId, nameof(KeyId));
        Require(PrivateKey, nameof(PrivateKey));
        Require(BundleId, nameof(BundleId));

        static void Require(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"AddSpinePush: Apple.{name} is required when Apple(...) is configured.");
        }
    }
}

/// <summary>Credentials for the FCM transport.</summary>
public sealed class AndroidPushOptions
{
    /// <summary>The contents of the Firebase service account JSON file.</summary>
    public string? ServiceAccountJson { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServiceAccountJson))
            throw new InvalidOperationException(
                $"AddSpinePush: Android.{nameof(ServiceAccountJson)} is required when Android(...) is configured.");
    }
}

/// <summary>How Spine.Push behaves on this server.</summary>
public sealed class SpinePushOptions
{
    /// <summary>The APNs settings, or <see langword="null"/> when Apple is not configured.</summary>
    public ApplePushOptions? AppleOptions { get; private set; }

    /// <summary>The FCM settings, or <see langword="null"/> when Android is not configured.</summary>
    public AndroidPushOptions? AndroidOptions { get; private set; }

    /// <summary>
    /// Narrows the tags a client is allowed to register. Runs on every registration; whatever it
    /// returns is what gets stored. Leave unset to accept the client's tags as they are.
    /// </summary>
    /// <example>
    /// <code>
    /// o.AllowTags = (installation, tags) =>
    ///     tags.Where(t => !t.StartsWith("user:") || t == $"user:{installation.UserId}");
    /// </code>
    /// </example>
    public Func<PushInstallation, IEnumerable<string>, IEnumerable<string>>? AllowTags { get; set; }

    /// <summary>
    /// Decides whether a registration request is allowed. Authentication is the backend's business;
    /// the client sends whatever <c>AuthorizationHeader</c> it was given. Unset means every request
    /// is accepted, which is only reasonable behind another gate.
    /// </summary>
    public Func<HttpRequest, ValueTask<bool>>? Authenticate { get; set; }

    internal Func<IServiceProvider, IPushInstallationStore>? StoreFactory { get; private set; }

    /// <summary>Configures the APNs transport.</summary>
    /// <param name="configure">Sets team, key, bundle and environment.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpinePushOptions Apple(Action<ApplePushOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AppleOptions ??= new ApplePushOptions();
        configure(AppleOptions);
        return this;
    }

    /// <summary>Configures the FCM transport.</summary>
    /// <param name="configure">Sets the service account.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpinePushOptions Android(Action<AndroidPushOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AndroidOptions ??= new AndroidPushOptions();
        configure(AndroidOptions);
        return this;
    }

    /// <summary>Keeps the register in the process. For tests and sample servers.</summary>
    /// <returns>The same options, for chaining.</returns>
    public SpinePushOptions UseInMemoryStore()
    {
        StoreFactory = _ => new InMemoryPushInstallationStore();
        return this;
    }

    /// <summary>Keeps the register in Azure Table Storage.</summary>
    /// <param name="connectionString">The storage account; <c>UseDevelopmentStorage=true</c> for Azurite.</param>
    /// <param name="prefix">Prepended to both table names, so several apps can share an account.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpinePushOptions UseAzureTableStore(string connectionString, string prefix = "SpinePush")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        StoreFactory = sp => new AzureTablePushInstallationStore(connectionString, prefix, sp.GetService<TimeProvider>());
        return this;
    }

    /// <summary>Uses a register of your own.</summary>
    /// <param name="factory">Builds the store from the application's services.</param>
    /// <returns>The same options, for chaining.</returns>
    public SpinePushOptions UseStore(Func<IServiceProvider, IPushInstallationStore> factory)
    {
        StoreFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return this;
    }

    internal void Validate()
    {
        // No platform configured is allowed: the register and the endpoints work without a transport,
        // and a server that only registers devices — during development, or one process registering
        // while another sends — is a real shape. Sending then reaches nobody and says so, since
        // PushResult lists no deliveries.
        AppleOptions?.Validate();
        AndroidOptions?.Validate();

        if (StoreFactory is null)
            throw new InvalidOperationException("AddSpinePush: pick a register with UseInMemoryStore() or UseStore(...).");
    }

    /// <summary>The tags <paramref name="requested"/> reduced to what <see cref="AllowTags"/> permits.</summary>
    internal IReadOnlyList<string> FilterTags(PushInstallation installation, IEnumerable<string> requested) =>
        AllowTags is null
            ? requested.ToList()
            : AllowTags(installation, requested).ToList();
}
