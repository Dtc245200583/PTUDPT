public sealed record CreateTicketRequest(
    Guid RequesterId,
    string? RequesterName,
    string? Subject,
    string? Description,
    TicketPriority? Priority);

public sealed record ChangeStatusRequest(TicketStatus Status, int ExpectedVersion, string? Reason);

public sealed record TicketSummaryDto(
    Guid Id,
    long TicketNumber,
    Guid RequesterId,
    string RequesterName,
    string Subject,
    TicketPriority Priority,
    TicketStatus Status,
    string? Team,
    string? Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version);

public sealed record TicketDto(
    Guid Id,
    long TicketNumber,
    Guid RequesterId,
    string RequesterName,
    string Subject,
    string Description,
    TicketPriority Priority,
    TicketStatus Status,
    string? Team,
    string? Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    IReadOnlyList<TicketChange> History);

public sealed record ApiError(string Code, string Message, Dictionary<string, string[]>? Errors = null);
