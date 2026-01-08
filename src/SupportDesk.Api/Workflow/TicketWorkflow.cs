using Stateless;
using SupportDesk.Api.Models;
using SupportDesk.Contracts.Tickets;

namespace SupportDesk.Api.Workflow;

public static class TicketWorkflow
{
    public sealed class Context
    {
        public required Ticket Ticket { get; init; }
        public required string Actor { get; init; }
        public string? Reason { get; init; }
    }

    public static StateMachine<TicketStatus, TicketTrigger> Build(Context context)
    {
        var sm = new StateMachine<TicketStatus, TicketTrigger>(context.Ticket.Status);

        // Guards (examples)
        bool CanClose() => context.Ticket.Status == TicketStatus.Resolved && context.Ticket.Comments.Count >= 1;
        bool CanResolve() => !string.IsNullOrWhiteSpace(context.Ticket.Description);

        sm.Configure(TicketStatus.Open)
            .Permit(TicketTrigger.StartWork, TicketStatus.Pending)
            .PermitIf(TicketTrigger.Resolve, TicketStatus.Resolved, CanResolve);

        sm.Configure(TicketStatus.Pending)
            .PermitIf(TicketTrigger.Resolve, TicketStatus.Resolved, CanResolve)
            .Permit(TicketTrigger.Reopen, TicketStatus.Open);

        sm.Configure(TicketStatus.Resolved)
            .PermitIf(TicketTrigger.Close, TicketStatus.Closed, CanClose)
            .Permit(TicketTrigger.Reopen, TicketStatus.Open);

        sm.Configure(TicketStatus.Closed)
            .Permit(TicketTrigger.Reopen, TicketStatus.Open);

        return sm;
    }

    public static bool TryParseTrigger(string value, out TicketTrigger trigger)
    {
        trigger = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Trim().ToLowerInvariant() switch
        {
            "startwork" or "start" => Set(TicketTrigger.StartWork, out trigger),
            "resolve" => Set(TicketTrigger.Resolve, out trigger),
            "close" => Set(TicketTrigger.Close, out trigger),
            "reopen" => Set(TicketTrigger.Reopen, out trigger),
            _ => false
        };

        static bool Set(TicketTrigger t, out TicketTrigger trg)
        {
            trg = t;
            return true;
        }
    }
}
