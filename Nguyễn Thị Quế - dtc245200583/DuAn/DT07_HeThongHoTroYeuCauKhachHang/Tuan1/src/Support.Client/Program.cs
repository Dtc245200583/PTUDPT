using System.Net;
using System.Net.Http.Json;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
var baseUrl = args.FirstOrDefault() ?? "http://localhost:5000";
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
Console.WriteLine($"Support.Client đang gọi {baseUrl}");

try
{
    var health = await http.GetFromJsonAsync<Dictionary<string, string>>("/");
    Console.WriteLine($"API: {health?["message"]}");

    Console.WriteLine("\n=== 1. Danh sách ticket ban đầu ===");
    await PrintList(http, "/tickets");

    Console.WriteLine("\n=== 2. Tạo ticket mới ===");
    var requesterId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    var request = new CreateTicketRequest(
        requesterId,
        "Nguyễn Thị Quế",
        "Không đăng nhập được",
        "Mã xác thực bị từ chối khi tôi đăng nhập.",
        "High");
    using var createResponse = await http.PostAsJsonAsync("/tickets", request);
    Console.WriteLine($"POST /tickets → {(int)createResponse.StatusCode} {createResponse.StatusCode}");
    if (createResponse.StatusCode != HttpStatusCode.Created)
    {
        await PrintError(createResponse);
        return;
    }

    var created = await createResponse.Content.ReadFromJsonAsync<TicketDto>();
    var location = createResponse.Headers.Location?.ToString();
    Console.WriteLine($"Location: {location}");
    if (created is null || location is null)
    {
        Console.WriteLine("API không trả ticket hoặc header Location.");
        return;
    }
    PrintTicket(created);

    Console.WriteLine("\n=== 3. Đọc lại ticket theo Location ===");
    using var detailResponse = await http.GetAsync(location);
    var ticket = await detailResponse.Content.ReadFromJsonAsync<TicketDto>();
    Console.WriteLine($"GET {location} → {(int)detailResponse.StatusCode} {detailResponse.StatusCode}");
    if (ticket is not null) PrintTicket(ticket);

    Console.WriteLine("\n=== 4. Lọc ticket theo mức ưu tiên ===");
    await PrintList(http, "/tickets?priority=High&page=1&pageSize=10");

    Console.WriteLine("\n=== 5. Ticket của người gửi ===");
    await PrintList(http, $"/users/{requesterId}/tickets");

    Console.WriteLine("\n=== 6. Cập nhật trạng thái ===");
    foreach (var nextStatus in new[] { "Assigned", "InProgress", "Resolved" })
    {
        using var response = await http.PatchAsJsonAsync($"/tickets/{created.Id}/status",
            new ChangeStatusRequest(nextStatus, created.Version, "Cập nhật trong bài thực hành."));
        Console.WriteLine($"PATCH /tickets/{created.Id}/status → {(int)response.StatusCode} {response.StatusCode}");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            await PrintError(response);
            break;
        }
        created = await response.Content.ReadFromJsonAsync<TicketDto>();
        if (created is null) break;
        Console.WriteLine($"  Trạng thái hiện tại: {created.Status}, version {created.Version}");
    }

    Console.WriteLine("\n=== 7. Gửi ticket không hợp lệ (mong đợi 400) ===");
    using var invalidResponse = await http.PostAsJsonAsync("/tickets",
        new CreateTicketRequest(Guid.Empty, "", "", "", "Normal"));
    Console.WriteLine($"POST /tickets → {(int)invalidResponse.StatusCode} {invalidResponse.StatusCode}");
    await PrintError(invalidResponse);

    Console.WriteLine("\n=== 8. Tra ticket không tồn tại (mong đợi 404) ===");
    using var missingResponse = await http.GetAsync($"/tickets/{Guid.NewGuid()}");
    Console.WriteLine($"GET /tickets/{{id}} → {(int)missingResponse.StatusCode} {missingResponse.StatusCode}");
    await PrintError(missingResponse);
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"Không kết nối được API: {ex.Message}");
    Console.WriteLine("Hãy chạy Support.Api trước và kiểm tra URL/cổng.");
}

static async Task PrintList(HttpClient http, string path)
{
    using var response = await http.GetAsync(path);
    Console.WriteLine($"GET {path} → {(int)response.StatusCode} {response.StatusCode}");
    if (response.StatusCode != HttpStatusCode.OK)
    {
        await PrintError(response);
        return;
    }

    var result = await response.Content.ReadFromJsonAsync<TicketListDto>();
    Console.WriteLine($"Tổng {result?.TotalCount ?? 0} ticket");
    foreach (var item in result?.Items ?? [])
        Console.WriteLine($"  #{item.TicketNumber} | {item.Subject} | {item.Priority} | {item.Status}");
}

static async Task PrintError(HttpResponseMessage response)
{
    try
    {
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        if (error is null)
        {
            Console.WriteLine($"  HTTP {(int)response.StatusCode}, không có ApiError.");
            return;
        }
        Console.WriteLine($"  {error.Code}: {error.Message}");
        foreach (var (field, messages) in error.Errors ?? [])
            Console.WriteLine($"  {field}: {string.Join("; ", messages)}");
    }
    catch (System.Text.Json.JsonException)
    {
        Console.WriteLine($"  HTTP {(int)response.StatusCode}, body không phải ApiError.");
    }
}

static void PrintTicket(TicketDto ticket)
{
    Console.WriteLine($"Ticket #{ticket.TicketNumber} | {ticket.Id}");
    Console.WriteLine($"  {ticket.Subject} | {ticket.Priority} | {ticket.Status} | version {ticket.Version}");
    Console.WriteLine($"  Người gửi: {ticket.RequesterName} ({ticket.RequesterId})");
    Console.WriteLine($"  Nội dung: {ticket.Description}");
}
