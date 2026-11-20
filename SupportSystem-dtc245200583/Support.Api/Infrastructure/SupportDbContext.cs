using Microsoft.EntityFrameworkCore;

namespace Support.Api.Infrastructure;

public sealed class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long TicketNumber { get; set; }
    public Guid RequesterId { get; set; }
    public string Subject { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Category { get; set; }
    public string Priority { get; set; } = "Normal";
    public string Status { get; set; } = "New";
    public Guid? TeamId { get; set; }
    public Guid? AssigneeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class Team
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class TeamMember
{
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CategoryTeamRule
{
    public string Category { get; set; } = "";
    public Guid TeamId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TicketHistory
{
    public long Id { get; set; }
    public Guid TicketId { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = "";
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CorrelationId { get; set; }
}

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "";
    public Guid AggregateId { get; set; }
    public string Payload { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
}

public sealed class ProcessedMessage
{
    public string ConsumerName { get; set; } = "";
    public Guid MessageId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SupportDbContext(DbContextOptions<SupportDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<CategoryTeamRule> CategoryTeamRules => Set<CategoryTeamRule>();
    public DbSet<TicketHistory> TicketHistory => Set<TicketHistory>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Ticket>().HasKey(x => x.Id);
        model.Entity<Ticket>().Property(x => x.Subject).HasMaxLength(200);
        model.Entity<Ticket>().Property(x => x.Category).HasMaxLength(80);
        model.Entity<Ticket>().Property(x => x.Priority).HasMaxLength(16);
        model.Entity<Ticket>().Property(x => x.Status).HasMaxLength(16);
        model.Entity<Ticket>().HasIndex(x => x.TicketNumber).IsUnique();
        model.Entity<Ticket>().Property(x => x.TicketNumber).UseIdentityAlwaysColumn();
        model.Entity<Ticket>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<Ticket>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Ticket>().HasIndex(x => new { x.RequesterId, x.CreatedAt });
        model.Entity<Ticket>().HasIndex(x => new { x.Status, x.Priority, x.CreatedAt });
        model.Entity<TicketHistory>().HasKey(x => x.Id);
        model.Entity<TicketHistory>().HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<TicketHistory>().HasIndex(x => new { x.TicketId, x.OccurredAt });
        model.Entity<OutboxMessage>().HasKey(x => x.Id);
        model.Entity<OutboxMessage>().HasIndex(x => x.OccurredAt).HasFilter("\"PublishedAt\" IS NULL");
        model.Entity<ProcessedMessage>().HasKey(x => new { x.ConsumerName, x.MessageId });
        model.Entity<Team>().HasKey(x => x.Id);
        model.Entity<Team>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<Team>().HasIndex(x => x.Name).IsUnique();
        model.Entity<TeamMember>().HasKey(x => new { x.TeamId, x.UserId });
        model.Entity<TeamMember>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<CategoryTeamRule>().HasKey(x => x.Category);
        model.Entity<CategoryTeamRule>().Property(x => x.Category).HasMaxLength(80);
        model.Entity<CategoryTeamRule>().HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
    }
}
