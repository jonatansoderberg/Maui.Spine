using System.Runtime.InteropServices;

// Spine messaging under Aspire: an API that publishes, a Functions app that handles, and either Azure Storage
// (Azurite) or Azure Service Bus (its emulator, which needs Docker) as the transport. Pick it with
//   dotnet run --project samples/SpineMessagingSample.AppHost -- --Messaging:Transport=servicebus
// Storage is the default. Blob storage runs either way: it holds large payloads and failed messages.
var builder = DistributedApplication.CreateBuilder(args);
var transport = builder.Configuration["Messaging:Transport"] ?? "storage";

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var queues = storage.AddQueues("queues");
var blobs = storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.SpineMessagingSample_Api>("api")
    .WithEnvironment("Messaging__Transport", transport)
    .WithReference(blobs)
    .WaitFor(blobs)
    .WithHttpHealthCheck("/health");

// The API makes postcards once it has an OpenAI key: start the AppHost with Ai__Providers__openai__ApiKey set.
if (builder.Configuration["Ai:Providers:openai:ApiKey"] is { Length: > 0 } openAIKey)
    api.WithEnvironment("Ai__Providers__openai__ApiKey", openAIKey);

var functions = builder.AddAzureFunctionsProject<Projects.SpineMessagingSample_Functions>("functions")
    .WithHostStorage(storage)
    .WithEnvironment("Messaging__Transport", transport)
    .WithReference(blobs)
    // The API provisions the queues or subscriptions as it starts, so the triggers find them.
    .WaitFor(api);

string[] off;
if (transport == "servicebus")
{
    var servicebus = builder.AddAzureServiceBus("servicebus").RunAsEmulator();

    // The emulator keeps its state in SQL Server, whose image crashes under emulation on Apple silicon. Azure SQL Edge
    // runs natively there and is all the emulator needs.
    if (RuntimeInformation.OSArchitecture == Architecture.Arm64
        && builder.Resources.OfType<ContainerResource>().FirstOrDefault(r => r.Name == "servicebus-mssql") is { } sql)
    {
        builder.CreateResourceBuilder(sql).WithImageRegistry("mcr.microsoft.com").WithImage("azure-sql-edge").WithImageTag("latest");
    }

    // The emulator takes administration calls on another port than AMQP, which Aspire maps. Both apps provision.
    var management = servicebus.GetEndpoint("emulatorhealth");
    api.WithReference(servicebus).WaitFor(servicebus).WithEnvironment("Messaging__ServiceBusManagement", management);
    functions.WithReference(servicebus).WithEnvironment("Messaging__ServiceBusManagement", management);
    off = ["WorkHighQueue", "WorkQueue", "WorkLowQueue"];
}
else
{
    api.WithReference(queues);
    functions.WithReference(queues);
    off = ["WorkHighServiceBus", "WorkServiceBus", "WorkLowServiceBus"];
}

// The other transport's triggers stay in the app but do not start.
foreach (var function in off)
    functions.WithEnvironment($"AzureWebJobs.{function}.Disabled", "true");

builder.Build().Run();
