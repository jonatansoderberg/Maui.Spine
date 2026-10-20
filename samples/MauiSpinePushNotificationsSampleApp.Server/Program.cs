using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Anthropic;
using MauiSpinePushNotificationsSampleApp.Server;
using MauiSpineSampleApp.Shared;
using Microsoft.Extensions.AI;
using OpenAI;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Plugin.Maui.Spine.Server.AI;
using Plugin.Maui.Spine.Server.Authentication;

var builder = WebApplication.CreateBuilder(args);

// In Azure (Sample:Hosted, README.md) anyone can reach the server: sign-in is rate limited, users are kept in Table
// storage, and the push endpoints reach only the caller's own devices. On the Mac it stays the open sample it was.
var hosted = builder.Configuration.GetValue<bool>("Sample:Hosted");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = hosted ? 30 : 1000, Window = TimeSpan.FromMinutes(1) }));
});

// The register is in memory: restart the server and every device has to register again, which is
// exactly what a sample wants. Credentials come from user secrets or the environment, never the repo.
builder.Services.AddSpinePushNotifications(o =>
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

    // Windows: an Entra app registration (docs/proposals/spine-push.md §9.3). Nothing registers as
    // Windows until the client exists (#233); configured, the server is ready for it.
    if (builder.Configuration["Push:Wns:ClientSecret"] is { Length: > 0 } secret)
    {
        o.Windows(w =>
        {
            w.TenantId = builder.Configuration["Push:Wns:TenantId"];
            w.ClientId = builder.Configuration["Push:Wns:ClientId"];
            w.ClientSecret = secret;
        });
    }

    o.UseInMemoryStore();

    // A sample server on a laptop has no users to tell apart. A real one authenticates here.
    o.Authenticate = _ => ValueTask.FromResult(true);

    // A device registered with a Spine access token is tagged user:<id> from the token, never from the body.
    o.UserId = request => request.GetSpineUserIdAsync();
});

// Sign-in: Sign in with Apple for the Showcase's bundle id, and email and password with codes written to the log
// (LogEmailSender). The signing key comes from user secrets (Auth:SigningKey); without one the server makes a key
// that lives until it stops, so every session ends with a restart, as the in-memory users do.
builder.Services.AddSingleton<ISpineAuthEmailSender, LogEmailSender>();
builder.Services.AddAuthorization();
builder.Services.AddSpineAuthentication(a =>
{
    a.Issuer = builder.Configuration["Auth:Issuer"] ?? "http://localhost:5100";
    a.SigningKey = builder.Configuration["Auth:SigningKey"] ?? ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem();
    a.PublicUrl = builder.Configuration["Auth:PublicUrl"] ?? "http://localhost:5100/auth";
    a.RedirectSchemes.Add("se.cosmomedia.spineshowcase");
    a.AllowLoopbackRedirects = true;   // the Showcase runs on Windows, which returns to a loopback address
    a.Apple(o =>
    {
        o.BundleIds.Add("se.cosmomedia.spineshowcase");

        // The browser sign-in (Android, Windows) and revocation on deletion need a Services ID and a Sign in with Apple key.
        if (builder.Configuration["Auth:Apple:ServicesId"] is { Length: > 0 } servicesId)
        {
            o.ServicesId = servicesId;
            o.TeamId = builder.Configuration["Auth:Apple:TeamId"];
            o.KeyId = builder.Configuration["Auth:Apple:KeyId"];
            o.PrivateKey = builder.Configuration["Auth:Apple:PrivateKey"];
        }
    });

    // Google through the browser: a web client from the Google Cloud console, with {PublicUrl}/google/callback as its redirect.
    if (builder.Configuration["Auth:Google:WebClientId"] is { Length: > 0 } googleWeb)
    {
        a.Google(o =>
        {
            o.ClientIds.Add(googleWeb);   // Credential Manager on Android issues its tokens for the web client
            o.WebClientId = googleWeb;
            o.WebClientSecret = builder.Configuration["Auth:Google:WebClientSecret"];
        });
    }

    // Microsoft through MSAL in the app: the app registration's client id, and who may sign in.
    if (builder.Configuration["Auth:Microsoft:ClientId"] is { Length: > 0 } microsoftClient)
        a.Microsoft(o => { o.ClientId = microsoftClient; o.Tenant = builder.Configuration["Auth:Microsoft:Tenant"] ?? "common"; });

    a.EmailPassword();

    // Passkeys for the domain the server answers on (the Azure host in appsettings.Production.json). The Showcase built
    // with -p:SpinePasskeyDomain=<that host> may use them once apple-app-site-association names it with the team id.
    if (builder.Configuration["Auth:Passkeys:RelyingPartyId"] is { Length: > 0 } passkeyDomain)
    {
        a.Passkeys(p =>
        {
            p.RelyingPartyId = passkeyDomain;
            p.RelyingPartyName = "Spine Showcase";
            if ((builder.Configuration["Auth:Apple:TeamId"] ?? builder.Configuration["Push:Apple:TeamId"]) is { Length: > 0 } team)
                p.AppleAppIds.Add($"{team}.se.cosmomedia.spineshowcase");
        });
    }

    // A first sign-in with a provider account the server does not know asks "Is this your first time here?", so a
    // user who signed in another way before ends up with one account (the Showcase's sheet asks; #575).
    a.AskBeforeNewAccount = true;

    if (builder.Configuration["Auth:Storage"] is { Length: > 0 } storage)
        a.UseAzureTableStore(storage);
    else
        a.UseInMemoryStore();
});

