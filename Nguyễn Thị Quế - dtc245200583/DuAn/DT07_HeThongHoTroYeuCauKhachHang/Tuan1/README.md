# Đề tài 07 — Hệ thống hỗ trợ yêu cầu khách hàng · Tuần 1

- **Sinh viên:** Nguyễn Thị Quế — dtc245200583
- **Lớp:** CNTT K23D
- **Nền tảng:** .NET 8

## 1. Bài toán

Xây dựng hệ thống tiếp nhận yêu cầu hỗ trợ của khách hàng. Ticket gồm người gửi, nội dung, mức ưu tiên, nhóm/người xử lý, trạng thái và lịch sử thay đổi.

Kiến trúc đích của đề tài:

~~~text
Client -> Support API -> RabbitMQ -> Assignment Worker -> Database
             ^                                  |
             | SignalR                           | Event
             +------ Notification Service <------+
~~~

Trong **Tuần 1**, hiện thực lát cắt đầu tiên để thực hành REST và tách Client khỏi API. API dùng danh sách trong bộ nhớ. PostgreSQL, RabbitMQ, Worker, SignalR và xác thực thật sẽ được bổ sung ở các tuần tiếp theo theo kế hoạch môn học.

## 2. MEP và phạm vi tuần 1

- **Đã hiện thực:** Client ↔ Support API dùng request-response qua HTTP.
- **MEP chính của kiến trúc hoàn chỉnh:** Queue-based, để giao ticket mới cho Assignment Worker xử lý bất đồng bộ.
- Client chỉ biết URL và JSON contract; Client không tham chiếu assembly của API.
- Dữ liệu hiện lưu trong Singleton TicketStore, vì vậy sẽ mất khi dừng API. Đây là giới hạn có chủ ý của lát cắt tuần 1.

## 3. Thành phần

| Thành phần | Công nghệ | Trách nhiệm tuần 1 |
|---|---|---|
| Support.Api | ASP.NET Core Minimal API, .NET 8 | Validate request, tạo và tra cứu ticket, đổi trạng thái, ghi lịch sử trong bộ nhớ. |
| Support.Client | .NET 8 Console, HttpClient | Gọi API độc lập qua HTTP, hiển thị status code, Location, ticket và lỗi. |
| TicketStore | Singleton + List | Lưu ticket và history trong vòng đời tiến trình; lock khi đọc/ghi. |
| RabbitMQ / Worker / Database / SignalR | Giai đoạn sau | Tự động phân công, lưu bền vững và thông báo realtime theo kiến trúc đích. |

## 4. Mô hình dữ liệu

- **SupportTicket:** Id, TicketNumber, RequesterId, RequesterName, Subject, Description, Priority, Status, Team, Assignee, CreatedAt, UpdatedAt, Version.
- **TicketChange:** Id, ActorId, Action, FieldName, OldValue, NewValue, Reason, OccurredAt.
- **Priority:** Low, Normal, High, Urgent.
- **Status:** New, Assigned, InProgress, Resolved.

Luồng trạng thái hợp lệ: New → Assigned → InProgress → Resolved. Mỗi thay đổi trạng thái tăng Version và ghi thêm một TicketChange.

## 5. API tối thiểu

| Method | Path | Mô tả |
|---|---|---|
| GET | / | Kiểm tra Support API đang chạy. |
| POST | /tickets | Tạo ticket; trả 201 Created và Location. |
| GET | /tickets | Danh sách, lọc theo status/priority/từ khóa, phân trang. |
| GET | /tickets/{id} | Chi tiết ticket; không tồn tại trả 404. |
| GET | /users/{userId}/tickets | Danh sách ticket của người gửi. |
| GET | /tickets/{id}/history | Lịch sử thay đổi. |
| PATCH | /tickets/{id}/status | Chuyển trạng thái; kiểm tra luồng và ExpectedVersion. |

JSON dùng camelCase; enum trả về dạng chuỗi. Request tạo ticket không nhận Id, TicketNumber, Status, Version hoặc CreatedAt; các giá trị này do server tạo.

Ví dụ tạo ticket:

~~~json
{
  "requesterId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "requesterName": "Nguyễn Thị Quế",
  "subject": "Không đăng nhập được",
  "description": "Mã xác thực bị từ chối khi tôi đăng nhập.",
  "priority": "High"
}
~~~

## 6. Cách chạy

Cần .NET 8 SDK. Mở terminal tại thư mục Tuần 1 này:

~~~powershell
dotnet build SupportSystem.sln
~~~

Terminal 1 — chạy API:

~~~powershell
dotnet run --project src/Support.Api --urls http://localhost:5000
~~~

Terminal 2 — chạy Client:

~~~powershell
dotnet run --project src/Support.Client -- http://localhost:5000
~~~

Client lần lượt gọi health, danh sách, tạo ticket, đọc lại theo Location, lọc, truy vấn ticket theo user, cập nhật trạng thái, gửi dữ liệu sai để nhận 400 và tra ID không tồn tại để nhận 404.

Có thể mở requests.http trong VS Code REST Client để gửi request riêng.

## 7. Ma trận tình huống cần quan sát

| Thao tác | Kết quả mong đợi | Ý nghĩa |
|---|---|---|
| GET / | 200 OK | API đang chạy. |
| GET /tickets | 200 OK | Danh sách ticket dạng JSON. |
| POST /tickets hợp lệ | 201 Created + Location | Tạo ticket mới và có URI để đọc lại. |
| POST /tickets thiếu requester/subject/description | 400 Bad Request | API validate dữ liệu tại biên. |
| GET /tickets/{guid không tồn tại} | 404 Not Found | Không tìm thấy tài nguyên. |
| PATCH sai chuyển trạng thái | 422 Unprocessable Entity | Luồng nghiệp vụ không hợp lệ. |
| PATCH với Version cũ | 409 Conflict | Ticket đã thay đổi; client cần tải lại. |
| Dừng API rồi gọi Client | Lỗi kết nối HTTP | Không có tiến trình lắng nghe; khác lỗi HTTP 4xx. |
| Khởi động lại API rồi đọc ticket cũ | Ticket không còn | Lưu trữ trong bộ nhớ; PostgreSQL sẽ thay thế ở giai đoạn sau. |

## 8. Phân biệt phạm vi tuần 1 và kiến trúc đích

Tuần 1 tập trung vào hợp đồng HTTP, validate, status code và Client gọi API độc lập. Các yêu cầu về RabbitMQ, assignment worker, database bền vững, SignalR và xác thực thuộc phần hoàn thiện sau. TicketStore trong bộ nhớ không được xem là lưu trữ production.

## 9. Cấu trúc thư mục

~~~text
Tuan1/
  SupportSystem.sln
  README.md
  requests.http
  src/
    Support.Api/
      Program.cs
      Models.cs
      Dtos.cs
      TicketStore.cs
    Support.Client/
      Program.cs
      Dtos.cs
~~~
