using SupportDesk.Contracts.Tickets;

namespace SupportDesk.Api.Activity;

public static class ActivityEventFactory
{
    public static string NewEventId(string ticketId, DateTimeOffset atUtc)
    {
        var stamp = atUtc.UtcDateTime.ToString("yyyyMMddHHmmssfffffff");
        return $"ticket-activity/{ticketId}/{stamp}-{Guid.NewGuid():N}";
    }

    public static string NewEventSortKey(DateTimeOffset atUtc, string eventId)
        => atUtc.UtcDateTime.ToString("yyyyMMddHHmmssfffffff") + "|" + eventId;

    public static TicketActivityEvent Create(
        string ticketId,
        string type,
        string actor,
        DateTimeOffset atUtc,
        Dictionary<string, object?> data)
    {
        var id = NewEventId(ticketId, atUtc);
        return new TicketActivityEvent
        {
            Id = id,
            TicketId = ticketId,
            Type = type,
            Actor = actor,
            At = atUtc,
            SortKey = NewEventSortKey(atUtc, id),
            Data = data
        };
    }
}
