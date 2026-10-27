using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Authentication;
using Plugin.Maui.Spine.Server.AI;
using Xunit;

namespace Plugin.Maui.Spine.AI.Tests;

public sealed class ContextTests
{
    private sealed class SignedIn(SpineUser? user) : ISpineSignedInUser
    {
        public SpineUser? User => user;
    }

    private sealed class PushLink(string? id) : ISignedInSession
    {
        public string? UserId => id;
        public Task<string?> GetAuthorizationHeaderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public event EventHandler? Changed { add { } remove { } }
    }

    private static Task<string?> Installation(CancellationToken _) => Task.FromResult<string?>("phone-1");

    [Fact]
    public async Task On_the_device_the_signed_in_user_comes_with_their_name_and_email_without_the_push_link()
    {
        var services = new ServiceCollection().AddSingleton<ISpineSignedInUser>(new SignedIn(new SpineUser("anna", "anna@example.com", "Anna", "apple"))).BuildServiceProvider();

        var context = await SpineAIFunctionContext.ForDeviceAsync(services, Installation, "Assistant", "conv-1", "call_1", CancellationToken.None);

        Assert.Equal(AICaller.SignedIn("anna"), context.Caller);
        var user = await context.GetUserAsync();
        Assert.Equal(("Anna", "anna@example.com"), (user?.DisplayName, user?.Email));
        Assert.Equal(("conv-1", "call_1"), (context.ConversationId, context.CallId));
        Assert.Equal(TimeZoneInfo.Local, context.GetTimeZone());
        Assert.False(string.IsNullOrEmpty(context.TimeZone));
    }

    [Fact]
    public async Task Without_Spine_Authentication_the_push_links_user_and_else_the_installation()
    {
        var linked = new ServiceCollection().AddSingleton<ISignedInSession>(new PushLink("bo")).BuildServiceProvider();
        var nobody = new ServiceCollection().AddSingleton<ISpineSignedInUser>(new SignedIn(null)).BuildServiceProvider();

        Assert.Equal(AICaller.SignedIn("bo"), (await SpineAIFunctionContext.ForDeviceAsync(linked, Installation, null, null, null, CancellationToken.None)).Caller);
        Assert.Equal(AICaller.Installation("phone-1"), (await SpineAIFunctionContext.ForDeviceAsync(nobody, Installation, null, null, null, CancellationToken.None)).Caller);
    }

    [Fact]
    public void Now_is_in_the_users_time_zone_and_on_the_server_UTC_when_the_app_sent_none()
    {
        var tokyo = new SpineAIFunctionContext(AICaller.Anonymous(null), SpineAIFunctionSide.Server) { TimeZone = "Asia/Tokyo" };
        var unknown = new SpineAIFunctionContext(AICaller.Anonymous(null), SpineAIFunctionSide.Server);

        Assert.Equal(TimeSpan.FromHours(9), tokyo.Now().Offset);
        Assert.Equal(TimeSpan.Zero, unknown.Now().Offset);
    }
}
