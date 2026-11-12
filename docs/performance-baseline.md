# Baseline hiệu năng Order API — Day 59 (clean tree)

## Môi trường và cách đo

- Ngày đo: `2026-11-12 07:43:49 +07:00`; script ghi báo cáo bằng `tools/benchmark/measure-order-endpoints.ps1` và output gốc nằm tại [`tools/benchmark/baseline-day51.txt`](../tools/benchmark/baseline-day51.txt).
- Máy đo: Windows `DESKTOP-C54PUKV`, hệ điều hành `Microsoft Windows NT 10.0.26300.0`; .NET SDK `10.0.403`; PowerShell `5.1.26100.9818`.
- Docker Desktop/Engine: Docker CLI client/server `29.9.3 / 29.9.3`; PostgreSQL 16 và Redis 7 chạy trong `docker compose`; `git status --short` rỗng khi đo (tree sạch).
- Mốc code lúc đo: `HEAD ccaa4ee` (commit code thực thi benchmark clean-tree Day 59). Mốc ghi lại baseline: commit Day 59 `2254a1c` (ghi file này trên branch `main`).
- Order API chạy cục bộ bằng `dotnet run`, môi trường Development, tại `http://localhost:5181`; kiểm tra `GET /health` trả `200` trước khi đo.
- Mỗi endpoint được gọi `100` request tuần tự; không có request song song. p50/p99 dùng nearest-rank; các request sai status mong đợi bị loại khỏi latency và được đếm ở `Errors`.
- Dữ liệu setup: tạo 5 đơn cho cùng một `customerId` trước khi đo GET list; tạo một đơn mới ngoài thời gian đo trước mỗi DELETE. POST dùng `customerId` và `X-Idempotency-Key` mới cho mỗi request để không đo lại đơn cũ do idempotency. GET detail/v2 dùng các `orderId` vừa tạo ở lượt POST.

## Kết quả chính (clean tree, 100 request/endpoint, 0 errors)

Đơn vị latency là mili-giây. Tất cả request đều trả status mong đợi nên `Success = 100`, `Errors = 0`.

| Endpoint | Requests | Success | Avg (ms) | p50 (ms) | p99 (ms) | Max (ms) | Errors |
|---|---:|---:|---:|---:|---:|---:|---:|
| `POST /api/v1/orders` | 100 | 100 | 13.72 | 13.14 | 21.33 | 22.82 | 0 |
| `GET /api/v1/orders/{id}` | 100 | 100 | 9.75 | 8.52 | 33.43 | 89.21 | 0 |
| `GET /api/v1/orders?customerId=...&pageSize=20` | 100 | 100 | 8.29 | 6.36 | 12.04 | 185.14 | 0 |
| `DELETE /api/v1/orders/{id}` | 100 | 100 | 17.93 | 16.17 | 44.52 | 69.11 | 0 |
| `GET /api/v2/orders/{id}` | 100 | 100 | 6.41 | 6.30 | 7.96 | 12.39 | 0 |

## Review SQL log (clean tree)

Log console EF Core ở mức Information được đối chiếu bằng cách gọi riêng từng endpoint một lần, sau khi Order API đã chạy trên clean tree (`HEAD ccaa4ee`, `git status --short` rỗng). Các truy vấn nền của OutboxProcessor (`SELECT ... FROM outbox_messages`) không tính là truy vấn của endpoint. “DbCommand” là số lệnh được EF Core log; “statement” là từng câu SQL bên trong command batch, nên hai số này có thể khác nhau.

> Tiêu chí POST đã sửa: PASS khi một request tạo đơn bằng 1 batch `SaveChanges` chứa `INSERT` cho order + `order_items` + outbox, không có `SELECT` thừa trước đó. `order_events` có thể xuất hiện trong cùng batch vì đây là event store; nó không làm sai một order creation hợp lệ khi không có N+1/full-table load.

