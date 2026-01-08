using SupportDesk.Contracts.Tickets;

namespace SupportDesk.Api.Models;

public sealed class Ticket
{
    public string Id { get; set; } = default!;
    public string CustomerId { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string Description { get; set; } = default!;

    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<TicketComment> Comments { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}
