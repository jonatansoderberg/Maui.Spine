using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class SpinePushOptionsTests
{
    private static void Apple(ApplePushOptions a)
    {
        a.TeamId = "TEAM123456";
        a.KeyId = "KEY1234567";
        a.PrivateKey = "-----BEGIN PRIVATE KEY-----\nx\n-----END PRIVATE KEY-----";
        a.BundleId = "com.companyname.orientera";
    }

    private static ServiceProvider Build(Action<SpinePushOptions> configure) =>
        new ServiceCollection().AddSpinePush(configure).BuildServiceProvider();

    [Fact]
    public void A_configured_server_resolves_its_register_and_options()
    {
        using var services = Build(o => o.Apple(Apple).UseInMemoryStore());

        Assert.IsType<InMemoryPushInstallationStore>(services.GetRequiredService<IPushInstallationStore>());
        Assert.NotNull(services.GetRequiredService<SpinePushOptions>().AppleOptions);
    }

    [Fact]
    public void A_server_without_a_platform_can_still_register_devices()
    {
        // The register and the endpoints do not need a transport; only sending does. A sample server
        // has to start before anyone has an APNs key.
        using var services = Build(o => o.UseInMemoryStore());

        Assert.IsType<InMemoryPushInstallationStore>(services.GetRequiredService<IPushInstallationStore>());
        Assert.Null(services.GetRequiredService<SpinePushOptions>().AppleOptions);
    }

    [Fact]
    public void A_server_without_a_register_is_rejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(o => o.Apple(Apple)));
        Assert.Contains("UseInMemoryStore", error.Message);
    }

    [Theory]
    [InlineData(nameof(ApplePushOptions.TeamId))]
    [InlineData(nameof(ApplePushOptions.KeyId))]
    [InlineData(nameof(ApplePushOptions.PrivateKey))]
    [InlineData(nameof(ApplePushOptions.BundleId))]
    public void Apple_says_which_credential_is_missing(string missing)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(o => o
            .Apple(a =>
            {
                Apple(a);
                switch (missing)
                {
                    case nameof(ApplePushOptions.TeamId): a.TeamId = null; break;
                    case nameof(ApplePushOptions.KeyId): a.KeyId = null; break;
                    case nameof(ApplePushOptions.PrivateKey): a.PrivateKey = "  "; break;
                    default: a.BundleId = null; break;
                }
            })
            .UseInMemoryStore()));

        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public void Android_requires_its_service_account()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Build(o => o.Android(_ => { }).UseInMemoryStore()));

        Assert.Contains(nameof(AndroidPushOptions.ServiceAccountJson), error.Message);
    }

    [Fact]
    public void Apns_defaults_to_trusting_what_each_installation_reported()
    {
        using var services = Build(o => o.Apple(Apple).UseInMemoryStore());

        Assert.Equal(ApnsEnvironment.PerInstallation,
            services.GetRequiredService<SpinePushOptions>().AppleOptions!.Environment);
    }

    [Fact]
    public void Without_a_policy_the_clients_tags_are_kept_as_they_are()
    {
        var options = new SpinePushOptions();
        var installation = new PushInstallation { Id = "a", Platform = PushPlatform.Apple, Handle = "h" };

        Assert.Equal(["a", "b"], options.FilterTags(installation, ["a", "b"]));
    }

    [Fact]
    public void Allow_tags_narrows_what_a_client_may_register()
    {
        var options = new SpinePushOptions
        {
            AllowTags = (installation, tags) =>
                tags.Where(t => !t.StartsWith("user:", StringComparison.Ordinal) || t == $"user:{installation.UserId}"),
        };

        var installation = new PushInstallation
        {
            Id = "a", Platform = PushPlatform.Apple, Handle = "h", UserId = "121330",
        };

        var kept = options.FilterTags(installation, ["kind:pm", "user:121330", "user:999"]);

        Assert.Equal(["kind:pm", "user:121330"], kept);
    }

    [Fact]
    public void A_register_of_your_own_can_be_supplied()
    {
        var mine = new InMemoryPushInstallationStore();
        using var services = Build(o => o.Apple(Apple).UseStore(_ => mine));

        Assert.Same(mine, services.GetRequiredService<IPushInstallationStore>());
    }
}
