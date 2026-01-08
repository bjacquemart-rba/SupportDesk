namespace SupportDesk.Contracts.Tickets;

public sealed record CreateTicketRequest(
    string CustomerId,
    string Subject,
    string Description,
    TicketPriority Priority,
    string[]? Tags
);

public sealed record AddCommentRequest(string Author, string Message);

// Non-workflow updates only (workflow done via triggers)
public sealed record UpdateTicketRequest(
    string Subject,
    string Description,
    TicketPriority Priority,
    string[] Tags
);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor
);
