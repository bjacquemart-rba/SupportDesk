using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Subscriptions;
using SupportDesk.Contracts.Tickets;

namespace SupportDesk.ActivityWorker;

internal static partial class  ActivityProjectionWorkerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Subscription started: {Name}")]
    internal static partial void SubscriptionStarted(ILogger logger, string name);
}

public sealed class ActivityProjectionWorker(IDocumentStore store, ILogger<ActivityProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subName = "TicketActivityProjection";

        var database = store.Database;
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("Raven DocumentStore.Database is not set.");

        await EnsureSubscriptionExists(subName, database, stoppingToken);

        var worker = store.Subscriptions.GetSubscriptionWorker<TicketActivityEvent>(
            new SubscriptionWorkerOptions(subName)
            {
                TimeToWaitBeforeConnectionRetry = TimeSpan.FromSeconds(5)
            });

        ActivityProjectionWorkerLog.SubscriptionStarted(logger, subName);

        await worker.Run(async batch =>
        {
            using var session = batch.OpenAsyncSession();

            foreach (var item in batch.Items)
                await ApplyEvent(session, item.Result, stoppingToken);

            await session.SaveChangesAsync(stoppingToken);
        }, stoppingToken);
    }
    
    private async Task EnsureSubscriptionExists(string name, string database, CancellationToken ct)
    {
        // Fast path: already exists?
        var existing = await store.Subscriptions.GetSubscriptionsAsync(0, 128, database, ct);
        if (existing.Any(s => string.Equals(s.SubscriptionName, name, StringComparison.Ordinal)))
            return;

        try
        {
            await store.Subscriptions.CreateAsync(
                new SubscriptionCreationOptions<TicketActivityEvent> { Name = name },
                database,
                ct);
        }
        catch (Exception)
        {
            // Race: another instance may have created it between our check and create.
            existing = await store.Subscriptions.GetSubscriptionsAsync(0, 128, database, ct);
            if (existing.Any(s => string.Equals(s.SubscriptionName, name, StringComparison.Ordinal)))
                return;

            throw; // real failure
        }
    }

    private static string ReadModelId(string ticketId) => $"ticket-readmodels/{ticketId}";

    private static string ReadModelSortKey(DateTimeOffset updatedAt, string ticketId)
        => updatedAt.ToString("O") + "|" + ticketId;

    private static string? GetString(Dictionary<string, object?> data, string key)
        => data.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static string[] GetStringArray(Dictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var v) || v is null) return [];

        if (v is string[] arr) return arr;

        if (v is IEnumerable<object> objs)
            return [.. objs.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0)];

        return [];
    }

    private static async Task ApplyEvent(IAsyncDocumentSession session, TicketActivityEvent evt, CancellationToken ct)
    {
        var rmId = ReadModelId(evt.TicketId);
        var rm = await session.LoadAsync<TicketReadModel>(rmId, ct);

        rm ??= new TicketReadModel
        {
            Id = rmId,
            TicketId = evt.TicketId,
            CreatedAt = evt.At,
            UpdatedAt = evt.At,
            SortKey = ReadModelSortKey(evt.At, evt.TicketId),
            Status = TicketStatus.Open,
            Priority = TicketPriority.Normal
        };

        switch (evt.Type)
        {
            case TicketActivityTypes.TicketCreated:
                rm.CustomerId = GetString(evt.Data, "customerId") ?? rm.CustomerId;
                rm.Subject = GetString(evt.Data, "subject") ?? rm.Subject;

                if (Enum.TryParse<TicketPriority>(GetString(evt.Data, "priority"), out var pr))
                    rm.Priority = pr;

                rm.Tags = GetStringArray(evt.Data, "tags");
                rm.CreatedAt = evt.At;
                rm.UpdatedAt = evt.At;
                break;

            case TicketActivityTypes.StatusChanged:
                if (Enum.TryParse<TicketStatus>(GetString(evt.Data, "to"), out var toStatus))
                    rm.Status = toStatus;

                rm.UpdatedAt = evt.At;
                break;

            case TicketActivityTypes.TicketUpdated:
                var subj = GetString(evt.Data, "subject");
                if (!string.IsNullOrWhiteSpace(subj))
                    rm.Subject = subj;

                if (Enum.TryParse<TicketPriority>(GetString(evt.Data, "priority"), out var pr2))
                    rm.Priority = pr2;

                var tags = GetStringArray(evt.Data, "tags");
                if (tags.Length > 0) rm.Tags = tags;

                rm.UpdatedAt = evt.At;
                break;

            case TicketActivityTypes.CommentAdded:
                rm.CommentCount += 1;
                rm.LastCommentPreview = GetString(evt.Data, "messagePreview");
                rm.UpdatedAt = evt.At;
                break;

            case TicketActivityTypes.RejectedTransition:
                // audit-only, do not mutate read model
                break;
        }

        rm.SortKey = ReadModelSortKey(rm.UpdatedAt, rm.TicketId);

        await session.StoreAsync(rm, rm.Id, ct);
    }
}