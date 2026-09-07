using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class SpinePushEndpointsTests
{
    private static SpinePushOptions Options(Action<SpinePushOptions>? extra = null)
    {
        var options = new SpinePushOptions()
            .Apple(a =>
            {
                a.TeamId = "TEAM123456";
                a.KeyId = "KEY1234567";
                a.PrivateKey = "-----BEGIN PRIVATE KEY-----\nx\n-----END PRIVATE KEY-----";
                a.BundleId = "com.companyname.orientera";
            })
            .UseInMemoryStore();

        extra?.Invoke(options);
        return options;
    }

    private static (HttpContext Context, InMemoryPushInstallationStore Store) Request(
        string method, string path, string? body = null, SpinePushOptions? options = null)
    {
        options ??= Options();
        var store = new InMemoryPushInstallationStore();

        var services = new ServiceCollection();
        services.AddLogging();  // IResult.ExecuteAsync resolves an ILoggerFactory.
        services.AddSingleton(options);
        services.AddSingleton<IPushInstallationStore>(store);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Method = method;
        context.Request.Path = path;
        if (body is not null) context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return (context, store);
    }

    private static async Task<int> StatusOf(IResult result, HttpContext context)
    {
        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }

    private static string Body(string id = "a", params string[] tags) => PushJson.Serialize(new PushInstallation
    {
        Id = id, Platform = PushPlatform.Apple, Handle = "token", Tags = tags, UserId = "121330",
    });

    [Fact]
    public async Task A_put_registers_the_installation()
    {
        var (context, store) = Request("PUT", "/push/installations/a", Body());

        var status = await StatusOf(await SpinePushEndpoints.HandleAsync(context.Request), context);

        Assert.Equal(StatusCodes.Status204NoContent, status);
        Assert.Equal("token", (await store.GetAsync("a"))!.Handle);
    }

    [Fact]
    public async Task A_delete_removes_it()
    {
        var (context, store) = Request("DELETE", "/push/installations/a");
        await store.UpsertAsync(new PushInstallation { Id = "a", Platform = PushPlatform.Apple, Handle = "t" });

        var status = await StatusOf(await SpinePushEndpoints.HandleAsync(context.Request), context);

        Assert.Equal(StatusCodes.Status204NoContent, status);
        Assert.Null(await store.GetAsync("a"));
    }

    [Fact]
    public async Task Deleting_something_that_is_not_there_still_succeeds()
    {
        var (context, _) = Request("DELETE", "/push/installations/nope");
        Assert.Equal(StatusCodes.Status204NoContent, await StatusOf(await SpinePushEndpoints.HandleAsync(context.Request), context));
    }

    [Fact]
    public async Task The_body_must_agree_with_the_path()
    {
        var (context, store) = Request("PUT", "/push/installations/b", Body("a"));

        var status = await StatusOf(await SpinePushEndpoints.HandleAsync(context.Request), context);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task A_body_that_is_not_an_installation_is_a_bad_request()
    {
        var (context, _) = Request("PUT", "/push/installations/a", "{ not json");
        Assert.Equal(StatusCodes.Status400BadRequest, await StatusOf(await SpinePushEndpoints.HandleAsync(context.Request), context));
    }

    [Fact]
    public async Task An_installation_without_a_handle_is_a_bad_request()
    {
        var body = PushJson.Serialize(new PushInstallation { Id = "a", Platform = PushPlatform.Apple, Handle = "" });
        var (context, _) = Request("PUT", "/push/installations/a", body);

        Assert.Equal(StatusCodes.Status400BadRequest, await StatusOf(await SpinePushEndpoints.HandleAsync(context.Request), context));
    }

    [Fact]
    public async Task A_path_without_an_id_is_a_bad_request()
    {
        var (context, _) = Request("PUT", "/push/installations/", Body());
        Assert.Equal(StatusCodes.Status400BadRequest, await StatusOf(await SpinePushEndpoints.HandleAsync(context.Request), context));
    }

    [Fact]
    public async Task Another_method_is_not_allowed()
    {
        var (context, _) = Request("POST", "/push/installations/a", Body());
        Assert.Equal(StatusCodes.Status405MethodNotAllowed, await StatusOf(await SpinePushEndpoints.HandleAsync(context.Request), context));
    }

    [Fact]
    public async Task The_authenticate_hook_can_refuse()
    {
        var options = Options(o => o.Authenticate = _ => ValueTask.FromResult(false));
        var (context, store) = Request("PUT", "/push/installations/a", Body(), options);

        var status = await StatusOf(await SpinePushEndpoints.HandleAsync(context.Request), context);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task The_authenticate_hook_sees_the_request()
    {
        HttpRequest? seen = null;
        var options = Options(o => o.Authenticate = r => { seen = r; return ValueTask.FromResult(true); });
        var (context, _) = Request("PUT", "/push/installations/a", Body(), options);

        await SpinePushEndpoints.HandleAsync(context.Request);

        Assert.Same(context.Request, seen);
    }

    [Fact]
    public async Task The_tag_policy_is_applied_before_the_registration_is_stored()
    {
        var options = Options(o => o.AllowTags = (installation, tags) =>
            tags.Where(t => !t.StartsWith("user:", StringComparison.Ordinal) || t == $"user:{installation.UserId}"));

        var (context, store) = Request("PUT", "/push/installations/a", Body("a", "kind:pm", "user:121330", "user:999"), options);

        await SpinePushEndpoints.HandleAsync(context.Request);

        Assert.Equal(["kind:pm", "user:121330"], (await store.GetAsync("a"))!.Tags);
    }

    [Fact]
    public async Task The_server_stamps_the_registration_with_its_own_clock()
    {
        var stale = PushJson.Serialize(new PushInstallation
        {
            Id = "a", Platform = PushPlatform.Apple, Handle = "t",
            UpdatedAt = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        var (context, store) = Request("PUT", "/push/installations/a", stale);
        await SpinePushEndpoints.HandleAsync(context.Request);

        Assert.True((await store.GetAsync("a"))!.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Theory]
    [InlineData("/push/installations/abc", "abc")]
    [InlineData("/api/v2/push/installations/abc", "abc")]
    [InlineData("/push/installations/abc/", "abc")]
    [InlineData("/push/installations/a%3Ab", "a:b")]
    [InlineData("/push/installations", null)]
    [InlineData("/push/installations/", null)]
    [InlineData("/something/else", null)]
    public void The_id_is_read_from_the_path(string path, string? expected)
    {
        Assert.Equal(expected, SpinePushEndpoints.IdFrom(path));
    }
}
