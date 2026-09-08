using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Orientera.Backend.Arena;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Backend.Eventor;
using Orientera.Backend.Livelox;
using Orientera.Backend.LiveResults;
using Orientera.Backend.Push;
using Orientera.Backend.Ranking;
using Orientera.Backend.Story;
using Plugin.Maui.Spine.Server;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.Configure<EventorOptions>(builder.Configuration.GetSection(EventorOptions.Section));
builder.Services.Configure<LiveResultsOptions>(builder.Configuration.GetSection(LiveResultsOptions.Section));
builder.Services.Configure<StoryOptions>(builder.Configuration.GetSection(StoryOptions.Section));
builder.Services.Configure<LiveloxOptions>(builder.Configuration.GetSection(LiveloxOptions.Section));
builder.Services.Configure<RankingOptions>(builder.Configuration.GetSection(RankingOptions.Section));
builder.Services.Configure<ArenaImageOptions>(builder.Configuration.GetSection(ArenaImageOptions.Section));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ResponseCache>();
builder.Services.AddSingleton(TimeProvider.System);

// Push. Lagringen är samma konto som värden själv använder, och nyckeln kommer som miljövariabler
// — lokalt från apphostens user-secrets, i drift från appinställningarna. Utan nyckel finns
// registret och endpointen ändå: telefoner kan registrera sig, det är bara ingen som skickar.
var storage = builder.Configuration["AzureWebJobsStorage"] is { Length: > 0 } connection
    ? connection
    : "UseDevelopmentStorage=true";

builder.Services.AddSpinePush(push =>
{
    push.UseAzureTableStore(storage, "OrienteraPush");

    if (builder.Configuration["Push:Apple:PrivateKey"] is { Length: > 0 } privateKey)
    {
        push.Apple(apple =>
        {
            apple.TeamId = builder.Configuration["Push:Apple:TeamId"];
            apple.KeyId = builder.Configuration["Push:Apple:KeyId"];
            apple.BundleId = builder.Configuration["Push:Apple:BundleId"];
            apple.PrivateKey = privateKey;
        });
    }

    // Endpointen är öppen, som resten av backendens API. En tagg är därför bara ett önskemål:
    // user: filtreras bort tills registreringen är autentiserad, så ingen kan lyssna som någon
    // annan. kind: och competition: är inte hemliga — de säger bara vad telefonen vill höra om.
    push.AllowTags = (_, tags) => tags.Where(t => !t.StartsWith("user:", StringComparison.Ordinal));
});

builder.Services.AddSingleton(_ => new AnnouncedStore(storage));

// Scoped rather than singleton: the typed clients are per-request, and long-lived ones would
// hold their connections — and their DNS answers — for the life of the process.
builder.Services.AddScoped<EventorSource>();
builder.Services.AddScoped<LiveSource>();
builder.Services.AddScoped<RaceStoryWriter>();
builder.Services.AddScoped<PeopleSearch>();
builder.Services.AddScoped<StartFieldSource>();
builder.Services.AddScoped<ArenaImageStore>();
builder.Services.AddScoped<TerrainSource>();
builder.Services.AddScoped<ArenaComposer>();

// The organisation list is fetched while the host starts rather than by whoever asks first.
builder.Services.AddHostedService<DirectoryWarmup>();

builder.Services.AddHttpClient<EventorClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.Add("Accept", "application/xml");
});

builder.Services.AddHttpClient<LiveloxSource>((sp, client) =>
{
    client.BaseAddress = new Uri(
        sp.GetRequiredService<IOptions<LiveloxOptions>>().Value.BaseAddress);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<RankingScraper>((sp, client) =>
{
    client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<RankingOptions>>().Value.BaseAddress);
    client.Timeout = TimeSpan.FromSeconds(20);
});

// The entry list is a public Eventor web page, so it rides on the same web base address as the
// ranking pages rather than on the API client — the API refuses entries to a club's key.
builder.Services.AddHttpClient<EntryListSource>((sp, client) =>
{
    client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<RankingOptions>>().Value.BaseAddress);
    client.Timeout = TimeSpan.FromSeconds(20);
});

// Höjdrutorna är stora och dl1 svarar långsamt under last, så gränsen ligger i minuter.
builder.Services.AddHttpClient<LantmaterietClient>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(5);
    })
    .AddTypedClient((http, provider) => new LantmaterietClient(
        http,
        provider.GetRequiredService<IOptions<ArenaImageOptions>>().Value.CacheDirectory
            is { Length: > 0 } directory
            ? directory
            : Path.Combine(Path.GetTempPath(), "arenabild-cache"),
        GeotorgetCredentials.Find(),
        provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LantmaterietClient>>()));

builder.Services.AddHttpClient<EventorArenaPage>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});

builder.Services.AddHttpClient<LiveResultsClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Build().Run();
