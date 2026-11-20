public enum TicketPriority
{
    Low,
    Normal,
    High,
    Urgent
}

public enum TicketStatus
{
    New,
    Assigned,
    InProgress,
    Resolved
}

public sealed class SupportTicket
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public long TicketNumber { get; init; }
    public Guid RequesterId { get; init; }
    public string RequesterName { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Description { get; init; } = "";
    public TicketPriority Priority { get; init; }
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public string? Team { get; set; }
    public string? Assignee { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; } = 1;
    public List<TicketChange> History { get; } = [];
}

public sealed record TicketChange(
    Guid Id,
    Guid? ActorId,
    string Action,
    string? FieldName,
    string? OldValue,
    string? NewValue,
    string? Reason,
    DateTimeOffset OccurredAt);
