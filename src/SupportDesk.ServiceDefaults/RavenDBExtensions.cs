using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;

namespace SupportDesk.ServiceDefaults;

public static class RavenDBExtensions
{
    public static async Task EnsureDatabaseExistsAsync(this IDocumentStore store, CancellationToken cancellationToken = default)
    {
        var database = store.Database;
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("Database name is not configured in DocumentStore");

        try
        {
            // Check if database exists by getting its statistics
            await store.Maintenance.ForDatabase(database).SendAsync(
                new GetStatisticsOperation(), 
                cancellationToken);
        }
        catch (Raven.Client.Exceptions.Database.DatabaseDoesNotExistException)
        {
            // Database doesn't exist, try to create it
            try
            {
                await store.Maintenance.Server.SendAsync(
                    new CreateDatabaseOperation(new DatabaseRecord(database)), 
                    cancellationToken);
            }
            catch (Raven.Client.Exceptions.ConcurrencyException)
            {
                // Another instance created it between our check and create - this is fine
                // Verify it exists now
                await store.Maintenance.ForDatabase(database).SendAsync(
                    new GetStatisticsOperation(), 
                    cancellationToken);
            }
        }
    }

    public static void DeployIndexes(this IDocumentStore store, System.Reflection.Assembly assembly)
    {
        IndexCreation.CreateIndexes(assembly, store);
    }
}
