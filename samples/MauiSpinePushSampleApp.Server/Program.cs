using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;

var builder = WebApplication.CreateBuilder(args);

// The register is in memory: restart the server and every device has to register again, which is
// exactly what a sample wants. Credentials come from user secrets or the environment, never the repo.
builder.Services.AddSpinePush(o =>
{
    if (builder.Configuration["Push:Apple:TeamId"] is { Length: > 0 })
    {
        o.Apple(a =>
        {
            a.TeamId = builder.Configuration["Push:Apple:TeamId"];
            a.KeyId = builder.Configuration["Push:Apple:KeyId"];
            a.PrivateKey = builder.Configuration["Push:Apple:PrivateKey"];
            a.BundleId = builder.Configuration["Push:Apple:BundleId"];
        });
    }

    if (builder.Configuration["Push:Fcm:ServiceAccount"] is { Length: > 0 } serviceAccount)
        o.Android(f => f.ServiceAccountJson = serviceAccount);

    o.UseInMemoryStore();

    // A sample server on a laptop has no users to tell apart. A real one authenticates here.
    o.Authenticate = _ => ValueTask.FromResult(true);
});

var app = builder.Build();

// PUT and DELETE /push/installations/{id} — what the app registers through.
app.MapSpinePush("/push");

/// Sends one message and answers with what happened per installation.
app.MapPost("/send", async (SendRequest request, IPushSender sender, SpinePushOptions options, CancellationToken cancellationToken) =>
{
    // The wait is here rather than in the app because the point of it is to let the caller put the
    // app in the background — and a backgrounded app is exactly what cannot be relied on to still be
    // running a timer. Capped so a stray value cannot tie up the request for long.
    if (request.DelaySeconds is > 0 and <= 60)
        await Task.Delay(TimeSpan.FromSeconds(request.DelaySeconds.Value), cancellationToken);

    var target = request.Target switch
    {
        { Length: > 0 } expression when request.TargetKind == "installation" => PushTarget.Installation(expression),
        { Length: > 0 } expression => PushTarget.Tags(expression),
        _ => PushTarget.All,
    };

    var result = request.Kind switch
    {
        "silent" => await sender.SendSilentAsync(target, request.Data ?? [], cancellationToken),

        "widget" => await sender.RefreshWidgetsAsync(target, request.WidgetKind, cancellationToken),

        "liveactivity" => await sender.UpdateLiveActivityAsync(
            target,
            request.ActivityKind ?? "sample",
            Layout(request),
            LiveActivityEvent.Update,
            new LiveActivityOptions { StaleAt = DateTimeOffset.UtcNow.AddMinutes(10) },
            cancellationToken),

        _ => await sender.SendAsync(target, new PushNotification
        {
            Title = request.Title ?? "Hej",
            Body = request.Body ?? "Ett meddelande från sample-servern.",
            Route = request.Route,
            Channel = request.Channel,
            Priority = request.HighPriority ? PushPriority.High : PushPriority.Normal,
            Data = request.Data ?? new Dictionary<string, string>(),
        }, cancellationToken),
    };

    return Results.Ok(new
    {
        result.Sent,
        result.Invalid,
        result.Throttled,
        result.Failed,
        Deliveries = result.Deliveries.Select(d => new { d.InstallationId, Platform = d.Platform.ToString(), Status = d.Status.ToString(), d.Reason }),

        // Without credentials there is no transport, so a send reaches nobody however many devices
        // are registered. Saying so here saves the next person a confusing half hour.
        Note = result.Deliveries.Count == 0 && options.AppleOptions is null && options.AndroidOptions is null
            ? "No platform is configured, so nothing was sent. Put Apple or Fcm credentials in user secrets."
            : null,
    });
});

/// What the register currently holds, so the app can show whether it got through.
app.MapGet("/installations", async (IPushInstallationStore store, CancellationToken cancellationToken) =>
{
    var found = new List<object>();

    await foreach (var installation in store.QueryAsync(PushTagExpression.MatchAll, cancellationToken: cancellationToken))
    {
        found.Add(new
        {
            installation.Id,
            Platform = installation.Platform.ToString(),
            Handle = Shorten(installation.Handle),
            Environment = installation.Environment?.ToString(),
            installation.Tags,
            installation.AppVersion,
            installation.OsVersion,
            installation.UpdatedAt,

            // Whether a Live Activity can be addressed at all. Without this a send answers
            // NoLiveActivityToken and the register gives no hint as to which half is missing:
            // the push-to-start token, or the running activity's own.
            LiveActivities = installation.LiveActivities is { } tokens
                ? new
                {
                    PushToStart = tokens.PushToStart is { Length: > 0 } start ? Shorten(start) : null,
                    Activities = tokens.Activities.ToDictionary(a => a.Key, a => Shorten(a.Value)),
                }
                : null,
        });
    }

    return Results.Ok(found);
});

app.Run();

static string Shorten(string handle) =>
    handle.Length <= 16 ? handle : $"{handle[..8]}…{handle[^8..]}";

/// <summary>
/// The layout the sample updates its Live Activity with; the same C# the app would build.
/// </summary>
/// <remarks>
/// The freshness line is a <see cref="W.Relative"/> node rather than a formatted time: the system
/// draws a Live Activity while the app is not running, so a stamped string never changes again and
/// an update that did arrive looks like one that never came.
/// </remarks>
static LiveActivityLayout Layout(SendRequest request) => new()
{
    LockScreen = W.VStack(4,
        W.Text(request.Title ?? "Live Activity").Headline().Bold(),
        W.Text(request.Body ?? "Uppdaterad av servern").Caption().Secondary(),
        W.Relative(DateTimeOffset.Now).Caption().Secondary()),
    // All four expanded slots, or a long press on the Dynamic Island opens to nothing.
    ExpandedLeading = W.Icon("bell"),
    ExpandedTrailing = W.Relative(DateTimeOffset.Now).Caption(),
    ExpandedCenter = W.Text(request.Title ?? "Live Activity").Headline().Bold(),
    ExpandedBottom = W.Text(request.Body ?? "Uppdaterad av servern").Caption().Secondary(),
    CompactLeading = W.Icon("bell"),
    CompactTrailing = W.Relative(DateTimeOffset.Now).Caption(),
    Minimal = W.Icon("bell"),
};

/// <param name="Kind">alert, silent, liveactivity or widget.</param>
/// <param name="TargetKind">tags (the default) or installation.</param>
/// <param name="Target">A tag expression, or an installation id. Empty reaches everyone.</param>
/// <param name="Title">The notification's first line.</param>
/// <param name="Body">The notification's body.</param>
/// <param name="Route">The page the app should open when it is tapped.</param>
/// <param name="Channel">The Android channel, and the thread id on iOS.</param>
/// <param name="HighPriority">Whether to ask for immediate delivery.</param>
/// <param name="WidgetKind">Which widget to rebuild; all of them when absent.</param>
/// <param name="ActivityKind">Which Live Activity to update.</param>
/// <param name="Data">Extra values handed to the app's handler.</param>
/// <param name="DelaySeconds">Seconds to wait before sending, so the caller can background the app first. 1-60.</param>
internal sealed record SendRequest(
    string? Kind,
    string? TargetKind,
    string? Target,
    string? Title,
    string? Body,
    string? Route,
    string? Channel,
    bool HighPriority,
    string? WidgetKind,
    string? ActivityKind,
    Dictionary<string, string>? Data,
    int? DelaySeconds);
