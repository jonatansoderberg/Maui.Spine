using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Backend.Eventor;
using Orientera.Backend.LiveResults;
using Orientera.Backend.Story;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.Configure<EventorOptions>(builder.Configuration.GetSection(EventorOptions.Section));
builder.Services.Configure<LiveResultsOptions>(builder.Configuration.GetSection(LiveResultsOptions.Section));
builder.Services.Configure<StoryOptions>(builder.Configuration.GetSection(StoryOptions.Section));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ResponseCache>();

// Scoped rather than singleton: the typed clients are per-request, and long-lived ones would
// hold their connections — and their DNS answers — for the life of the process.
builder.Services.AddScoped<EventorSource>();
builder.Services.AddScoped<LiveSource>();
builder.Services.AddScoped<RaceStoryWriter>();

builder.Services.AddHttpClient<EventorClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.Add("Accept", "application/xml");
});

builder.Services.AddHttpClient<LiveResultsClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Build().Run();
