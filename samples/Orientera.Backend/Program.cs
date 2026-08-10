using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orientera.Backend.Caching;
using Orientera.Backend.Configuration;
using Orientera.Backend.Eventor;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.Configure<EventorOptions>(builder.Configuration.GetSection(EventorOptions.Section));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ResponseCache>();

// Scoped rather than singleton: the typed client is per-request, and a long-lived one would
// hold its connections — and its DNS answers — for the life of the process.
builder.Services.AddScoped<EventorSource>();

builder.Services.AddHttpClient<EventorClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.Add("Accept", "application/xml");
});

builder.Build().Run();