// AI features: the providers are one line each, everything else (features, models, prices, budgets) is the "Ai"
// section of appsettings.Development.json. Usage is kept in memory, so budgets reset with the server.
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
{
    ai.Chat("demo", (_, _) => new DemoChatClient());
    ai.Chat("anthropic", (p, model) => new AnthropicClient { ApiKey = p["ApiKey"] }.AsIChatClient(model));
    ai.Chat("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetChatClient(model).AsIChatClient());
    ai.Image("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetImageClient(model).AsIImageGenerator());
    ai.Realtime("openai", AIRealtimeProviders.OpenAI);
    ai.Speech("demo", _ => new DemoSpeechProvider());
    ai.Speech("azure", AISpeechProviders.AzureSpeech);
    ai.Speech("openai", AISpeechProviders.OpenAI);
    ai.UseInMemoryUsageStore();

    // The functions the Assistant and Voice features list ("Functions"), shared with the Showcase. get_my_bookings runs
    // here with the signed-in user; show_time runs on the phone or here, as the chat or the voice session chooses.
    ai.Functions.Add(SampleFunctions.GetMyBookings).Add(SampleFunctions.ShowTime).AddDevice(SampleFunctions.ShowTime);

    // The report shows every caller's spending. A sample on a laptop shows it to the laptop only.
    ai.AuthorizeReport = request => ValueTask.FromResult(request.HttpContext.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip));

    // A real server would push this to the owner with IPushSender; the sample logs it.
    ai.OnBudgetThreshold = (alert, services, _) =>
    {
        services.GetRequiredService<ILogger<Program>>().LogWarning("AI budget: {Alert}", alert);
        return Task.CompletedTask;
    };
});

var app = builder.Build();

// PUT and DELETE /push/installations/{id} — what the app registers through.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapSpinePushNotifications("/push");

// GET /auth/nonce, POST /auth/{provider}/token, POST /auth/token, POST /auth/revoke, DELETE /auth/account and
// POST /auth/password/{signup,verify,resend,signin,forgot,reset}, POST /auth/passkey/{register,signin}[/options].
app.MapSpineAuthentication("/auth").RequireRateLimiting("auth");

// GET /.well-known/apple-app-site-association: the Showcase may use this domain's passkeys.
app.MapSpinePasskeyAssociations();

// Hosted, the push endpoints are for a signed-in user and their own devices; on the Mac, for anyone and every device.
async ValueTask<(bool Allowed, string? UserId)> PushCallerAsync(HttpRequest request) =>
    hosted ? await request.GetSpineUserIdAsync() is { } id ? (true, id) : (false, null) : (true, null);

