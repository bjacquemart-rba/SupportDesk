using Raven.Client.Documents.Indexes;
using SupportDesk.Contracts.Tickets;

namespace SupportDesk.Api.Indexes;

public sealed class TicketReadModels_Inbox : AbstractIndexCreationTask<TicketReadModel>
{
    public TicketReadModels_Inbox()
    {
        Map = rms => from r in rms
                     select new
                     {
                         r.CustomerId,
                         r.Status,
                         r.Priority,
                         r.UpdatedAt,
                         r.SortKey,
                         r.Tags,
                         r.Subject
                     };

        Index(x => x.Subject, FieldIndexing.Search);
    }
}
