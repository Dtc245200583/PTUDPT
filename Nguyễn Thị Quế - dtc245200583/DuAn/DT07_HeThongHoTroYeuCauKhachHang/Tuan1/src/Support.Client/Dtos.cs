public sealed record CreateTicketRequest(
    Guid RequesterId,
    string RequesterName,
    string Subject,
    string Description,
    string Priority);

public sealed record ChangeStatusRequest(string Status, int ExpectedVersion, string? Reason);

public sealed record TicketSummaryDto(
    Guid Id,
    long TicketNumber,
    Guid RequesterId,
    string RequesterName,
    string Subject,
    string Priority,
    string Status,
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
    string Priority,
    string Status,
    string? Team,
    string? Assignee,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    List<TicketChangeDto> History);

public sealed record TicketChangeDto(
    Guid Id,
    Guid? ActorId,
    string Action,
    string? FieldName,
    string? OldValue,
    string? NewValue,
    string? Reason,
    DateTimeOffset OccurredAt);

public sealed record TicketListDto(List<TicketSummaryDto> Items, int Page, int PageSize, int TotalCount, bool HasMore);
public sealed record ApiError(string Code, string Message, Dictionary<string, string[]>? Errors);
