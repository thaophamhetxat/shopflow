# ShopFlow

ShopFlow là solution .NET 8 theo mô hình Clean Architecture hướng đến 4 bounded context: Order, Inventory, Payment và Notification. Mục tiêu Day 38 là khởi tạo nền tảng đúng lịch NET_Schedule, giữ ranh giới dữ liệu riêng cho từng context và đảm bảo solution build được trước khi thêm nghiệp vụ trong các day tiếp theo.

## Công nghệ và chuẩn kiến trúc

- .NET 8 + ASP.NET Core minimal API
- EF Core 8 và PostgreSQL cho persistence ở từng bounded context
- Redis và RabbitMQ làm infrastructure local cho cache/message
- MediatR và FluentValidation sẽ được thêm ở Day 41 theo lịch
- YARP Gateway sẽ được thêm ở Day 49 theo lịch
- Mỗi context có database riêng: shopflow_order, shopflow_inventory, shopflow_payment, shopflow_notification

## Cách chạy local

1. Chạy docker compose up -d để khởi động PostgreSQL, Redis và RabbitMQ.
2. Chạy từng API bằng dotnet run --project src/Services/<Service>/<Project>.csproj.
3. Kiểm tra service bằng GET /health trên port được cấu hình khi app đã start.
4. Chạy gateway Day 49 theo command: `dotnet run --project src/Gateway/ShopFlow.Gateway --launch-profile http` sau khi 3 API đang chạy local.

> Lưu ý: mật khẩu local ShopFlow123! chỉ dùng cho bài tập trên máy dev, không được dùng cho môi trường production.

## Gateway Day 49

| Service | Port | Endpoint chính |
| --- | --- | --- |
| ShopFlow.Gateway (YARP) | `5000` | `/health`, `/health/live`, proxy route tới Order/Inventory/Payment |
| Order API | `5181` | `/api/v1/orders`, `/api/v2/orders/{id}` |
| Inventory API | `5140` | `/inventory/{id}`, `/inventory/reserve`, `/inventory/release` |
| Payment API | `5199` | `/payments`, `/payments/{id}`, `/payments/{id}/refund` |

## Lộ trình Day 38–67

| Day | Tên công việc |
| --- | --- |
| Day 38 | Solution setup + bounded context design |
| Day 39 | Domain model cho Order |
| Day 40 | Domain model cho Inventory/Payment/Notification |
| Day 41 | Application layer + MediatR + FluentValidation |
| Day 42 | EF Core + PostgreSQL integration |
| Day 43 | Outbox pattern |
| Day 45 | Idempotency |
| Day 47 | API versioning |
| Day 48 | Inventory + Payment skeleton |
| Day 49 | Gateway YARP |
| Day 52 | Saga coordination |
| Day 53 | Saga compensation |
| Day 54 | Resilience |
| Day 55 | Event sourcing |
| Day 64 | Jaeger + tracing |
| Day 65 | Prometheus + Grafana + health readiness |

Chi tiết từng ngày xem file ShopFlow_Lich_Trinh_Day38_67.md nếu repo bổ sung tài liệu lịch trình này.