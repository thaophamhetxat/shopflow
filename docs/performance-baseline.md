# Baseline hiệu năng Order API — Day 51

## Môi trường và cách đo

- Ngày đo: 2026-11-11; script ghi báo cáo lúc `13:46:00 +07:00`.
- Máy đo: Windows `DESKTOP-C54PUKV`, phiên bản hệ điều hành `Microsoft Windows NT 10.0.26300.0`; .NET SDK `10.0.403`; Windows PowerShell `5.1.26100.9818`.
- Docker Desktop/Engine: Docker CLI client/server `29.9.3 / 29.9.3`.
- Order API chạy cục bộ bằng `dotnet run`, môi trường Development, tại `http://localhost:5181`; kiểm tra `GET /health` trả `200` trước khi đo.
- PostgreSQL 16 và Redis 7 chạy bằng `docker compose`; tại thời điểm kiểm tra, PostgreSQL và Redis đều healthy. RabbitMQ cũng đang chạy trong Compose nhưng không nằm trên đường đi của các request Order được đo.
- Script: [`tools/benchmark/measure-order-endpoints.ps1`](../tools/benchmark/measure-order-endpoints.ps1). Mỗi endpoint nhận 100 request tuần tự; không có request song song. Thời gian đo bằng `System.Diagnostics.Stopwatch`. p50/p99 dùng nearest-rank; các request sai status mong đợi bị loại khỏi latency và được đếm ở `Errors`.
- Dữ liệu setup: tạo 5 đơn cho cùng một `customerId` trước khi đo GET list; tạo một đơn mới ngoài thời gian đo trước mỗi DELETE. POST dùng `customerId` và `X-Idempotency-Key` mới cho mỗi request để không đo lại đơn cũ do idempotency. GET detail/v2 dùng các `orderId` vừa tạo ở lượt POST.
- Output nguyên gốc của lần chạy nằm tại [`tools/benchmark/baseline-day51.txt`](../tools/benchmark/baseline-day51.txt); các số dưới đây được chép nguyên từ file đó.

> **Giới hạn quan trọng của số liệu:** lúc đo, working tree đã có thay đổi chưa commit trong `OrderRepository.cs` và hai project file Order; các thay đổi này không do Day 51 tạo. Đặc biệt, repository đang đọc toàn bộ Orders rồi lọc trong bộ nhớ. Vì vậy đây là số đo thật của working tree tại thời điểm chạy, không phải baseline có thể tái lập từ riêng commit `770ca84` hoặc một working tree sạch. Cần giữ cảnh báo này khi dùng số liệu để so sánh.

## Kết quả

Đơn vị latency là mili-giây. Mỗi hàng có 100 request; tất cả đều trả status mong đợi nên `Success = 100`, `Errors = 0`.

| Endpoint | Requests | Success | Avg (ms) | p50 (ms) | p99 (ms) | Max (ms) | Errors |
|---|---:|---:|---:|---:|---:|---:|---:|
| `POST /api/v1/orders` | 100 | 100 | 14.85 | 14.50 | 20.60 | 20.64 | 0 |
| `GET /api/v1/orders/{id}` | 100 | 100 | 6.65 | 6.53 | 8.64 | 8.86 | 0 |
| `GET /api/v1/orders?customerId=...&pageSize=20` | 100 | 100 | 6.29 | 5.95 | 8.35 | 21.17 | 0 |
| `DELETE /api/v1/orders/{id}` | 100 | 100 | 19.07 | 18.89 | 23.09 | 24.82 | 0 |
| `GET /api/v2/orders/{id}` | 100 | 100 | 7.77 | 7.56 | 11.01 | 12.10 | 0 |

## Review SQL log

Log console EF Core ở mức Information được đối chiếu bằng cách gọi riêng từng endpoint một lần và xem SQL `Executed DbCommand` tương ứng. Các truy vấn nền của OutboxProcessor (`SELECT ... FROM outbox_messages`) không được tính là truy vấn của endpoint. “DbCommand” là số lệnh được EF Core log; “statement” là từng câu SQL bên trong command batch, nên hai số này có thể khác nhau.

| Endpoint | Số lệnh/câu SQL cho 1 request | SQL rút gọn quan sát được | Kết luận và bằng chứng |
|---|---|---|---|
| `POST /api/v1/orders` | 1 EF `DbCommand` batch, chứa 3 câu `INSERT` | `INSERT INTO orders ...`; `INSERT INTO outbox_messages ...`; `INSERT INTO order_items ...` | **FAIL** theo tiêu chí Day 51 yêu cầu hai câu INSERT (orders và outbox): log thực tế có thêm INSERT cho item. Cả ba câu xuất hiện trong cùng batch SaveChanges; log này không phát dòng Begin/Commit riêng để xác nhận transaction độc lập. |
| `GET /api/v1/orders/{id}` | 1 `SELECT` | `SELECT ... FROM orders AS o LEFT JOIN order_items AS o0 ... ORDER BY o.id` | **FAIL** theo yêu cầu đọc projection theo id: có đúng một query và không có N+1, nhưng SQL không có `WHERE id = ...`; code hiện tại tải toàn bộ Orders/items rồi mới tìm ID trong bộ nhớ. |
| `GET /api/v1/orders?customerId=...&pageSize=20` | 1 `SELECT` | `SELECT ... FROM orders AS o LEFT JOIN order_items AS o0 ... ORDER BY o.id` | **FAIL** theo yêu cầu truy vấn list theo customer: số query là một và không có N+1, nhưng SQL không có điều kiện customer/cursor hoặc giới hạn trang; lọc và phân trang diễn ra trong bộ nhớ sau khi tải toàn bộ dữ liệu. |
| `DELETE /api/v1/orders/{id}` | 2 EF `DbCommand`; tổng cộng 3 câu SQL | `SELECT ... FROM orders ... LEFT JOIN order_items ...`; batch `UPDATE orders SET status ...` + `INSERT INTO outbox_messages ...` | **FAIL** theo shape mong đợi `SELECT` đơn cần hủy + `UPDATE` trạng thái: SELECT thực tế tải toàn bộ Orders/items, và batch ghi còn có INSERT Outbox. Log xác nhận hai DbCommand, không phải hai SQL statement. |
| `GET /api/v2/orders/{id}` | 1 `SELECT` | `SELECT ... FROM orders AS o LEFT JOIN order_items AS o0 ... ORDER BY o.id` | **FAIL** theo yêu cầu projection theo id: cùng query shape như GET v1, không có `WHERE id = ...`; một query nhưng tải toàn bộ Orders/items trước khi lọc trong bộ nhớ. |

Không sửa các vấn đề SQL trong Day 51; việc tối ưu query thuộc phạm vi Day 60. Các kết luận trên phản ánh log thật, không thay thế cho việc kiểm tra lại sau khi working tree được làm sạch.

Baseline này là mốc để so sánh sau tuning ở Day 59 và khi load test ở Day 66. Không dùng các số này để kết luận API nhanh/chậm một cách tuyệt đối.
