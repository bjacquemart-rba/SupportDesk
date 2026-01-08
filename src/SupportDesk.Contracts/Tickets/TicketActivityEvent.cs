namespace SupportDesk.Contracts.Tickets;

public sealed class TicketActivityEvent
{
    public string Id { get; set; } = default!;
    public string TicketId { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string? Actor { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object?> Data { get; set; } = [];
    public string SortKey { get; set; } = default!;
}