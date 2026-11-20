public sealed class TicketStore
{
    private readonly object _sync = new();
    private readonly List<SupportTicket> _tickets = [];
    private long _nextTicketNumber = 1001;

    public TicketDto Create(CreateTicketRequest request)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var ticket = new SupportTicket
            {
                TicketNumber = _nextTicketNumber++,
                RequesterId = request.RequesterId,
                RequesterName = request.RequesterName!.Trim(),
                Subject = request.Subject!.Trim(),
                Description = request.Description!.Trim(),
                Priority = request.Priority ?? TicketPriority.Normal,
                CreatedAt = now,
                UpdatedAt = now
            };
            ticket.History.Add(new TicketChange(Guid.NewGuid(), request.RequesterId, "TicketCreated", "Status", null, "New", null, now));
            _tickets.Add(ticket);
            return ToDto(ticket);
        }
    }

    public List<TicketSummaryDto> List(Guid? requesterId = null, TicketStatus? status = null, TicketPriority? priority = null, string? query = null)
    {
        lock (_sync)
        {
            return _tickets
                .Where(t => requesterId is null || t.RequesterId == requesterId)
                .Where(t => status is null || t.Status == status)
                .Where(t => priority is null || t.Priority == priority)
                .Where(t => string.IsNullOrWhiteSpace(query) || t.Subject.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.TicketNumber)
                .Select(ToSummary)
                .ToList();
        }
    }

    public TicketDto? Find(Guid id)
    {
        lock (_sync)
        {
            var ticket = _tickets.FirstOrDefault(t => t.Id == id);
            return ticket is null ? null : ToDto(ticket);
        }
    }

    public (TicketDto? Ticket, string? ErrorCode, string? ErrorMessage) ChangeStatus(Guid id, ChangeStatusRequest request)
    {
        lock (_sync)
        {
            var ticket = _tickets.FirstOrDefault(t => t.Id == id);
            if (ticket is null) return (null, "TICKET_NOT_FOUND", "Không tìm thấy yêu cầu hỗ trợ.");
            if (ticket.Version != request.ExpectedVersion) return (null, "VERSION_CONFLICT", "Ticket đã thay đổi; hãy tải lại trước khi thử lại.");
            if (!CanTransition(ticket.Status, request.Status)) return (null, "INVALID_STATUS_TRANSITION", $"Không thể chuyển từ {ticket.Status} sang {request.Status}.");

            var oldStatus = ticket.Status;
            var now = DateTimeOffset.UtcNow;
            ticket.Status = request.Status;
            ticket.UpdatedAt = now;
            ticket.Version++;
            ticket.History.Add(new TicketChange(Guid.NewGuid(), null, "StatusChanged", "Status", oldStatus.ToString(), request.Status.ToString(), request.Reason, now));
            return (ToDto(ticket), null, null);
        }
    }

    public List<TicketChange>? GetHistory(Guid id)
    {
        lock (_sync)
        {
            var ticket = _tickets.FirstOrDefault(t => t.Id == id);
            return ticket?.History.ToList();
        }
    }

    private static bool CanTransition(TicketStatus current, TicketStatus next) =>
        (current, next) is (TicketStatus.New, TicketStatus.Assigned)
            or (TicketStatus.Assigned, TicketStatus.InProgress)
            or (TicketStatus.InProgress, TicketStatus.Resolved);

    private static TicketSummaryDto ToSummary(SupportTicket t) => new(
        t.Id, t.TicketNumber, t.RequesterId, t.RequesterName, t.Subject,
        t.Priority, t.Status, t.Team, t.Assignee, t.CreatedAt, t.UpdatedAt, t.Version);

    private static TicketDto ToDto(SupportTicket t) => new(
        t.Id, t.TicketNumber, t.RequesterId, t.RequesterName, t.Subject,
        t.Description, t.Priority, t.Status, t.Team, t.Assignee,
        t.CreatedAt, t.UpdatedAt, t.Version, t.History.ToList());
}
