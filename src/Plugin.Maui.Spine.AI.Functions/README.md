# Plugin.Maui.Spine.AI.Functions

Functions the model calls, declared once for a Spine app and its server. A function runs on the device, on the server, or on either, in an order the app can change per conversation. On the server it knows who is calling: the signed-in user's id, claims and details reach it through a context the model never sees. Every call is logged.

```bash
dotnet add package Plugin.Maui.Spine.AI.Functions
```

```csharp
// In a project the app and the server both reference.
public static class Functions
{
    public static SpineAIFunction GetMyBookings { get; } = SpineAIFunction.Create(
        "get_my_bookings", "Lists the signed-in user's bookings.", GetMyBookingsAsync, SpineAIFunctionSide.Server);

    static async Task<object> GetMyBookingsAsync(SpineAIFunctionContext context, int? days)
    {
        if (await context.GetUserAsync() is not { } user) return SpineAIFunctionResult.SignInRequired();
        return await Bookings.ForAsync(user.Id, days ?? 30, context.CancellationToken);
    }
}

// The server:  AddSpineAI(..., ai => ai.Functions.Add(Functions.GetMyBookings));  and "Functions": ["get_my_bookings"] on a feature.
// The app:     UseSpineAI(o => o.Functions.Add(Functions.ShowTime));               shared with UseSpineVoice.
```

Plugin.Maui.Spine.AI, .Voice and .Server.AI reference it. See the [AI server](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-server.md#functions) and [AI client](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-client.md#functions) pages.
