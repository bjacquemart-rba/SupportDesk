namespace SupportDesk.Api.Models;

public sealed class IdempotencyReceipt
{
    public string Id { get; set; } = default!;
    public string TicketId { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
}