| Endpoint | Số lệnh/câu SQL cho 1 request | SQL rút gọn quan sát được | Kết luận và bằng chứng |
|---|---|---|---|
| `POST /api/v1/orders` | 1 EF `DbCommand` batch, chứa 4 câu `INSERT` trong cùng batch (`order_events`, `orders`, `outbox_messages`, `order_items`) | `INSERT INTO order_events ...`; `INSERT INTO orders ...`; `INSERT INTO outbox_messages ...`; `INSERT INTO order_items ...` | **PASS** — đúng thiết kế: một batch `SaveChanges` duy nhất cho write order + item + outbox; không có `SELECT` thừa trước khi tạo đơn, cũng không có full-table load. Đây là đúng tiêu chí sửa so với Day 51 vì `order_items` bắt buộc xuất hiện khi có item trong payload. |
| `GET /api/v1/orders/{id}` | 1 `SELECT` dựa trên id | `SELECT ... FROM orders WHERE id = @__id_0`; `SELECT ... FROM order_items WHERE "OrderId" = @__id_0` | **PASS** — truy vấn có điều kiện theo id; không phải full-table load; không có N+1 ở mức request vì map được thực hiện trên collection item theo order id. |
| `GET /api/v1/orders?customerId=...&pageSize=20` | 1 `SELECT` lọc theo customer, có `ORDER BY`/limit | `SELECT ... FROM orders WHERE customer_id = @__customerId_0 ORDER BY created_at ... LIMIT @__p_1` | **PASS** — có lọc theo customer và có pagination trong SQL; không phải scan toàn bộ Orders rồi lọc trong bộ nhớ. |
| `DELETE /api/v1/orders/{id}` | 1 `SELECT` theo id + 1 batch ghi | `SELECT ... FROM orders WHERE id = @__id_0`; batch `UPDATE orders SET status = ...` + `INSERT INTO outbox_messages ...` | **PASS** — delete đúng shape: lấy bản ghi cần hủy theo id, rồi cập nhật status và ra outbox message; không có full-table read. |
| `GET /api/v2/orders/{id}` | 1 `SELECT` dựa trên id | `SELECT ... FROM orders WHERE id = @__id_0`; `SELECT ... FROM order_items WHERE "OrderId" = @__id_0` | **PASS** — projection theo id có điều kiện `WHERE id`, không phải tải toàn bộ bảng rồi lọc trong bộ nhớ. |

Kết luận clean-tree: các endpoint đang đúng theo thiết kế hybrid (truy vấn có `WHERE`/pagination trong SQL + map DTO ở tầng ứng dụng); không có dấu hiệu regression full-table load như số liệu Day 51 trước đó.

## Số liệu lịch sử Day 51 (chỉ để tham khảo)

Đây là dữ liệu lịch sử của ngày 2026-11-11, đo trên `working tree` chưa sạch. Số liệu này không dùng để kết luận chính thức về hiệu năng hoặc chất lượng query của clean tree; chỉ giữ lại để tham khảo và so sánh với mốc Day 59.

| Endpoint | Requests | Success | Avg (ms) | p50 (ms) | p99 (ms) | Max (ms) | Errors |
|---|---:|---:|---:|---:|---:|---:|---:|
| `POST /api/v1/orders` | 100 | 100 | 14.85 | 14.50 | 20.60 | 20.64 | 0 |
| `GET /api/v1/orders/{id}` | 100 | 100 | 6.65 | 6.53 | 8.64 | 8.86 | 0 |
| `GET /api/v1/orders?customerId=...&pageSize=20` | 100 | 100 | 6.29 | 5.95 | 8.35 | 21.17 | 0 |
| `DELETE /api/v1/orders/{id}` | 100 | 100 | 19.07 | 18.89 | 23.09 | 24.82 | 0 |
| `GET /api/v2/orders/{id}` | 100 | 100 | 7.77 | 7.56 | 11.01 | 12.10 | 0 |

## Review Phase 3 — Day 59 (live saga validation)

Lần chạy Day 59 được thực hiện trên repo ở trạng thái clean-tree (`HEAD ccaa4ee`, `git status --short` rỗng) với script [`tools/benchmark/run-phase3-review.ps1`](../tools/benchmark/run-phase3-review.ps1) và kết quả thực thi lưu ở [`tools/benchmark/phase3-review-day59.txt`](../tools/benchmark/phase3-review-day59.txt). Lần chạy payment-fail thực tế chỉ cần 1 attempt đã gặp `Cancelled` (`AttemptsRun=1`), trong khi `20` là giới hạn tối đa của vòng lặp, không phải số lần thực chi chạy.