// Who the access token says you are. Signed-in users also get the AI features' SignedIn limits.
app.MapGet("/sample/me", (ClaimsPrincipal user) => new
{
    id = user.FindFirstValue("sub"),
    provider = user.FindFirstValue(SpineAuthDefaults.ProviderClaim),
    email = user.FindFirstValue("email"),
}).RequireAuthorization();

// POST /ai/features/{name} for exposed features (Assistant), POST /ai/functions/{feature}/{name} for the Voice feature's
// server functions, and GET /ai/usage for the report.
app.MapSpineAI("/ai");

// POST /ai/realtime/{name}: a short-lived token for a realtime voice session (Voice, VoiceFull), or a ticket for the
// relay a GPT-Live session talks through (VoiceLive, /ai/realtime/{name}/relay). The OpenAI key stays here.
app.UseWebSockets();
app.MapSpineRealtimeSessions("/ai");

/// A feature the server owns: the app sends facts, the server writes the prompt. The caller is whoever the request
/// is for, so the per-installation limits apply here as they do on the exposed route.
app.MapPost("/sample/summary", async (SummaryRequest request, HttpRequest http, IAIFeatures ai, CancellationToken cancellationToken) =>
{
    var caller = await ai.IdentifyAsync(http);
    if (ai.Chat(request.Feature ?? "Summary", caller) is not { } summary)
        return Results.NotFound();

    try
    {
        var answer = await summary.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, "Sammanfatta punkterna i en mening på svenska. Lägg inte till något."),
                new ChatMessage(ChatRole.User, string.Join('\n', request.Facts.Select(f => $"- {f}"))),
            ],
            cancellationToken: cancellationToken);

        return Results.Ok(new { answer.Text, answer.Usage });
    }
    catch (AIFeatureException e)
    {
        return e.ToResult();
    }
});

/// One picture from the Picture feature, as PNG.
app.MapPost("/sample/picture", async (PictureRequest request, HttpRequest http, IAIFeatures ai, CancellationToken cancellationToken) =>
{
    if (ai.Image("Picture", await ai.IdentifyAsync(http)) is not { } painter)
        return Results.NotFound();

    try
    {
        var result = await painter.GenerateImagesAsync(request.Prompt, cancellationToken: cancellationToken);
        return result.Contents.OfType<DataContent>().FirstOrDefault() is { } image
            ? Results.File(image.Data.ToArray(), image.MediaType)
            : Results.Problem("The provider answered without a picture.");
    }
    catch (AIFeatureException e)
    {
        return e.ToResult();
    }
});

