# Thiết kế chi tiết — Hệ thống hỗ trợ yêu cầu khách hàng

## 1. Mục tiêu và giả định

Hệ thống tiếp nhận ticket, phân phối ticket mới qua RabbitMQ để worker phân loại/gán nhóm hoặc nhân viên, lưu trạng thái và lịch sử, rồi gửi thông báo realtime bằng SignalR.

**Phạm vi bản đầu:** tạo và tra cứu ticket; gán tự động; chuyển trạng thái `New → Assigned → InProgress → Resolved`; lịch sử append-only; thông báo realtime; retry và chống xử lý lặp.

**Giả định để có thể bắt đầu triển khai:** ASP.NET Core Web API + SignalR, PostgreSQL, RabbitMQ; JWT có sẵn từ Identity Provider; ticket thuộc về một khách hàng, có thể gán một team và một assignee. Ticket `Resolved` là kết thúc, chưa hỗ trợ mở lại. Các giả định này cần xác nhận với chủ sản phẩm.

## 2. Kiến trúc

```text
Web/Mobile Client --HTTPS/JWT--> Support API --PostgreSQL
       |                              |             ^
       | SignalR                      | Outbox      | transaction
       v                              v             |
Notification Service <--RabbitMQ-- Assignment Worker
       ^                         TicketCreated
       +---- TicketAssigned / TicketStatusChanged
```

- **Support API:** xác thực, phân quyền, validate, CRUD/query ticket, cập nhật trạng thái, ghi history và outbox.
- **PostgreSQL:** nguồn dữ liệu chuẩn. API và Worker dùng chung DB ở bản đầu, mỗi thay đổi ticket phải cùng transaction với history và outbox.
- **RabbitMQ:** chuyển message bất đồng bộ; persistent messages, publisher confirms, retry giới hạn và dead-letter queue.
- **Assignment Worker:** tiêu thụ yêu cầu mới, áp dụng quy tắc gán, cập nhật ticket và phát event.
- **Notification Service:** nhận event, gửi SignalR tới các nhóm liên quan; không sửa dữ liệu nghiệp vụ.
- **Client:** dùng REST để đồng bộ dữ liệu chuẩn và SignalR để nhận cập nhật nhanh.

Dùng **Transactional Outbox** để tránh trường hợp DB đã commit nhưng message chưa gửi: ghi ticket/history/outbox trong cùng transaction; publisher nền gửi outbox sang RabbitMQ và đánh dấu published sau publisher confirm. Consumer phải idempotent vì RabbitMQ có thể giao lại message. Realtime không thay thế REST; client reconnect cần tải lại ticket.

## 3. Quy tắc nghiệp vụ

### Trạng thái và quyền chuyển

| Trạng thái hiện tại | Trạng thái kế tiếp | Ai thực hiện |
|---|---|---|
| Chưa có | New | API khi khách tạo |
| New | Assigned | Assignment Worker |
| Assigned | InProgress | Assignee hoặc Admin |
| InProgress | Resolved | Assignee hoặc Admin |

Server từ chối chuyển trạng thái không nằm trong bảng. Mỗi thao tác ghi actor, thời điểm, giá trị cũ/mới và lý do nếu có. Reassignment sau trạng thái Assigned chỉ cho nhân viên điều phối/Admin và cũng phải audit.

### Gán mặc định

1. Lấy category được xác định hoặc gán `General`.
2. Tra ánh xạ category sang team đang hoạt động.
3. Chọn thành viên đủ điều kiện có ít ticket đang mở nhất; phá hòa ổn định theo user ID.
4. Nếu team có sẵn nhưng không có nhân viên đủ điều kiện, gán team và để assignee rỗng.
5. Nếu không có ánh xạ, dùng team mặc định `Customer Support`. Nếu thiếu cấu hình, giữ ticket ở New, ghi lỗi và chuyển message qua retry/DLQ để xử lý vận hành.

Danh sách category, team mặc định và thuật toán chọn nhân viên là cấu hình cần chốt. Mức ưu tiên đề xuất: `Low`, `Normal`, `High`, `Urgent`; mặc định Normal.

## 4. Mô hình dữ liệu

