# Day 64 — Distributed tracing với OpenTelemetry và Jaeger

## 1. Span đã đặt trong code

- `order.place` — `src/Services/Order/OrderService.Application/Commands/PlaceOrderCommandHandler.cs`
- `saga.step.reserve` — `src/Services/Order/OrderService.Application/Sagas/OrderSaga.cs`
- `saga.step.payment` — `src/Services/Order/OrderService.Application/Sagas/OrderSaga.cs`
- `outbox.process` — `src/Services/Order/OrderService.Infrastructure/Outbox/OutboxProcessor.cs`
- `inventory.reserve` — `src/Services/Inventory/InventoryService.Application/Commands/ReserveStockCommandHandler.cs`
- `payment.process` — `src/Services/Payment/PaymentService.Application/Commands/ProcessPaymentCommandHandler.cs`

Các span dùng chung `ActivitySource` `ShopFlow` ở `src/BuildingBlocks/ShopFlow.BuildingBlocks/ShopFlowActivitySource.cs`.

## 2. Service name và thiết lập

- `Order API`: `shopflow-order`
- `Inventory API`: `shopflow-inventory`
- `Payment API`: `shopflow-payment`
- Tất cả 3 API đều gọi `AddShopFlowTracing(..., serviceName)` ở `Program.cs` để dùng chung `ResourceBuilder` và OTLP exporter.

## 3. Chạy local

```bash
docker compose up -d postgres redis rabbitmq jaeger
```

Sau đó khởi động 3 API với `dotnet run` trên máy:

```bash
dotnet run --project src/Services/Order/OrderService.API --urls http://localhost:5181
dotnet run --project src/Services/Inventory/InventoryService.API --urls http://localhost:5140
dotnet run --project src/Services/Payment/PaymentService.API --urls http://localhost:5199
```

## 4. Mở Jaeger UI

- URL: `http://localhost:16686`
- Chọn service `shopflow-order` và tìm trace của `POST /api/v1/orders` hoặc `POST /orders` theo route map hiện tại.

## 5. Giới hạn thiết kế Day 64

- `outbox.process` là span gốc vì outbox không lưu traceparent trong bảng `outbox_messages`; schema không thay đổi ở Day 64.
- Gateway YARP vẫn forward header `traceparent` mặc định, nên không có span riêng cho gateway ở Day 64.
- Day 64 không wire Serilog; log scope hiện tại chỉ nối `TraceId`/`SpanId` cho log ASP.NET Core và sẽ được thay thế bởi Serilog enrichment ở Day 65.

## 6. Smoke check

Sau khi 3 API health check trả 200, có thể tạo order thật bằng POST với customerId mới và 1 item `Kiếm Sắt` số lượng 1. Trên Jaeger UI, kiểm tra `order.place` xuất hiện trong trace của order vừa tạo.