/// Sends one message and answers with what happened per installation.
app.MapPost("/send", async (SendRequest request, HttpRequest http, IPushSender sender, SpinePushNotificationsOptions options, CancellationToken cancellationToken) =>
{
    var (allowed, userId) = await PushCallerAsync(http);
    if (!allowed) return Results.Unauthorized();
    if (userId is not null && request.Kind == "broadcast")
        return Results.BadRequest("A broadcast reaches everyone following the channel, so the hosted server does not send one.");

    // The wait is here rather than in the app because the point of it is to let the caller put the
    // app in the background — and a backgrounded app is exactly what cannot be relied on to still be
    // running a timer. Capped so a stray value cannot tie up the request for long.
    if (request.DelaySeconds is > 0 and <= 60)
        await Task.Delay(TimeSpan.FromSeconds(request.DelaySeconds.Value), cancellationToken);

    var target = request.Target switch
    {
        _ when userId is not null => PushTarget.User(userId),
        { Length: > 0 } expression when request.TargetKind == "installation" => PushTarget.Installation(expression),
        { Length: > 0 } expression => PushTarget.Tags(expression),
        _ => PushTarget.All,
    };

    if (request.Kind == "broadcast" && request.BroadcastChannel is not { Length: > 0 })
        return Results.BadRequest("A broadcast needs broadcastChannel: the id POST /channels answered.");

    var result = request.Kind switch
    {
        // One push to everyone following the channel, however many they are — not one per activity
        // token. The sample's apps are development builds, so their channels live in the sandbox.
        "broadcast" => await sender.BroadcastLiveActivityAsync(
            request.BroadcastChannel!,
            request.ActivityKind ?? "sample",
            Layout(request),
            LiveActivityEvent.Update,
            new LiveActivityOptions { StaleAt = DateTimeOffset.UtcNow.AddMinutes(10) },
            ApnsEnvironment.Sandbox,
            cancellationToken),

        "silent" => await sender.SendSilentAsync(target, request.Data ?? [], cancellationToken),

        "widget" => await sender.RefreshWidgetsAsync(target, request.WidgetKind, cancellationToken),

        "liveactivity" => await sender.UpdateLiveActivityAsync(
            target,
            request.ActivityKind ?? "sample",
            Layout(request),
            LiveActivityEvent.Update,
            // With broadcastChannel the activity follows that channel: an Android device that is not
            // running it starts it on the channel, and later broadcasts reach it through the topic.
            new LiveActivityOptions { StaleAt = DateTimeOffset.UtcNow.AddMinutes(10), Channel = request.BroadcastChannel },
            cancellationToken),

        _ => await sender.SendAsync(target, new PushNotification
        {
            Title = request.Title ?? "Hej",
            Body = request.Body ?? "Ett meddelande från sample-servern.",
            Route = request.Route,
            Channel = request.Channel,
            Category = request.Category,
            Image = request.Image is { Length: > 0 } image ? new Uri(image) : null,
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

/// The broadcast channel the Live Activity page's "Starta på kanal" follows. One per server run: every
/// activity started on it follows the same channel, which is what makes one push reach them all. Made
/// at the first request rather than at startup, so a server without Apple credentials still starts.
string? sampleChannel = null;
app.MapPost("/channels", async (HttpRequest http, IServiceProvider services, CancellationToken cancellationToken) =>
{
    if (!(await PushCallerAsync(http)).Allowed) return Results.Unauthorized();

    if (services.GetService<IPushChannels>() is not { } channels)
        return Results.Problem("Apple is not configured, so there are no channels. Put Apple credentials in user secrets.");

    try
    {
        sampleChannel ??= await channels.CreateAsync(PushChannelStorage.MostRecent, ApnsEnvironment.Sandbox, cancellationToken);
        return Results.Ok(new { Channel = sampleChannel });
    }
    catch (PushChannelException e)
    {
        return Results.Problem(e.Message, statusCode: (int)e.StatusCode);
    }
});

/// Avatars too large to bundle with the Showcase (Kairo), downloaded by its Avatars page and cached there. The files are
/// published next to the server (the csproj links them from samples/Avatars, the repository's one copy); anything else is
/// not served. GET /sample/avatars lists them with the name, profile and version each package's manifest names, its size and its SHA-256,
/// so an app compares the version with its cached copy's and downloads a newer one (and checks the hash).
var avatars = AvatarCatalog.Read(Path.Combine(AppContext.BaseDirectory, "avatars"));
// No caching: the list says which version is current, and a phone that kept an old answer never saw an update.
app.MapGet("/sample/avatars", (HttpResponse response) =>
{
    response.Headers.CacheControl = "no-cache";
    return Results.Ok(avatars.Values);
});
// The posters, so the app can show an avatar it has not downloaded yet.
app.MapGet("/sample/avatars/{id}/poster-{theme}.png", (string id, string theme) =>
    avatars.TryGetValue(id, out var avatar) && avatar.Posters.TryGetValue(theme, out var png)
        ? Results.File(png, "image/png")
        : Results.NotFound());
app.MapGet("/sample/avatars/{name}", (string name) =>
{
    if (!avatars.TryGetValue(Path.GetFileNameWithoutExtension(name), out var avatar) || name != avatar.File)
        return Results.NotFound();
    var path = Path.Combine(AppContext.BaseDirectory, "avatars", avatar.File);
    return File.Exists(path)
        ? Results.File(path, "application/zip", avatar.File, entityTag: new($"\"{avatar.Sha256}\""), enableRangeProcessing: true)
        : Results.Problem($"{avatar.File} is not in {Path.GetDirectoryName(path)}.", statusCode: StatusCodes.Status404NotFound);
});

/// The remote widget's content. The platform fetches this itself at every reload — the widget
/// extension on iOS, a receiver in the app's process on Android — so what the widget shows can change
/// while the app is not running. The clock says the answer is fresh; "Från servern" says where it came from.
app.MapGet("/widget/remote", () =>
{
    var now = DateTimeOffset.Now;

    var white = WidgetColor.FromHex("#FFFFFF");
    var mint = WidgetColor.FromHex("#CFF5E3");

    var timeline = WidgetTimeline
        .Single(W.VStack(4,
            W.Text("Spine remote").Caption().Color(mint),
            // The accent group when iOS draws the widget tinted or clear; iOS 26 draws both groups white.
            W.Text("Från servern").Headline().Bold().Color(white).Accented(),
            W.Text($"Hämtad {now:HH:mm:ss}").Caption().Color(white),
            W.Relative(now).Caption().Color(mint)))
        // Only the server's answer has the gradient, so a widget that fell back to the app's reserve looks it.
        .Background(new WidgetGradient([WidgetColor.FromHex("#1B5E3F"), WidgetColor.FromHex("#3FA37A")], WidgetGradientDirection.Diagonal))
        .Refresh(TimeSpan.FromMinutes(15));

    return Results.Text(timeline.ToJson(), "application/json");
});

/// What the register currently holds, so the app can show whether it got through.
app.MapGet("/installations", async (HttpRequest http, IPushInstallationStore store, CancellationToken cancellationToken) =>
{
    var (allowed, userId) = await PushCallerAsync(http);
    if (!allowed) return Results.Unauthorized();

    var found = new List<object>();

    var whose = userId is null ? PushTagExpression.MatchAll : PushTagExpression.Parse($"user:{userId}");
    await foreach (var installation in store.QueryAsync(whose, cancellationToken: cancellationToken))
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

            // Present when the app runs on iOS 26 with SpineWidgetsPush: widget refreshes to it go as a
            // widgets push, which WidgetKit acts on without the app.
            WidgetToken = installation.WidgetToken is { Length: > 0 } widget ? Shorten(widget) : null,

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
/// an update that did arrive looks like one that never came. The background and the fixed text
/// colors match the app's own layout: an update replaces the whole layout, background included.
/// </remarks>
static LiveActivityLayout Layout(SendRequest request) => new()
{
    LockScreen = W.VStack(4,
        W.Text(request.Title ?? "Live Activity").Headline().Bold().Color(WidgetColor.FromHex("#FFFFFF")),
        W.Text(request.Body ?? "Uppdaterad av servern").Caption().Color(WidgetColor.FromHex("#B3FFFFFF")),
        W.Relative(DateTimeOffset.Now).Caption().Color(WidgetColor.FromHex("#B3FFFFFF"))),
    Background = WidgetColor.FromHex("#1B5E3F"),
    // All four expanded slots, or a long press on the Dynamic Island opens to nothing.
    ExpandedLeading = W.Icon("bell"),
    ExpandedTrailing = W.Relative(DateTimeOffset.Now, compact: true).Caption(),
    ExpandedCenter = W.Text(request.Title ?? "Live Activity").Headline().Bold(),
    ExpandedBottom = W.Text(request.Body ?? "Uppdaterad av servern").Caption().Secondary(),
    CompactLeading = W.Icon("bell"),

    // Compact has room for a glance, not a sentence: a short stamp of when this arrived. The ticking
    // freshness lives in the expanded view, where there is room for it.
    CompactTrailing = W.Text($"{DateTimeOffset.Now:HH:mm}").Caption(),
    Minimal = W.Icon("bell"),
};

/// <param name="Facts">The points to summarise.</param>
/// <param name="Feature">Summary (demo) by default; Story (Anthropic) or Quick (OpenAI) once their keys are set.</param>
internal sealed record SummaryRequest(string[] Facts, string? Feature);

/// <param name="Prompt">What to paint.</param>
internal sealed record PictureRequest(string Prompt);

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
    int? DelaySeconds,
    string? Category = null,
    string? Image = null,
    string? BroadcastChannel = null);