- **Tickets:** `Id UUID PK`, `TicketNumber BIGINT UNIQUE`, `RequesterId`, `Subject(200)`, `Description`, `Category`, `Priority`, `Status`, `TeamId nullable`, `AssigneeId nullable`, `CreatedAt`, `UpdatedAt`, `AssignedAt nullable`, `ResolvedAt nullable`, `Version INT`, `IsDeleted BOOL`.
- **TicketHistory:** `Id`, `TicketId`, `ActorId nullable` (null nghĩa hệ thống), `Action`, `FieldName`, `OldValue`, `NewValue`, `Reason`, `OccurredAt UTC`, `CorrelationId`. Append-only.
- **Teams:** `Id`, `Name`, `IsActive`.
- **TeamMembers:** `TeamId`, `UserId`, `IsActive`, `JoinedAt`; unique TeamId + UserId.
- **CategoryTeamRules:** `Category`, `TeamId`, `IsDefault`, `IsActive`.
- **OutboxMessages:** `Id`, `Type`, `AggregateId`, `Payload`, `OccurredAt`, `PublishedAt nullable`, `Attempts`, `LastError`.
- **ProcessedMessages:** `ConsumerName`, `MessageId`, `ProcessedAt`; unique ConsumerName + MessageId.

Index chính: requester + created time; status + priority + created time; team/status; assignee/status. Dùng optimistic concurrency bằng Version. Mọi timestamp lưu UTC.

## 5. REST API

Base path `/api/v1`; JSON camelCase; thời gian ISO-8601 UTC; lỗi theo `application/problem+json`.

| Method và path | Mục đích |
|---|---|
| `POST /tickets` | Tạo ticket. Request gồm subject, description, priority. Requester lấy từ JWT, không nhận tin requesterId từ body. Trả 201, ticket ở New. Hỗ trợ Idempotency-Key. |
| `GET /tickets` | Danh sách có filter status, priority, teamId, assigneeId, khoảng thời gian, từ khóa; cursor pagination. |
| `GET /tickets/{id}` | Chi tiết ticket; kiểm tra quyền sở hữu/nhóm. Trả 404 nếu không tồn tại hoặc không được xem. |
| `PATCH /tickets/{id}/status` | Body gồm status, expectedVersion, reason. Kiểm tra role, transition và version. |
| `GET /users/{userId}/tickets` | Ticket của user; khách chỉ được truy vấn user ID trong token. |
| `GET /tickets/{id}/history` | Lịch sử phân trang, theo cùng quyền đọc ticket. |
| `PATCH /tickets/{id}/assignment` | Gán lại team/assignee cho người có quyền điều phối. |

Pagination mặc định 20, tối đa 100; sort ổn định theo CreatedAt và Id giảm dần. Khách chỉ thấy ticket mình tạo; Agent thấy ticket của mình/nhóm; Admin xem toàn bộ. Lỗi chính: 400 sai dữ liệu, 401 chưa đăng nhập, 403 thiếu quyền, 404 không thấy/không được xem, 409 xung đột Version/idempotency, 422 sai nghiệp vụ, 429 quá giới hạn.

Ví dụ tạo ticket:
```json
{ "subject": "Không đăng nhập được", "description": "Lỗi sau khi nhập mã xác thực.", "priority": "Normal" }
```

Ví dụ cập nhật trạng thái:
```json
{ "status": "InProgress", "expectedVersion": 4, "reason": "Đang kiểm tra lỗi." }
```

## 6. RabbitMQ events

Envelope thống nhất: `messageId, type, occurredAt, aggregateId, correlationId, data`. Dùng version schema trong type.

- `TicketCreated.v1` (API → assignment): ticketId, requesterId, priority, thời điểm tạo; chỉ gửi description nếu worker thật sự cần phân loại nội dung.
- `TicketAssigned.v1` (Worker/API → event exchange): ticketId, requesterId, teamId, assigneeId, category, status, version.
- `TicketStatusChanged.v1`: ticketId, requesterId, teamId, assigneeId, oldStatus, newStatus, actorId, version, occurredAt.

Exchange topic `support.events`; routing key `ticket.created`, `ticket.assigned`, `ticket.status_changed`; queue riêng cho assignment và notifications. Consumer ghi ProcessedMessages cùng transaction thay đổi trước khi ack. Retry theo backoff; message lỗi vĩnh viễn chuyển DLQ và tạo cảnh báo. Event có Version để bỏ qua event cũ/đến sai thứ tự.

## 7. SignalR realtime

Hub `/hubs/tickets`, chỉ HTTPS và JWT. Server tự đưa kết nối vào `user:{userId}`; không cho client tự đăng ký group tùy ý. Nhân viên vào `team:{teamId}` sau khi kiểm tra membership. Phát các event `ticket.created`, `ticket.assigned`, `ticket.statusChanged` với payload gọn (ticketId, ticketNumber, status, teamId, assigneeId, version, occurredAt). Tránh phát description/nội dung nhạy cảm. Nhiều instance cần Redis backplane hoặc Azure SignalR. Khi reconnect client gọi REST để đồng bộ.

