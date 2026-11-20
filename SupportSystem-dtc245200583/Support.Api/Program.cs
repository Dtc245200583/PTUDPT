using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Support.Api.Infrastructure;
using Support.Api.Realtime;
using Support.Api.Services;
using Support.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddDbContext<SupportDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("SupportDb")));
if (builder.Environment.IsDevelopment())
    builder.Services.AddAuthentication("Demo").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DemoAuthHandler>("Demo", _ => { });
else
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Authentication:Authority"];
        o.Audience = builder.Configuration["Authentication:Audience"];
        o.RequireHttpsMetadata = true;
        o.MapInboundClaims = false;
        o.TokenValidationParameters.NameClaimType = "sub";
        o.TokenValidationParameters.RoleClaimType = builder.Configuration["Authentication:RoleClaimType"] ?? "role";
    });
builder.Services.AddAuthorization();
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<NotificationConsumer>();

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
using (var migrationScope = app.Services.CreateScope())
    await SchemaMigrator.ApplyAsync(migrationScope.ServiceProvider.GetRequiredService<SupportDbContext>());
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/v1/tickets", async (CreateTicketRequest request, HttpContext http, SupportDbContext db, CancellationToken ct) =>
{
    if (!TryUserId(http.User, out var requesterId)) return Results.Unauthorized();
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 200) errors["subject"] = ["Subject is required and must be at most 200 characters."];
    if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 10000) errors["description"] = ["Description is required and must be at most 10000 characters."];
    if (request.Priority is not null && request.Priority is not ("Low" or "Normal" or "High" or "Urgent")) errors["priority"] = ["Priority must be Low, Normal, High, or Urgent."];
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var priority = request.Priority ?? "Normal";
    var ticket = new Ticket { RequesterId = requesterId, Subject = request.Subject!.Trim(), Description = request.Description!.Trim(), Priority = priority };
    var message = new TicketCreatedV1(Guid.NewGuid(), ticket.Id, requesterId, ticket.Subject, ticket.Description, ticket.Priority, ticket.CreatedAt);
    db.Tickets.Add(ticket);
    db.TicketHistory.Add(new TicketHistory { TicketId = ticket.Id, ActorId = requesterId, Action = "TicketCreated", NewValue = "New", CorrelationId = Correlation(http) });
    db.OutboxMessages.Add(Outbox("TicketCreated.v1", ticket.Id, message));
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/tickets/{ticket.Id}", ToResponse(ticket));
}).RequireAuthorization();

app.MapGet("/api/v1/tickets", async (HttpContext http, SupportDbContext db, string? status, string? priority, string? q, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, CancellationToken ct) =>
{
    if (!TryUserId(http.User, out var userId)) return Results.Unauthorized();
    if (status is not null && status is not (TicketStates.New or TicketStates.Assigned or TicketStates.InProgress or TicketStates.Resolved))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Unsupported ticket status."] });
    if (priority is not null && priority is not ("Low" or "Normal" or "High" or "Urgent"))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["priority"] = ["Unsupported priority."] });
    if (from.HasValue && to.HasValue && from > to)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["from must be earlier than or equal to to."] });
    var query = VisibleTickets(db.Tickets.AsNoTracking(), http.User, userId);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(t => t.Status == status);
    if (!string.IsNullOrWhiteSpace(priority)) query = query.Where(t => t.Priority == priority);
    if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim(); query = query.Where(t => t.Subject.Contains(term)); }
    if (from.HasValue) query = query.Where(t => t.CreatedAt >= from.Value);
    if (to.HasValue) query = query.Where(t => t.CreatedAt <= to.Value);
    var p = Math.Clamp(page ?? 1, 1, 100000); var size = Math.Clamp(pageSize ?? 20, 1, 100);
    var totalCount = await query.CountAsync(ct);
    var rows = (await query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct)).Select(ToResponse);
    return Results.Ok(new { items = rows, page = p, pageSize = size, totalCount, hasMore = p * size < totalCount });
}).RequireAuthorization();

app.MapGet("/api/v1/tickets/{id:guid}", async (Guid id, HttpContext http, SupportDbContext db, CancellationToken ct) =>
{
    if (!TryUserId(http.User, out var userId)) return Results.Unauthorized();
    var ticket = await VisibleTickets(db.Tickets.AsNoTracking(), http.User, userId).FirstOrDefaultAsync(t => t.Id == id, ct);
    return ticket is null ? Results.NotFound() : Results.Ok(ToResponse(ticket));
}).RequireAuthorization();

