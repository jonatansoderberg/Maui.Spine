using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;
using SpineMessagingSample;

// Handles the "work" subscriber, one trigger per lane, and runs the janitor on a timer. The same declaration as the
// API's, from the shared library.
var builder = FunctionsApplication.CreateBuilder(args);

builder.AddSampleTelemetry(logs: false);
builder.Services.AddSampleMessaging(builder.Configuration);

builder.Build().Run();
