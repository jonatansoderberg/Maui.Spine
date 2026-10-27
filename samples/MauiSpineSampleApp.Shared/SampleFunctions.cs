using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.AI;

namespace MauiSpineSampleApp.Shared;

/// <summary>
/// The Showcase's AI functions. The push sample server runs <see cref="GetMyBookings"/> with the signed-in user; the app
/// runs <see cref="ShowTime"/>, which the server also has, so a chat or a voice session can choose the side.
/// </summary>
public static class SampleFunctions
{
    private static readonly string[] Places = ["Tvättstugan", "Padelbana 2", "Frisören på hörnet", "Bastun", "Mötesrum Ekan", "Gästlägenheten"];

    /// <summary>The signed-in user's bookings: made up, but the same for the same user, and with their name and email.</summary>
    public static SpineAIFunction GetMyBookings { get; } = SpineAIFunction.Create(
        "get_my_bookings",
        "Lists the signed-in user's bookings in the coming days: what, when, and the name and email they are booked under. Call it when the user asks about their bookings, reservations or what they have booked.",
        GetMyBookingsAsync,
        SpineAIFunctionSide.Server);

    /// <summary>The time, and where the clock was read: the phone or the server.</summary>
    public static SpineAIFunction ShowTime { get; } = SpineAIFunction.Create(
        "show_time",
        "Says what time it is for the user, and where the clock was read. Call it when the user asks what time it is.",
        ReadClock,
        SpineAIFunctionSide.Device);

    private static async Task<JsonElement> GetMyBookingsAsync(
        SpineAIFunctionContext context, [Description("How many days ahead to look; 14 when the user does not say.")] int days = 14)
    {
        if (await context.GetUserAsync() is not { } user)
            return SpineAIFunctionResult.SignInRequired("The user is not signed in. Tell them to sign in on the Showcase's Sign-in page to see their bookings.");

        // The same user always gets the same bookings, spread over the coming days.
        var seed = user.Id.Aggregate(17, (hash, c) => unchecked(hash * 31 + c));
        var random = new Random(seed);
        // The user's today, wherever the function runs.
        var today = context.Now().Date;
        var bookings = new JsonArray();

        foreach (var day in Enumerable.Range(1, Math.Clamp(days, 1, 60)).Where(_ => random.Next(4) == 0).Take(5))
        {
            var at = today.AddDays(day).AddHours(8 + random.Next(12));
            bookings.Add(new JsonObject
            {
                ["what"] = Places[random.Next(Places.Length)],
                ["when"] = at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            });
        }

        var answer = new JsonObject
        {
            ["bookedAs"] = user.DisplayName ?? user.Email ?? user.Id,
            ["email"] = user.Email,
            ["days"] = days,
            ["bookings"] = bookings,
        };

        using var json = JsonDocument.Parse(answer.ToJsonString());
        return json.RootElement.Clone();
    }

    // The user's time either way: on the server in the time zone the app sent, not the server's.
    private static string ReadClock(SpineAIFunctionContext context) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{context.Now():HH:mm} ({context.GetTimeZone().Id}), read on {(context.RunsOn == SpineAIFunctionSide.Device ? "the user's phone" : "the server")}");
}
