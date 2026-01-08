using Raven.Client.Documents;
using SupportDesk.ActivityWorker;
using SupportDesk.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddRavenDBClient(connectionName: "ravendb");

builder.Services.AddHostedService<ActivityProjectionWorker>();

var host = builder.Build();

// Ensure database exists before starting workers
var store = host.Services.GetRequiredService<IDocumentStore>();
await store.EnsureDatabaseExistsAsync();

await host.RunAsync();
