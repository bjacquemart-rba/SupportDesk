namespace SupportDesk.Contracts.Tickets;

public sealed record WorkflowCommandRequest(string? Reason = null);