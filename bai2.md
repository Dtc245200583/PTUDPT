# Bài 2 — Hệ thống hỗ trợ yêu cầu khách hàng (Tuần 1–3)

Họ tên: Nguyễn Thị Quế
MSSV: dtc245200583
Lớp: CNTT K23D

## Phạm vi đã thực hiện

- Tuần 1: mô tả yêu cầu, vai trò, quy tắc nghiệp vụ, ERD và REST API contract.
- Tuần 2: solution .NET 8, PostgreSQL schema migration có version, API tạo/tra cứu/lọc/phân trang ticket.
- Tuần 3: xác thực JWT ngoài Development, demo identity chỉ cho Development, phân quyền theo customer/agent/admin, gán team/assignee, cập nhật trạng thái, lịch sử và optimistic concurrency.

## Nội dung

- Mã nguồn: `SupportSystem-dtc245200583/`
- Thiết kế chi tiết: `SupportSystem-dtc245200583/THIET_KE_CHI_TIET.md`
- Kế hoạch 9 tuần: `SupportSystem-dtc245200583/KE_HOACH_9_TUAN.md`
- Yêu cầu, ERD và API contract: `SupportSystem-dtc245200583/docs/01-yeu-cau-erd-api.md`

## Chạy dự án

Cần Docker Desktop, chạy từ thư mục `SupportSystem-dtc245200583`:

```powershell
docker compose up --build
```

API chạy tại `http://localhost:8080`; OpenAPI tại `http://localhost:8080/openapi/v1.json`. Xem README trong thư mục dự án để biết cách tạo ticket và cấu hình môi trường.

## Ghi chú

Migration SQL đầu tiên được nhúng và áp dụng khi API khởi động. Demo authentication chỉ dùng môi trường Development. Trạng thái build cần được xác nhận trên máy có thể restore các gói NuGet.