## 8. Luồng xử lý

### Tạo và phân công

1. Client POST ticket với JWT và idempotency key.
2. API xác thực/validate; transaction ghi Tickets(New), TicketHistory(TicketCreated), Outbox(TicketCreated).
3. Publisher gửi message và đánh dấu outbox published sau confirm.
4. Worker nhận message; bỏ qua nếu đã xử lý; đọc ticket và áp dụng rule.
5. Transaction cập nhật category/team/assignee, chuyển sang Assigned nếu gán được, tăng Version, ghi history, outbox event và ProcessedMessages.
6. Notification Service tiêu thụ event và phát SignalR đến requester, team và assignee.
7. Client hiển thị cập nhật; REST là nguồn đọc chính xác.

### Chuyển trạng thái

API xác minh token, quyền thao tác, transition và expectedVersion; transaction cập nhật trạng thái/timestamp, history và outbox. Notification Service phát sự kiện cho các bên liên quan. Nếu version không khớp, trả 409 để client tải lại.

## 9. Bảo mật, lỗi và quan sát

- JWT kiểm tra issuer/audience/signature; TLS; phân quyền theo role và quyền trên từng ticket.
- Không tin requesterId/role/team do client gửi. Rate limit tạo ticket và kết nối; validate kích thước nội dung; encode nội dung khi hiển thị.
- Không log token hoặc toàn bộ description; secrets lấy từ secret manager/environment.
- API chết sau commit: Outbox gửi tiếp khi phục hồi. Worker nhận trùng: ProcessedMessages/idempotency ngăn cập nhật lặp. Notification ngừng: queue bền giữ event; client đồng bộ qua REST.
- Gắn correlationId xuyên API → outbox → RabbitMQ → Worker → Notification; structured logs, trace và metrics.
- Theo dõi API latency/error, queue backlog, outbox message tồn lâu, assignment lỗi, retry/DLQ, SignalR connections; health endpoints live/ready và cảnh báo phụ thuộc lỗi.

## 10. Cấu trúc mã và triển khai

```text
src/
  Support.Api/                  # REST, auth, SignalR hub
  Support.Application/          # use cases, DTO, validation
  Support.Domain/               # Ticket, state machine, domain events
  Support.Infrastructure/       # PostgreSQL, outbox, RabbitMQ
  Support.AssignmentWorker/     # consumer và assignment rules
  Support.NotificationService/  # consumer và SignalR dispatch
tests/
  Support.Domain.Tests/
  Support.Api.IntegrationTests/
  Support.Messaging.IntegrationTests/
deploy/
  docker-compose.yml
  migrations/
```

Local chạy API, worker, notification service, PostgreSQL và RabbitMQ bằng Docker Compose. Staging/production dùng migrations có kiểm soát, backup DB, durable queues, secret manager; scale service stateless. Nếu scale SignalR nhiều instance, bật backplane. CI nên build, kiểm tra migration và chạy kiểm tra tự động ở các tầng domain/API/messaging.

## 11. Kế hoạch thực hiện và nghiệm thu

1. Khởi tạo solution, cấu hình môi trường, container và health check.
2. Domain, schema/migration, state machine, history, concurrency.
3. REST API, validation, phân quyền, filter/pagination.
4. Outbox, RabbitMQ topology, idempotent consumer.
5. Assignment Worker và quy tắc gán.
6. Notification Service, SignalR group, reconnect/resync client.
7. Logging/metrics/tracing, tài liệu triển khai và vận hành.

Nghiệm thu khi: ticket tạo ở New và chỉ đúng người xem được; worker gán theo rule, không nhân đôi khi message giao lại; chuyển trạng thái đúng luồng và từ chối sai quyền/version; mọi thay đổi có history; realtime chỉ tới client liên quan; RabbitMQ/Notification gián đoạn không làm mất dữ liệu; REST hỗ trợ phân trang/lọc và không lộ ticket trái quyền.

## 12. Các quyết định sản phẩm còn thiếu

Cần chốt: Identity Provider; category/team mặc định; round-robin hay ít ticket mở; mức priority khách được chọn; quyền sửa priority; chính sách reopen; có comment/đính kèm/email/SLA trong bản đầu không; thời hạn lưu/xóa dữ liệu; triển khai cloud hay on-prem. Đây là câu hỏi mở của đề bài, không phải yêu cầu đã có.
