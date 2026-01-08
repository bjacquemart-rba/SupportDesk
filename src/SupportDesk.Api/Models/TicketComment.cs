namespace SupportDesk.Api.Models;

public sealed class TicketComment
{
    public string Author { get; set; } = default!;
    public string Message { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
