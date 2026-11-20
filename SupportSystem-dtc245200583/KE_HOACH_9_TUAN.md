# Kế hoạch hoàn thiện dự án trong 9 tuần

## Mục tiêu

Hoàn thiện MVP **Hệ thống hỗ trợ yêu cầu khách hàng** trong 9 tuần, chia thành 3 phần. Mỗi phần có sản phẩm chạy được và tiêu chí nghiệm thu. Lịch giả định nhóm có thể làm đều mỗi tuần; nếu lịch học/nhân lực thay đổi, cần điều chỉnh khối lượng chứ không bỏ mốc tích hợp.

## Phần 1 — Nền tảng và nghiệp vụ ticket (Tuần 1–3)

**Mục tiêu:** chốt yêu cầu và hoàn thiện nền API/dữ liệu cho luồng ticket cơ bản.

| Tuần | Công việc | Sản phẩm cuối tuần |
|---|---|---|
| 1 | Chốt use case, vai trò Customer/Agent/Admin, quy tắc trạng thái và priority. Vẽ kiến trúc, ERD, API contract. Tạo solution .NET 8, cấu hình PostgreSQL và Docker Compose. | Tài liệu yêu cầu/ERD/API; solution khởi chạy được; schema ban đầu. |
| 2 | Xây Ticket, TicketHistory và DbContext; cấu hình migration; tạo API POST /tickets, GET /tickets, GET /tickets/{id}. Thêm validate, phân trang và lọc cơ bản. | Có thể tạo và tra cứu ticket qua API; dữ liệu tồn tại sau khi khởi động lại. |
| 3 | Thêm authentication/authorization, endpoint tickets theo user, cập nhật trạng thái có kiểm tra luồng và version; lưu lịch sử mọi thay đổi. | Luồng thủ công tạo → xem → đổi trạng thái hoạt động; quyền Customer/Agent/Admin được kiểm tra. |

**Mốc nghiệm thu phần 1:** API tạo và truy vấn đúng dữ liệu; customer không xem ticket người khác; trạng thái sai luồng hoặc sai quyền bị từ chối; lịch sử ghi đủ actor và thay đổi.

## Phần 2 — Xử lý bất đồng bộ và realtime (Tuần 4–6)

**Mục tiêu:** tự động gán ticket, phát event an toàn và cập nhật client theo thời gian thực.

| Tuần | Công việc | Sản phẩm cuối tuần |
|---|---|---|
| 4 | Khai báo RabbitMQ exchange/queue; triển khai Transactional Outbox và publisher; phát TicketCreated.v1. Thêm correlation ID và retry cơ bản. | Tạo ticket được ghi DB và message được chuyển bền vững, không phụ thuộc gửi message trực tiếp trong request. |
| 5 | Hoàn thiện Assignment Worker: phân loại mặc định, gán team, cập nhật trạng thái/history/outbox; chống xử lý message trùng; cấu hình retry và DLQ. | Worker xử lý ticket mới đúng một lần về mặt nghiệp vụ; lỗi có thể quan sát và xử lý lại. |
| 6 | Hoàn thiện Notification Service/SignalR; phát sự kiện gán và đổi trạng thái tới user liên quan; client reconnect và đồng bộ lại qua REST. | Luồng API → RabbitMQ → Worker → event → SignalR chạy xuyên suốt. |

**Mốc nghiệm thu phần 2:** ticket mới được Worker gán team; message giao lặp không tạo lịch sử/gán trùng; người tạo nhận cập nhật realtime; khi client ngắt kết nối vẫn lấy lại trạng thái mới nhất qua REST.

## Phần 3 — Client, tích hợp và bàn giao (Tuần 7–9)

**Mục tiêu:** hoàn thiện trải nghiệm sử dụng, xử lý tình huống lỗi và chuẩn bị demo/bàn giao.

| Tuần | Công việc | Sản phẩm cuối tuần |
|---|---|---|
| 7 | Làm giao diện Client: đăng nhập theo cấu hình môi trường, tạo ticket, danh sách/lọc, trang chi tiết và lịch sử. Hiển thị trạng thái realtime. | Người dùng thao tác được các luồng chính qua UI thay vì chỉ gọi API. |
| 8 | Làm giao diện Agent/Admin: danh sách ticket, xem chi tiết, bắt đầu xử lý, giải quyết; kiểm tra quyền, lỗi, loading/empty state. Bổ sung log, health check, cấu hình chạy local. | Luồng Customer và Agent/Admin hoàn chỉnh; lỗi phổ biến có thông báo rõ. |
| 9 | Tích hợp end-to-end; xử lý lỗi còn lại; chuẩn bị dữ liệu demo, hướng dẫn cài đặt/chạy, tài liệu kiến trúc và API; chuẩn bị slide/kịch bản trình bày. | Bản MVP đóng gói được; README, tài liệu, kịch bản demo và danh sách giới hạn/hướng phát triển. |

**Mốc nghiệm thu phần 3:** người dùng tạo ticket trên UI, ticket được tự động gán, Agent cập nhật trạng thái, Customer nhận realtime, lịch sử đầy đủ; nhóm khác có thể chạy theo README.

## Phân công theo vai trò

Nếu nhóm có nhiều thành viên, chia theo luồng công việc thay vì chia theo tuần:

- **Backend/API:** domain, PostgreSQL, REST, quyền truy cập, history.
- **Messaging/Realtime:** outbox, RabbitMQ, Worker, SignalR, retry và observability.
- **Client/Integration:** giao diện, gọi API, SignalR client, tích hợp luồng và demo.
- Một người phụ trách tích hợp/release giữ solution chạy đồng nhất; mọi phần dùng chung API contract và được tích hợp từ sớm.

Nếu nhóm ít hơn ba người, gộp Messaging/Realtime với Backend; nếu chỉ một người, làm lần lượt theo kế hoạch tuần, bắt đầu UI đơn giản từ tuần 7.

## Quy tắc quản lý tiến độ

- Cuối mỗi tuần phải có phần mềm chạy được và demo ngắn; không để đến tuần 9 mới ghép các nhánh.
- Chốt contract giữa API, RabbitMQ event và client trước khi làm song song.
- Mỗi thay đổi schema có migration; không sửa database thủ công mà không ghi lại.
- Giữ một danh sách lỗi/việc còn lại; tuần 9 chỉ sửa lỗi và hoàn thiện bàn giao, không nhận thêm tính năng lớn.
- Nếu chậm tiến độ, ưu tiên luồng cốt lõi: tạo ticket → gán team → cập nhật trạng thái → lưu history → realtime. Có thể hoãn giao diện quản trị nâng cao, SLA, file đính kèm, email/SMS.

## Giả định và phần cần xác nhận

- Đề bài chưa yêu cầu giao diện cụ thể; kế hoạch thêm Client UI để hoàn chỉnh sản phẩm đầu cuối. Có thể chọn React/TypeScript hoặc Blazor trước tuần 1.
- Quy tắc hiện tại trong starter chỉ gán team mặc định. Gán nhân viên cá nhân cần có danh sách team/member và tiêu chí chọn nhân viên; nếu đây là yêu cầu chấm điểm, phải đưa việc đó vào tuần 3–5.
- Thời lượng 9 tuần là kế hoạch, không phải ước lượng cam kết cho một quy mô nhóm cụ thể.