app.MapGet("/api/v1/users/{userId:guid}/tickets", async (Guid userId, HttpContext http, SupportDbContext db, int? page, int? pageSize, CancellationToken ct) =>
{
    if (!TryUserId(http.User, out var callerId)) return Results.Unauthorized();
    if (callerId != userId && !http.User.IsInRole("Admin")) return Results.Forbid();
    var p = Math.Clamp(page ?? 1, 1, 100000); var size = Math.Clamp(pageSize ?? 20, 1, 100);
    var query = db.Tickets.AsNoTracking().Where(t => t.RequesterId == userId);
    var totalCount = await query.CountAsync(ct);
    var rows = (await query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct)).Select(ToResponse);
    return Results.Ok(new { items = rows, page = p, pageSize = size, totalCount, hasMore = p * size < totalCount });
}).RequireAuthorization();

app.MapGet("/api/v1/tickets/{id:guid}/history", async (Guid id, HttpContext http, SupportDbContext db, CancellationToken ct) =>
{
    if (!TryUserId(http.User, out var userId)) return Results.Unauthorized();
    var visible = await VisibleTickets(db.Tickets.AsNoTracking(), http.User, userId).AnyAsync(t => t.Id == id, ct);
    if (!visible) return Results.NotFound();
    var history = await db.TicketHistory.AsNoTracking().Where(h => h.TicketId == id).OrderBy(h => h.OccurredAt).ToListAsync(ct);
    return Results.Ok(history);
}).RequireAuthorization();

app.MapPatch("/api/v1/tickets/{id:guid}/assignment", async (Guid id, ChangeAssignmentRequest request, HttpContext http, SupportDbContext db, CancellationToken ct) =>
{
    if (!TryUserId(http.User, out var actorId)) return Results.Unauthorized();
    if (!http.User.IsInRole("Admin")) return Results.Forbid();
    if (request.ExpectedVersion < 1) return Results.ValidationProblem(new Dictionary<string, string[]> { ["expectedVersion"] = ["Expected version must be positive."] });
    if (request.TeamId == Guid.Empty) return Results.ValidationProblem(new Dictionary<string, string[]> { ["teamId"] = ["TeamId is required."] });
    if (request.Reason?.Length > 1000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Reason must be at most 1000 characters."] });
    var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
    if (ticket is null) return Results.NotFound();
    if (ticket.Version != request.ExpectedVersion) return Results.Conflict(new { error = "Ticket version changed; reload before retrying." });
    var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == request.TeamId && t.IsActive, ct);
    if (team is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["teamId"] = ["Active team was not found."] });
    if (request.AssigneeId.HasValue && !await db.TeamMembers.AnyAsync(m => m.TeamId == team.Id && m.UserId == request.AssigneeId && m.IsActive, ct))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["assigneeId"] = ["Assignee must be an active member of the selected team."] });

    var now = DateTimeOffset.UtcNow;
    var oldTeamId = ticket.TeamId; var oldAssigneeId = ticket.AssigneeId; var oldStatus = ticket.Status;
    var correlationId = Correlation(http);
    ticket.TeamId = team.Id; ticket.AssigneeId = request.AssigneeId;
    if (ticket.Status == TicketStates.New) { ticket.Status = TicketStates.Assigned; ticket.AssignedAt = now; }
    ticket.UpdatedAt = now; ticket.Version++;
    db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = actorId, Action = "TeamAssigned", FieldName = "TeamId", OldValue = oldTeamId?.ToString(), NewValue = team.Id.ToString(), Reason = request.Reason, OccurredAt = now, CorrelationId = correlationId });
    if (oldAssigneeId != request.AssigneeId)
        db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = actorId, Action = "AssigneeChanged", FieldName = "AssigneeId", OldValue = oldAssigneeId?.ToString(), NewValue = request.AssigneeId?.ToString(), Reason = request.Reason, OccurredAt = now, CorrelationId = correlationId });
    if (oldStatus != ticket.Status)
        db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = actorId, Action = "StatusChanged", FieldName = "Status", OldValue = oldStatus, NewValue = ticket.Status, Reason = request.Reason, OccurredAt = now, CorrelationId = correlationId });
    var evt = new TicketAssignedV1(Guid.NewGuid(), id, ticket.RequesterId, team.Id, ticket.AssigneeId, ticket.Category ?? "General", ticket.Status, ticket.Version, now);
    db.OutboxMessages.Add(Outbox("TicketAssigned.v1", id, evt));
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Ticket version changed; reload before retrying." }); }
    return Results.Ok(ToResponse(ticket));
}).RequireAuthorization();

