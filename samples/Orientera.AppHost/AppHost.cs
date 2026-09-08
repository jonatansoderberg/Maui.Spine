// Local development for Orientera's backend: the Functions host and the storage it needs, started
// together with one command, with the Aspire dashboard for logs and traces.
//
// The MAUI apps are deliberately not here. They run on a device or a simulator and reach the backend
// over the network, so Aspire cannot orchestrate them — the Android emulator reaches the host on
// 10.0.2.2, and the iOS simulator on localhost.
var builder = DistributedApplication.CreateBuilder(args);

// Azurite in a container, so nothing has to be installed globally. The ports are pinned to the
// emulator's well-known ones: WithHostStorage tells the Functions host where storage is, but the
// backend's own queue and blob clients read UseDevelopmentStorage=true from local.settings.json,
// which always means 10000-10002. Without pinning they connect to nothing and the host shuts down
// with "Connection refused (127.0.0.1:10001)".
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite
        .WithBlobPort(10000)
        .WithQueuePort(10001)
        .WithTablePort(10002));

var tables = storage.AddTables("tables");
var queues = storage.AddQueues("queues");
var blobs = storage.AddBlobs("blobs");

builder.AddAzureFunctionsProject<Projects.Orientera_Backend>("backend")
    .WithHostStorage(storage)
    .WithReference(tables)
    .WithReference(queues)
    .WithReference(blobs)
    .WaitFor(storage);

builder.Build().Run();
