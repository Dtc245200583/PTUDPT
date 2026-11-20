using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<TicketStore>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)));

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { message = "Support.Api đang chạy." }));

app.MapPost("/tickets", (CreateTicketRequest request, TicketStore store) =>
{
    var errors = new Dictionary<string, string[]>();
    if (request.RequesterId == Guid.Empty) errors["requesterId"] = ["RequesterId không được để trống."];
    if (string.IsNullOrWhiteSpace(request.RequesterName) || request.RequesterName.Trim().Length > 120)
        errors["requesterName"] = ["Tên người gửi là bắt buộc và tối đa 120 ký tự."];
    if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 200)
        errors["subject"] = ["Tiêu đề là bắt buộc và tối đa 200 ký tự."];
    if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 10000)
        errors["description"] = ["Nội dung là bắt buộc và tối đa 10000 ký tự."];
    if (request.Priority.HasValue && !Enum.IsDefined(request.Priority.Value))
        errors["priority"] = ["Mức ưu tiên phải là Low, Normal, High hoặc Urgent."];
    if (errors.Count > 0) return Results.BadRequest(new ApiError("INVALID_TICKET", "Thông tin ticket không hợp lệ.", errors));

    var ticket = store.Create(request);
    return Results.Created($"/tickets/{ticket.Id}", ticket);
});

app.MapGet("/tickets", (string? status, string? priority, string? q, int? page, int? pageSize, TicketStore store) =>
{
    if (!TryParseEnum<TicketStatus>(status, out var parsedStatus))
        return Results.BadRequest(new ApiError("INVALID_STATUS", "Status phải là New, Assigned, InProgress hoặc Resolved."));
    if (!TryParseEnum<TicketPriority>(priority, out var parsedPriority))
        return Results.BadRequest(new ApiError("INVALID_PRIORITY", "Priority phải là Low, Normal, High hoặc Urgent."));

    var all = store.List(status: parsedStatus, priority: parsedPriority, query: q);
    var currentPage = Math.Clamp(page ?? 1, 1, 100000);
    var size = Math.Clamp(pageSize ?? 20, 1, 100);
    var items = all.Skip((currentPage - 1) * size).Take(size).ToList();
    return Results.Ok(new { items, page = currentPage, pageSize = size, totalCount = all.Count, hasMore = currentPage * size < all.Count });
});

app.MapGet("/tickets/{id:guid}", (Guid id, TicketStore store) =>
{
    var ticket = store.Find(id);
    return ticket is null
        ? Results.NotFound(new ApiError("TICKET_NOT_FOUND", $"Không tìm thấy ticket {id}."))
        : Results.Ok(ticket);
});

app.MapGet("/users/{userId:guid}/tickets", (Guid userId, int? page, int? pageSize, TicketStore store) =>
{
    var all = store.List(requesterId: userId);
    var currentPage = Math.Clamp(page ?? 1, 1, 100000);
    var size = Math.Clamp(pageSize ?? 20, 1, 100);
    var items = all.Skip((currentPage - 1) * size).Take(size).ToList();
    return Results.Ok(new { items, page = currentPage, pageSize = size, totalCount = all.Count, hasMore = currentPage * size < all.Count });
});

app.MapGet("/tickets/{id:guid}/history", (Guid id, TicketStore store) =>
{
    var history = store.GetHistory(id);
    return history is null
        ? Results.NotFound(new ApiError("TICKET_NOT_FOUND", $"Không tìm thấy ticket {id}."))
        : Results.Ok(history);
});

app.MapPatch("/tickets/{id:guid}/status", (Guid id, ChangeStatusRequest request, TicketStore store) =>
{
    if (!Enum.IsDefined(request.Status))
        return Results.BadRequest(new ApiError("INVALID_STATUS", "Status không hợp lệ."));
    if (request.ExpectedVersion < 1)
        return Results.BadRequest(new ApiError("INVALID_VERSION", "ExpectedVersion phải lớn hơn 0."));
    if (request.Reason?.Length > 1000)
        return Results.BadRequest(new ApiError("INVALID_REASON", "Reason tối đa 1000 ký tự."));

    var result = store.ChangeStatus(id, request);
    if (result.Ticket is not null) return Results.Ok(result.Ticket);
    if (result.ErrorCode == "TICKET_NOT_FOUND") return Results.NotFound(new ApiError(result.ErrorCode, result.ErrorMessage!));
    if (result.ErrorCode == "VERSION_CONFLICT") return Results.Conflict(new ApiError(result.ErrorCode, result.ErrorMessage!));
    return Results.UnprocessableEntity(new ApiError(result.ErrorCode!, result.ErrorMessage!));
});

app.Run();

static bool TryParseEnum<TEnum>(string? raw, out TEnum? value) where TEnum : struct, Enum
{
    value = default;
    if (raw is null) return true;
    if (!Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)) return false;
    value = parsed;
    return true;
}
