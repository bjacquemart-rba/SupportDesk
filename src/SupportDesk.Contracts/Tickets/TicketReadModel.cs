namespace SupportDesk.Contracts.Tickets;

public sealed class TicketReadModel
{
    public string Id { get; set; } = default!; // "ticket-readmodels/{ticketId}"

    public string TicketId { get; set; } = default!;
    public string CustomerId { get; set; } = default!;
    public string Subject { get; set; } = default!;

    public TicketStatus Status { get; set; }
    public TicketPriority Priority { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public int CommentCount { get; set; }
    public string? LastCommentPreview { get; set; }

    public string[] Tags { get; set; } = [];

    public string SortKey { get; set; } = default!; // UpdatedAt(O)|TicketId
}

public sealed record TicketInboxItem(
    string TicketId,
    string CustomerId,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    DateTimeOffset UpdatedAt,
    int CommentCount,
    string? LastCommentPreview,
    string[] Tags,
    string SortKey
);