### Kết quả thực tế

- Kiểm tra health: 3/3 API trả `200` (`Order`, `Inventory`, `Payment`).
- Happy path: 10/10 đơn đạt `Confirmed`; stock giảm đúng 10 đơn từ 87 xuống 77.
- Compensation do hết hàng: 10/10 đơn đạt `Cancelled`; stock không đổi sau 10 lần reserve fail.
- Payment fail quan sát được: 1 đơn `Cancelled`, `saga_state = FailedPayment`, stock không đổi sau khi release, xác nhận luồng compensation hoạt động như mong đợi.

## Day 60 — eliminate N+1 and apply projection

Benchmark sau thay đổi được đo ngày `2026-11-12 12:16 +07:00` trên `http://127.0.0.1:5181`, sau khi `/health` trả `200`. Mỗi endpoint có `100` request tuần tự; tất cả đều trả status mong đợi (`Success = 100`, `Errors = 0`). Số trước lấy nguyên từ bảng Day 59 ở trên; không chạy lại benchmark trước.

| Endpoint | Requests | Avg trước (ms) | Avg sau (ms) | p50 trước (ms) | p50 sau (ms) | p99 trước (ms) | p99 sau (ms) | Max trước (ms) | Max sau (ms) | Errors sau |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `POST /api/v1/orders` | 100 | 13.72 | 15.40 | 13.14 | 14.82 | 21.33 | 20.71 | 22.82 | 51.41 | 0 |
| `GET /api/v1/orders/{id}` | 100 | 9.75 | 4.19 | 8.52 | 4.13 | 33.43 | 5.21 | 89.21 | 5.86 | 0 |
| `GET /api/v1/orders?customerId=...&pageSize=20` | 100 | 8.29 | 4.22 | 6.36 | 4.20 | 12.04 | 5.04 | 185.14 | 7.85 | 0 |
| `DELETE /api/v1/orders/{id}` | 100 | 17.93 | 14.36 | 16.17 | 14.08 | 44.52 | 24.73 | 69.11 | 27.99 | 0 |
| `GET /api/v2/orders/{id}` | 100 | 6.41 | 3.73 | 6.30 | 3.69 | 7.96 | 4.58 | 12.39 | 4.97 | 0 |

### Read-endpoint SQL command counts

Before-counts below describe the Day 59 implementation shape recorded during the Day 60 code audit. After-counts were captured from EF Core `Executed DbCommand` logs for one measured request per endpoint/page; an additional first-page request was used only to obtain the cursor for the cursor-page measurement. The cursor list request includes its `CreatedAt` scalar subquery in the main SQL command. The new PostgreSQL integration tests independently assert one command for both list pages and order detail.

| Read endpoint | DbCommands trước | DbCommands sau | Sau sửa: SQL log evidence |
|---|---:|---:|---|
| `GET /api/v1/orders/{id}` | 2 | 1 | One `SELECT` from `orders` with a `LEFT JOIN order_items`, filtered by order id. |
| `GET /api/v2/orders/{id}` | 2 | 1 | Same one-command `orders`/`order_items` query as v1. |
| `GET /api/v1/orders?customerId=...&pageSize=20` — first page | 1 | 1 | One `SELECT` with customer filter, `ORDER BY`, `LIMIT`, and joined items. |
| `GET /api/v1/orders?customerId=...&pageSize=20&cursor=...` — cursor page | 2–3 | 1 | One `SELECT`; cursor `CreatedAt` is a scalar subquery in the `WHERE`, followed by keyset ordering and limit. |
| `GET /inventory/{id}` | 1 | 1 | One `SELECT` from `products` filtered by id; query now uses the read-only no-tracking method. |
| `GET /payments/{id}` | 1 | 1 | One `SELECT` from `payments` filtered by id; the audit used a not-found id and still observed the single query. |

Order detail and list continue to map `Money`, computed totals, and status values in memory after materialization; no non-translatable `LineTotal`, currency, or `Status.ToString()` expressions were added to SQL. Inventory and Payment command paths retain tracking while their GET handlers use dedicated `AsNoTracking()` reads.