app.MapPatch("/api/v1/tickets/{id:guid}/status", async (Guid id, ChangeStatusRequest request, HttpContext http, SupportDbContext db, CancellationToken ct) =>
{
    if (!TryUserId(http.User, out var actorId)) return Results.Unauthorized();
    if (!http.User.IsInRole("Agent") && !http.User.IsInRole("Admin")) return Results.Forbid();
    if (request.ExpectedVersion < 1) return Results.ValidationProblem(new Dictionary<string, string[]> { ["expectedVersion"] = ["Expected version must be positive."] });
    if (request.Reason?.Length > 1000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Reason must be at most 1000 characters."] });
    var ticket = await VisibleTickets(db.Tickets, http.User, actorId).FirstOrDefaultAsync(t => t.Id == id, ct);
    if (ticket is null) return Results.NotFound();
    if (ticket.Version != request.ExpectedVersion) return Results.Conflict(new { error = "Ticket version changed; reload before retrying." });
    if (!TicketStates.CanTransition(ticket.Status, request.Status)) return Results.UnprocessableEntity(new { error = $"Transition {ticket.Status} -> {request.Status} is not allowed." });
    if (request.Status == TicketStates.Assigned && ticket.TeamId is null) return Results.UnprocessableEntity(new { error = "Assign the ticket to a team before changing its status to Assigned." });
    var old = ticket.Status; ticket.Status = request.Status; ticket.UpdatedAt = DateTimeOffset.UtcNow; ticket.Version++;
    if (request.Status == TicketStates.Assigned) ticket.AssignedAt = ticket.UpdatedAt;
    if (request.Status == TicketStates.Resolved) ticket.ResolvedAt = ticket.UpdatedAt;
    db.TicketHistory.Add(new TicketHistory { TicketId = ticket.Id, ActorId = actorId, Action = "StatusChanged", FieldName = "Status", OldValue = old, NewValue = ticket.Status, Reason = request.Reason, CorrelationId = Correlation(http) });
    var evt = new TicketStatusChangedV1(Guid.NewGuid(), ticket.Id, ticket.RequesterId, ticket.TeamId, ticket.AssigneeId, old, ticket.Status, actorId, ticket.Version, ticket.UpdatedAt);
    db.OutboxMessages.Add(Outbox("TicketStatusChanged.v1", ticket.Id, evt));
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Ticket version changed; reload before retrying." }); }
    return Results.Ok(ToResponse(ticket));
}).RequireAuthorization();

app.MapHub<TicketHub>("/hubs/tickets").RequireAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (SupportDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.Run();

static bool TryUserId(ClaimsPrincipal user, out Guid id) => Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out id);
static IQueryable<Ticket> VisibleTickets(IQueryable<Ticket> query, ClaimsPrincipal user, Guid userId) =>
    user.IsInRole("Admin") ? query : user.IsInRole("Agent") ? query.Where(t => t.AssigneeId == userId) : query.Where(t => t.RequesterId == userId);
static Guid Correlation(HttpContext http) => Guid.TryParse(http.Request.Headers["X-Correlation-Id"], out var id) ? id : Guid.NewGuid();
static OutboxMessage Outbox<T>(string type, Guid ticketId, T data) => new() { Type = type, AggregateId = ticketId, Payload = JsonSerializer.Serialize(data) };
static object ToResponse(Ticket t) => new { t.Id, t.TicketNumber, t.RequesterId, t.Subject, t.Description, t.Category, t.Priority, t.Status, t.TeamId, t.AssigneeId, t.CreatedAt, t.UpdatedAt, t.AssignedAt, t.ResolvedAt, t.Version };
record CreateTicketRequest(string Subject, string Description, string? Priority);
record ChangeStatusRequest(string Status, int ExpectedVersion, string? Reason);
record ChangeAssignmentRequest(Guid TeamId, Guid? AssigneeId, int ExpectedVersion, string? Reason);
