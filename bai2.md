# Dự án tuần 1 — Hệ thống hỗ trợ yêu cầu khách hàng

Họ tên: Nguyễn Thị Quế
MSSV: dtc245200583
Lớp: CNTT K23D

## Phần tuần 1 đã thực hiện

- Mô tả mục tiêu và phạm vi hệ thống hỗ trợ khách hàng.
- Xác định vai trò Customer, Agent và Admin cùng các use case chính.
- Đặc tả quy tắc nghiệp vụ, trạng thái ticket và yêu cầu phi chức năng.
- Thiết kế kiến trúc, ERD và hợp đồng REST API.

## Mã nền kèm theo

Thư mục dự án có kèm phần triển khai nền cho các tuần 2–3: solution .NET 8, PostgreSQL schema migration, API tạo/tra cứu ticket, phân quyền, lịch sử thay đổi và cập nhật trạng thái.

- Mã nguồn: `SupportSystem-dtc245200583/`
- Thiết kế chi tiết: `SupportSystem-dtc245200583/THIET_KE_CHI_TIET.md`
- Kế hoạch 9 tuần: `SupportSystem-dtc245200583/KE_HOACH_9_TUAN.md`
- Yêu cầu, ERD và API contract: `SupportSystem-dtc245200583/docs/01-yeu-cau-erd-api.md`

## Chạy dự án

Cần Docker Desktop. Chạy từ thư mục `SupportSystem-dtc245200583`:

```powershell
docker compose up --build
```

API chạy tại `http://localhost:8080`; OpenAPI tại `http://localhost:8080/openapi/v1.json`. Xem README trong thư mục dự án để biết cách tạo ticket và cấu hình môi trường.

## Ghi chú

Migration SQL đầu tiên được nhúng và áp dụng khi API khởi động. Demo authentication chỉ dùng môi trường Development. Trạng thái build cần được xác nhận trên máy có thể restore các gói NuGet.
