using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using SupportDesk.Api.Activity;
using SupportDesk.Api.Indexes;
using SupportDesk.Api.Models;
using SupportDesk.Api.Paging;
using SupportDesk.Api.Workflow;
using SupportDesk.Contracts.Tickets;

namespace SupportDesk.Api.Endpoints;

public static class TicketsEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tickets").WithTags("Tickets");

        group.MapPost("/", CreateTicket);
        group.MapGet("/{id}", GetTicket);
        group.MapPut("/{id}", UpdateTicket);
        group.MapPost("/{id}/comments", AddComment);

        // Workflow: /tickets/{id}/status:resolve
        group.MapPost("/{id}/status:{trigger}", ApplyWorkflowTrigger);

        group.MapGet("/inbox", Inbox);
        group.MapGet("/{id}/timeline", Timeline);

        return app;
    }

    private static string Actor(HttpContext http) => http.User?.Identity?.Name ?? "anonymous";

    private static async Task<IResult> CreateTicket(
    CreateTicketRequest req,
    IAsyncDocumentSession session,
    HttpContext http)
    {
        var now = DateTimeOffset.UtcNow;

        // 1) Optional idempotency key
        var idemKey = http.Request.Headers["Idempotency-Key"].ToString();
        idemKey = string.IsNullOrWhiteSpace(idemKey) ? null : idemKey.Trim();

        // If present, use a deterministic receipt doc id (NO INDEX NEEDED)
        var receiptId = idemKey is null ? null : $"idempotency/{idemKey}";
        var receiptTtl = TimeSpan.FromHours(24);

        if (receiptId is not null)
        {
            // Fast path: receipt already exists => return the original ticket
            var existingReceipt = await session.LoadAsync<IdempotencyReceipt>(receiptId);
            if (existingReceipt is not null)
            {
                var existingTicket = await session.LoadAsync<Ticket>(existingReceipt.TicketId);
                return existingTicket is null
                    ? Results.Problem("Idempotency receipt exists but ticket was not found.", statusCode: 500)
                    : Results.Ok(existingTicket);
            }
        }

        // 2) Build ticket
        var ticket = new Ticket
        {
            CustomerId = req.CustomerId,
            Subject = req.Subject,
            Description = req.Description,
            Priority = req.Priority,
            Tags = [.. (req.Tags ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)],
            CreatedAt = now,
            UpdatedAt = now
        };

        // 3) If idempotency is used, we try to "claim" the key by storing a receipt first.
        //    This is what prevents duplicates under concurrency.
        if (receiptId is not null)
        {
            var receipt = new IdempotencyReceipt
            {
                Id = receiptId,
                TicketId = "__PENDING__",
                CreatedAt = now
            };

            await session.StoreAsync(receipt, receipt.Id);

            // Set expiration (requires expiration enabled in RavenDB database)
            session.Advanced.GetMetadataFor(receipt)["@expires"] = now.Add(receiptTtl).UtcDateTime;

            try
            {
                // Save the receipt claim. If another request already claimed it, this will conflict.
                await session.SaveChangesAsync();
            }
            catch (ConcurrencyException)
            {
                // Another request won the race. Load and return its result.
                var winnerReceipt = await session.LoadAsync<IdempotencyReceipt>(receiptId);
                if (winnerReceipt is null)
                    return Results.Problem("Idempotency conflict occurred but receipt was not found.", statusCode: 500);

                var winnerTicket = await session.LoadAsync<Ticket>(winnerReceipt.TicketId);
                return winnerTicket is null
                    ? Results.Problem("Idempotency receipt exists but ticket was not found.", statusCode: 500)
                    : Results.Ok(winnerTicket);
            }
        }

        // 4) Store ticket
        await session.StoreAsync(ticket);
        await session.SaveChangesAsync(); // ensures ticket.Id exists

        // 5) Emit TicketCreated event
        var evt = ActivityEventFactory.Create(
            ticket.Id,
            TicketActivityTypes.TicketCreated,
            Actor(http),
            now,
            new Dictionary<string, object?>
            {
                ["customerId"] = ticket.CustomerId,
                ["subject"] = ticket.Subject,
                ["priority"] = ticket.Priority.ToString(),
                ["tags"] = ticket.Tags.ToArray()
            });

        await session.StoreAsync(evt, evt.Id);

        // 6) If we used idempotency, finalize the receipt to point to the created ticket
        if (receiptId is not null)
        {
            var receipt = await session.LoadAsync<IdempotencyReceipt>(receiptId);
            if (receipt is null)
                return Results.Problem("Idempotency receipt missing after creation.", statusCode: 500);

            receipt.TicketId = ticket.Id;
            // keep @expires as-is
        }

        await session.SaveChangesAsync();

        return Results.Created($"/tickets/{ticket.Id}", ticket);
    }

    //private static async Task<IResult> CreateTicket(
    //    CreateTicketRequest req,
    //    IAsyncDocumentSession session,
    //    HttpContext http)
    //{
    //    var now = DateTimeOffset.UtcNow;

    //    var ticket = new Ticket
    //    {
    //        CustomerId = req.CustomerId,
    //        Subject = req.Subject,
    //        Description = req.Description,
    //        Priority = req.Priority,
    //        Tags = [.. (req.Tags ?? [])
    //            .Where(t => !string.IsNullOrWhiteSpace(t))
    //            .Distinct(StringComparer.OrdinalIgnoreCase)],
    //        CreatedAt = now,
    //        UpdatedAt = now
    //    };

    //    await session.StoreAsync(ticket);
    //    await session.SaveChangesAsync(); // ensures ticket.Id exists

    //    var evt = ActivityEventFactory.Create(
    //        ticket.Id,
    //        TicketActivityTypes.TicketCreated,
    //        Actor(http),
    //        now,
    //        new Dictionary<string, object?>
    //        {
    //            ["customerId"] = ticket.CustomerId,
    //            ["subject"] = ticket.Subject,
    //            ["priority"] = ticket.Priority.ToString(),
    //            ["tags"] = ticket.Tags.ToArray()
    //        });

    //    await session.StoreAsync(evt, evt.Id);
    //    await session.SaveChangesAsync();

    //    return Results.Created($"/tickets/{ticket.Id}", ticket);
    //}

    private static async Task<IResult> GetTicket(string id, IAsyncDocumentSession session)
    {
        var ticket = await session.LoadAsync<Ticket>(id);
        return ticket is null ? Results.NotFound() : Results.Ok(ticket);
    }

    // Non-workflow updates only
    private static async Task<IResult> UpdateTicket(
        string id,
        UpdateTicketRequest req,
        IAsyncDocumentSession session,
        HttpContext http)
    {
        var ticket = await session.LoadAsync<Ticket>(id);
        if (ticket is null) return Results.NotFound();

        var now = DateTimeOffset.UtcNow;

        var changed = false;
        var data = new Dictionary<string, object?>();

        if (!string.Equals(ticket.Subject, req.Subject, StringComparison.Ordinal))
        {
            ticket.Subject = req.Subject;
            data["subject"] = ticket.Subject;
            changed = true;
        }

        if (!string.Equals(ticket.Description, req.Description, StringComparison.Ordinal))
        {
            ticket.Description = req.Description;
            data["descriptionChanged"] = true;
            changed = true;
        }

        if (ticket.Priority != req.Priority)
        {
            ticket.Priority = req.Priority;
            data["priority"] = ticket.Priority.ToString();
            changed = true;
        }

        var normalizedTags = (req.Tags ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existing = new HashSet<string>(ticket.Tags, StringComparer.OrdinalIgnoreCase);
        var proposed = new HashSet<string>(normalizedTags, StringComparer.OrdinalIgnoreCase);

        if (!existing.SetEquals(proposed))
        {
            ticket.Tags = normalizedTags;
            data["tags"] = ticket.Tags.ToArray();
            changed = true;
        }

        if (!changed)
            return Results.Ok(ticket);

        ticket.UpdatedAt = now;

        var evt = ActivityEventFactory.Create(
            ticket.Id,
            TicketActivityTypes.TicketUpdated,
            Actor(http),
            now,
            data);

        await session.StoreAsync(evt, evt.Id);
        await session.SaveChangesAsync();

        return Results.Ok(ticket);
    }

    private static async Task<IResult> AddComment(
        string id,
        AddCommentRequest req,
        IAsyncDocumentSession session,
        HttpContext http)
    {
        var ticket = await session.LoadAsync<Ticket>(id);
        if (ticket is null) return Results.NotFound();

        var now = DateTimeOffset.UtcNow;

        ticket.Comments.Add(new TicketComment
        {
            Author = req.Author,
            Message = req.Message,
            CreatedAt = now
        });

        ticket.UpdatedAt = now;

        var preview = req.Message.Length <= 120 ? req.Message : req.Message[..120];

        var evt = ActivityEventFactory.Create(
            ticket.Id,
            TicketActivityTypes.CommentAdded,
            Actor(http),
            now,
            new Dictionary<string, object?>
            {
                ["author"] = req.Author,
                ["messagePreview"] = preview
            });

        await session.StoreAsync(evt, evt.Id);
        await session.SaveChangesAsync();

        return Results.Ok(ticket);
    }

    private static async Task<IResult> ApplyWorkflowTrigger(
        string id,
        string trigger,
        WorkflowCommandRequest? body,
        IAsyncDocumentSession session,
        HttpContext http)
    {
        var ticket = await session.LoadAsync<Ticket>(id);
        if (ticket is null) return Results.NotFound();

        var now = DateTimeOffset.UtcNow;
        var actor = Actor(http);

        if (!TicketWorkflow.TryParseTrigger(trigger, out var trig))
        {
            var rej = ActivityEventFactory.Create(
                ticket.Id,
                TicketActivityTypes.RejectedTransition,
                actor,
                now,
                new Dictionary<string, object?>
                {
                    ["reason"] = "UnknownTrigger",
                    ["trigger"] = trigger,
                    ["from"] = ticket.Status.ToString(),
                    ["message"] = "Trigger was not recognized.",
                    ["note"] = body?.Reason
                });

            await session.StoreAsync(rej, rej.Id);
            await session.SaveChangesAsync();

            return Results.BadRequest(new { error = "UnknownTrigger", trigger });
        }

        var ctx = new TicketWorkflow.Context
        {
            Ticket = ticket,
            Actor = actor,
            Reason = body?.Reason
        };

        var sm = TicketWorkflow.Build(ctx);

        if (!sm.CanFire(trig))
        {
            var rej = ActivityEventFactory.Create(
                ticket.Id,
                TicketActivityTypes.RejectedTransition,
                actor,
                now,
                new Dictionary<string, object?>
                {
                    ["reason"] = "NotPermittedOrGuardFailed",
                    ["trigger"] = trig.ToString(),
                    ["from"] = ticket.Status.ToString(),
                    ["message"] = "Transition not permitted or guard failed.",
                    ["note"] = body?.Reason,
                    ["commentCount"] = ticket.Comments.Count
                });

            await session.StoreAsync(rej, rej.Id);
            await session.SaveChangesAsync();

            return Results.BadRequest(new
            {
                error = "NotPermittedOrGuardFailed",
                from = ticket.Status.ToString(),
                trigger = trig.ToString()
            });
        }

        var from = ticket.Status;

        try
        {
            sm.Fire(trig);
        }
        catch (Exception ex)
        {
            var rej = ActivityEventFactory.Create(
                ticket.Id,
                TicketActivityTypes.RejectedTransition,
                actor,
                now,
                new Dictionary<string, object?>
                {
                    ["reason"] = "Exception",
                    ["trigger"] = trig.ToString(),
                    ["from"] = from.ToString(),
                    ["message"] = ex.Message,
                    ["note"] = body?.Reason
                });

            await session.StoreAsync(rej, rej.Id);
            await session.SaveChangesAsync();

            return Results.BadRequest(new
            {
                error = "TransitionFailed",
                from = from.ToString(),
                trigger = trig.ToString(),
                message = ex.Message
            });
        }

        ticket.Status = sm.State;
        ticket.UpdatedAt = now;

        var statusEvt = ActivityEventFactory.Create(
            ticket.Id,
            TicketActivityTypes.StatusChanged,
            actor,
            now,
            new Dictionary<string, object?>
            {
                ["from"] = from.ToString(),
                ["to"] = ticket.Status.ToString(),
                ["trigger"] = trig.ToString(),
                ["note"] = body?.Reason
            });

        await session.StoreAsync(statusEvt, statusEvt.Id);
        await session.SaveChangesAsync();

        return Results.Ok(new
        {
            ticketId = ticket.Id,
            from = from.ToString(),
            to = ticket.Status.ToString(),
            trigger = trig.ToString()
        });
    }

    private static async Task<IResult> Inbox(
        int? limit,
        string? cursor,
        string? customerId,
        TicketStatus? status,
        string? q,
        IAsyncDocumentSession session)
    {
        var pageSize = Math.Clamp(limit ?? 25, 1, 100);

        var query = session.Query<TicketReadModel, TicketReadModels_Inbox>();

        if (!string.IsNullOrWhiteSpace(customerId))
            query = query.Where(x => x.CustomerId == customerId);

        if (status is not null)
            query = query.Where(x => x.Status == status);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Search(x => x.Subject, q);

        if (!string.IsNullOrWhiteSpace(cursor) && Cursor.TryDecode(cursor, out var sortKey))
            query = query.Where(x => string.Compare(x.SortKey, sortKey, StringComparison.Ordinal) < 0);

        var rows = await query
            .OrderByDescending(x => x.SortKey)
            .Take(pageSize + 1)
            .ToListAsync();

        var hasMore = rows.Count > pageSize;
        var page = rows.Take(pageSize).ToList();
        var nextCursor = hasMore ? Cursor.Encode(page[^1].SortKey) : null;

        var items = page.Select(r => new TicketInboxItem(
            r.TicketId,
            r.CustomerId,
            r.Subject,
            r.Status,
            r.Priority,
            r.UpdatedAt,
            r.CommentCount,
            r.LastCommentPreview,
            r.Tags,
            r.SortKey
        )).ToList();

        return Results.Ok(new PagedResult<TicketInboxItem>(items, nextCursor));
    }

    private static async Task<IResult> Timeline(
        string id,
        int? limit,
        string? cursor,
        IAsyncDocumentSession session)
    {
        var pageSize = Math.Clamp(limit ?? 100, 1, 500);

        var query = session.Query<TicketActivityEvent, TicketActivity_ByTicketAndSortKey>()
            .Where(x => x.TicketId == id);

        if (!string.IsNullOrWhiteSpace(cursor) && Cursor.TryDecode(cursor, out var sortKey))
            query = query.Where(x => string.Compare(x.SortKey, sortKey, StringComparison.Ordinal) > 0);

        var rows = await query
            .OrderBy(x => x.SortKey)
            .Take(pageSize + 1)
            .ToListAsync();

        var hasMore = rows.Count > pageSize;
        var page = rows.Take(pageSize).ToList();
        var nextCursor = hasMore ? Cursor.Encode(page[^1].SortKey) : null;

        return Results.Ok(new PagedResult<TicketActivityEvent>(page, nextCursor));
    }
}
