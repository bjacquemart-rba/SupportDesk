namespace SupportDesk.Contracts.Tickets;

public enum TicketStatus
{
    Open,
    Pending,
    Resolved,
    Closed
}

public enum TicketPriority
{
    Low,
    Normal,
    High,
    Urgent
}

public static class TicketActivityTypes
{
    public const string TicketCreated = "TicketCreated";
    public const string TicketUpdated = "TicketUpdated";
    public const string CommentAdded = "CommentAdded";

    public const string StatusChanged = "StatusChanged";
    public const string RejectedTransition = "RejectedTransition";
}