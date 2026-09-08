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

// The APNs signing key lives in the AppHost's user-secrets rather than in the backend's
// local.settings.json, which is a file in the repo's tree that is easy to commit by accident.
// Aspire hands each one to the backend as an environment variable, so the double underscore is
// what makes it "Push:Apple:*" in configuration.
var appleTeamId = builder.AddParameter("apple-team-id");
var appleKeyId = builder.AddParameter("apple-key-id");
var appleBundleId = builder.AddParameter("apple-bundle-id");
var applePrivateKey = builder.AddParameter("apple-private-key", secret: true);

// Firebase tjänstekonto-JSON. Tom tills det finns ett Firebase-projekt; då kan Android nås.
var androidServiceAccount = builder.AddParameter("android-service-account", secret: true, value: "");

// Bygg inte om Orientera.Backend medan apphosten kör. Aspire startar den med --no-build, och ett
// bygge byter ut assemblies under den levande workern: värden lever vidare och svarar 200 på
// /admin/functions, medan varje funktionsanrop ger 500 med tom kropp, för alltid. Kör om
// `dotnet run --project samples/Orientera.AppHost` efter ett bygge — den bygger själv.
//
// WithHttpHealthCheck gör just det synligt: resursen blir röd i dashboarden i stället för att se
// frisk ut medan ingenting fungerar.
builder.AddAzureFunctionsProject<Projects.Orientera_Backend>("backend")
    .WithHttpHealthCheck("/api/health")
    .WithHostStorage(storage)
    .WithReference(tables)
    .WithReference(queues)
    .WithReference(blobs)
    .WithEnvironment("Push__Apple__TeamId", appleTeamId)
    .WithEnvironment("Push__Apple__KeyId", appleKeyId)
    .WithEnvironment("Push__Apple__BundleId", appleBundleId)
    .WithEnvironment("Push__Apple__PrivateKey", applePrivateKey)
    .WithEnvironment("Push__Android__ServiceAccountJson", androidServiceAccount)
    .WaitFor(storage);

builder.Build().Run();
