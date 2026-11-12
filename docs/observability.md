# Day 65 — Metrics, logging và health checks

Tài liệu này mô tả cấu hình observability local của ShopFlow, được bổ sung ở Day 65.
Các API dùng chung Serilog JSON, Prometheus metrics và readiness checks; Gateway và Worker có giới hạn phạm vi riêng.
Dashboard Grafana được provision từ `docker/grafana/dashboards/shopflow-dashboard.json`.

## Structured logging

Order, Inventory, Payment và Notification API ghi mỗi log event thành một dòng JSON ra console. Mỗi event có property `Service`; khi có `Activity.Current`, enricher thêm `TraceId` và `SpanId` để liên kết log với trace Jaeger. Khi chạy bằng `dotnet run`, xem log trực tiếp ở console; với container, dùng `docker logs <container>`. Day 65 không ghi log ra file và không cấu hình section Serilog trong `appsettings.json`.

Gateway giữ logging mặc định, chưa được wire Serilog ở Day 65.

## Prometheus metrics

Mỗi API xuất metrics tại `/metrics`:

| Metric | Loại / nguồn | Ý nghĩa |
| --- | --- | --- |
| `http_requests_total` | Counter qua `UseHttpMetrics()` | Số request HTTP theo method, status code và handler. |
| `http_request_duration_seconds` | Histogram qua `UseHttpMetrics()` | Thời lượng xử lý request HTTP theo method, status code và handler. |
| `shopflow_orders_created_total` | Counter `ShopFlowMetrics` | Số order được persist thành công. |
| `shopflow_saga_runs_total` | Counter `ShopFlowMetrics`, nhãn `outcome` | Kết quả saga: `completed`, `failed_inventory` hoặc `failed_payment`. |

Prometheus chạy ở `http://localhost:9090` và scrape Order (`5181`), Inventory (`5140`), Payment (`5199`) cùng Notification API (`5035`) qua `host.docker.internal`. Grafana chạy ở `http://localhost:3000`; dashboard provisioned tên `ShopFlow` có đúng bốn panel HTTP request rate, HTTP p99 latency, Orders created và Saga runs by outcome.

Grafana dùng tài khoản mặc định local `admin` / `admin`; chỉ dùng trong môi trường dev local và cần đổi trước khi dùng ở môi trường khác.

## Package versions

- Serilog.AspNetCore `8.0.3`; Serilog.Sinks.Console `6.0.0`.
- prometheus-net and prometheus-net.AspNetCore `8.2.1`.
- AspNetCore.HealthChecks.NpgSql `8.0.2`; AspNetCore.HealthChecks.Redis `8.0.1`.
- AspNetCore.HealthChecks.Rabbitmq `9.0.0` targets `net8.0` and uses RabbitMQ.Client `7.0.0`, matching Order infrastructure. Version `8.0.2` requires RabbitMQ.Client `6.8.1` and caused a runtime `TypeLoadException` with the application's version, so only this health-check package uses a different major version.

## Health endpoints

- `/health/live` chỉ xác nhận process HTTP còn sống; không probe dependency.
- `/health/ready` chạy dependency checks có tag `ready` và trả `503` nếu một dependency unhealthy.
- `/health` chạy mọi health check đã đăng ký; vì vậy dependency down cũng làm endpoint trả `503`.

Notification API hiện không có dependency ngoài, nên readiness của nó chỉ xác nhận app đã khởi động. Notification Worker là `IHost` thuần, không có HTTP server nên không có endpoint `/health/*`. Gateway giữ `/health` tổng hợp hiện có và bổ sung `/health/ready` để probe readiness của Order, Inventory và Payment; Gateway chưa được wire Serilog hoặc metrics ở Day 65.
