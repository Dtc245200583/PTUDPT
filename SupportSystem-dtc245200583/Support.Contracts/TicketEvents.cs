namespace Support.Contracts;

public sealed record TicketCreatedV1(Guid MessageId, Guid TicketId, Guid RequesterId, string Subject, string Description, string Priority, DateTimeOffset OccurredAt);
public sealed record TicketAssignedV1(Guid MessageId, Guid TicketId, Guid RequesterId, Guid? TeamId, Guid? AssigneeId, string Category, string Status, int Version, DateTimeOffset OccurredAt);
public sealed record TicketStatusChangedV1(Guid MessageId, Guid TicketId, Guid RequesterId, Guid? TeamId, Guid? AssigneeId, string OldStatus, string NewStatus, Guid ActorId, int Version, DateTimeOffset OccurredAt);

public static class TicketStates
{
    public const string New = "New";
    public const string Assigned = "Assigned";
    public const string InProgress = "InProgress";
    public const string Resolved = "Resolved";

    public static bool CanTransition(string current, string next) =>
        (current, next) is (New, Assigned) or (Assigned, InProgress) or (InProgress, Resolved);
}
