# ShopFlow

ShopFlow là hệ thống demo về order processing theo mô hình microservice và saga orchestration. Repo hiện đang tập trung vào 4 bounded context chính: Order, Inventory, Payment và Notification, cùng một gateway YARP để đóng vai trò route trung tâm.

## 1. Tổng quan

Mục tiêu của repo là mô phỏng luồng nghiệp vụ thực tế:
- Khách hàng tạo order
- Inventory kiểm tra tồn kho và reserve
- Payment xử lý thanh toán
- Order theo dõi tiến độ qua saga / outbox / event bus
- Notification xử lý thông báo hoặc worker dựa trên message queue

Các service hiện có trong repo được xây dựng theo hướng Clean Architecture rõ ràng: Domain → Application → Infrastructure → API.

## 2. Kiến trúc hiện tại

### Thành phần chính
- OrderService.API: API cho order và integration với inventory/payment
- InventoryService.API: API dùng cho stock check, reserve/release
- PaymentService.API: API thanh toán / gốc nghiệp vụ tài chính
- NotificationService.API + NotificationService.Worker: phục vụ notification / consumer background
- ShopFlow.Gateway: YARP reverse proxy cho các request tới downstream services
- ShopFlow.BuildingBlocks: shared abstractions, tracing, health checks, logging

### Cổng/port dùng trong local dev

| Service | Port | Ghi chú |
| --- | --- | --- |
| Gateway | `5000` | Route trung tâm qua YARP |
| Order API | `5181` | Endpoint chính cho order |
| Inventory API | `5140` | Check stock / reserve / release |
| Payment API | `5199` | Thanh toán và refund |
| RabbitMQ Management | `15672` | UI quản trị broker |
| Redis | `6379` | Cache + idempotency / dependency |
| PostgreSQL | `5432` | DB local của services |
| Jaeger UI | `16686` | Tracing |
| Prometheus | `9090` | Metrics |
| Grafana | `3000` | Dashboard metrics |

## 3. Công nghệ và nền tảng

- .NET 8 + ASP.NET Core
- Minimal API và API versioning
- EF Core + PostgreSQL
- Redis cho cache / idempotency / infra
- RabbitMQ dùng cho event bus / outbox / async integration
- YARP Gateway
- OpenTelemetry + Jaeger
- Prometheus + Grafana
- Docker Compose cho local infrastructure

## 4. Khởi động local

### Bước 1: khởi động infra

```bash
docker compose up -d
```

Các service chạy sau khi compose lên: PostgreSQL, Redis, RabbitMQ, Jaeger, Prometheus, Grafana và gateway Docker.

### Bước 2: chạy từng service .NET local

```bash
dotnet run --project src/Services/Order/OrderService.API/OrderService.API.csproj
```

```bash
dotnet run --project src/Services/Inventory/InventoryService.API/InventoryService.API.csproj
```

```bash
dotnet run --project src/Services/Payment/PaymentService.API/PaymentService.API.csproj
```

```bash
dotnet run --project src/Gateway/ShopFlow.Gateway/ShopFlow.Gateway.csproj
```

> Lưu ý: nếu đã chạy gateway Docker trong `docker compose`, nên dừng hoặc tránh xung đột port `5000` trước khi chạy local gateway host.

### Bước 3: health check

- `http://localhost:5181/health`
- `http://localhost:5140/health`
- `http://localhost:5199/health`
- `http://localhost:5000/health`

Các endpoint `/health/ready` cũng được định nghĩa để kiểm tra readiness khi downstream đang sẵn sàng nhận traffic.

## 5. Tài khoản và cấu hình môi trường

### PostgreSQL
Tại Docker Compose, container database được tạo với:
- user: `shopflow`
- password: `ShopFlow123!`

Tuy nhiên, các connection string runtime app local đang dùng:
- `Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;Password=shopflow123`

Đây là hai giá trị khác nhau và cần được phân biệt rõ ràng nhằm tránh nhầm lẫn khi debug local infrastructure.

### RabbitMQ
- user: `guest`
- password: `guest`
- queue chính quan sát trong smoke test: `shopflow.order-events`

## 6. Lộ trình Day 38–67 và trạng thái thực tế

| Day | Mục tiêu | Trạng thái |
| --- | --- | --- |
| Day 38 | Setup solution + bounded contexts | Đã triển khai |
| Day 39 | Domain model cho Order | Đã triển khai |
| Day 40 | Domain model cho Inventory/Payment/Notification | Đã triển khai |
| Day 41 | Application layer + MediatR + validation | Đã triển khai |
| Day 42 | EF Core + PostgreSQL integration | Đã triển khai |
| Day 43 | Outbox pattern | Đã có nền tảng |
| Day 45 | Idempotency | Đã có cơ chế trong cấu trúc |
| Day 47 | API versioning | Đã triển khai |
| Day 48 | Inventory + Payment skeleton | Đã triển khai |
| Day 49 | Gateway YARP | Đã triển khai |
| Day 52 | Saga coordination | Có nền tảng tương ứng |
| Day 53 | Saga compensation | Có nền tảng tương ứng |
| Day 54 | Resilience | Có triển khai qua middleware/health |
| Day 55 | Event sourcing / event flow | Có cấu trúc liên quan |
| Day 64 | Jaeger + tracing | Đã triển khai |
| Day 65 | Prometheus + Grafana + health readiness | Đã triển khai |
| Day 66 | Repo hygiene + stale fix + validation | Đã hoàn tất ở mức local verification |
| Day 67 | Final review + Definition of Done | Đã cập nhật docs và evidence |

## 7. Verification evidence

### Test suite
```bash
dotnet test ShopFlow.sln --logger "console;verbosity=minimal"
```

Kết quả thực tế ở trạng thái local: tests chính chạy thành công; các test performance-sensitive đã từng flaky trong full suite khi chạy đồng thời được kiểm lại riêng lẻ và pass. Đây là tình huống được chấp nhận trong quy tắc Day 67 khi không có regression mới.

### Smoke validation
- Gateway `/health` trả 200 khi downstream đang sẵn sàng
- `OrderService` có thể xử lý order hợp lệ qua gateway
- Order status đạt `Confirmed`
- Message xuất hiện trong RabbitMQ queue `shopflow.order-events`

## 8. Repo hygiene và ràng buộc

- Các placeholder không có logic nghiệp vụ đã được dọn sạch khỏi repo.
- Các registration in-memory trong `OrderService.Application` là stale và dễ gây hiểu nhầm, nhưng không phải runtime bug vì `OrderService.Infrastructure` override thực tế trong production composition.
- Mục tiêu hiện tại là project state thật, không phải mô tả “future plan” không còn khớp.

## 9. Cảnh báo quan trọng

Repo vẫn là một hệ thống demo / local development sample, không phải sản phẩm production-ready hoàn chỉnh với auth, secrets management, hoặc CI/CD hoàn chỉnh. Các warning NuGet và advisory package hiện còn tồn tại và nên được xử lý trong giai đoạn maintenance tiếp theo.

## 10. Tài liệu liên quan

- [docs/definition-of-done.md](./docs/definition-of-done.md)
- [docs/retrospective.md](./docs/retrospective.md)
- [docs/architecture-diagram.md](./docs/architecture-diagram.md)
- [docs/observability.md](./docs/observability.md)
- [docs/tracing.md](./docs/tracing.md)
- [docs/performance-baseline.md](./docs/performance-baseline.md)
