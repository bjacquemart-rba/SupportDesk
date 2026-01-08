using Raven.Client.Documents.Indexes;
using SupportDesk.Contracts.Tickets;

namespace SupportDesk.Api.Indexes;

public sealed class TicketActivity_ByTicketAndSortKey : AbstractIndexCreationTask<TicketActivityEvent>
{
    public TicketActivity_ByTicketAndSortKey()
    {
        Map = events => from e in events
                        select new
                        {
                            e.TicketId,
                            e.SortKey,
                            e.At,
                            e.Type
                        };
    }
}
