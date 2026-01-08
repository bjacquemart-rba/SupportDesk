var builder = DistributedApplication.CreateBuilder(args);

var ravenServer = builder.AddRavenDB("ravenServer")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var ravendb = ravenServer.AddDatabase("ravendb");

var api = builder
    .AddProject<Projects.SupportDesk_Api>("api")
    .WithReference(ravendb)
    .WaitFor(ravendb);

builder
    .AddProject<Projects.SupportDesk_ActivityWorker>("activityworker")
    .WithReference(ravendb)
    .WaitFor(api); // Worker waits for API, which will create the database

builder.Build().Run();
