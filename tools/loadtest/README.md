# Day 66 — Load Testing & Performance Tuning

## Mục tiêu

File k6 này mô phỏng tải đồng thời 50 VU theo mix 70/20/10 cho Order API: `place_order` 35 VU, `get_order` 10 VU, `list_orders` 5 VU. Mỗi lần chạy dùng `BASE_URL` để trỏ tới API thật, mặc định là `http://localhost:5181` để phù hợp với benchmark Day 51/59/60.

## Cài đặt k6

### Windows

```powershell
winget install --id GrafanaLabs.k6 --accept-source-agreements --accept-package-agreements --silent
```

Sau khi cài xong, xác nhận:

```powershell
k6 version
```

### Chạy load test chính (5 phút)

```powershell
$env:BASE_URL = "http://localhost:5181"
k6 run .\tools\loadtest\shopflow-load-test.js
```

Hoặc không cần biến môi trường, script sẽ mặc định dùng `http://localhost:5181`.

### Chạy load test cho Gateway (1 phút)

```powershell
$env:BASE_URL = "http://localhost:5000"
$env:TEST_DURATION = "1m"
k6 run .\tools\loadtest\shopflow-load-test.js
```

## Cách đọc output

Kết quả k6 in ra summary gồm các cột quan trọng:

- `http_req_duration`: đo latency theo avg / p(50) / p(99) / max.
- `http_reqs`: tổng số request và throughput ước tính qua `count`/thời gian chạy.
- `http_req_failed`: tỷ lệ lỗi HTTP, cần dưới `5%` theo day 66.
- `checks`: số request đạt điều kiện `status === 201` hoặc `status === 200`.

Đối với workload hỗn hợp, throughput tổng cần > 50 req/s; p99 mỗi endpoint phải được ghi trong doc trước/sau tuning để so sánh rõ ràng.

## Grafana trong lúc chạy

- Mở `http://localhost:3000`.
- Đăng nhập `admin / admin`.
- Vào dashboard `ShopFlow` để quan sát `http_requests_total`, `http_request_duration_seconds`, `shopflow_orders_created_total`, `shopflow_saga_runs_total`.
- Nếu endpoint đang choke, hãy nhìn `rate(http_requests_total[1m])` và `histogram_quantile(0.99, rate(http_request_duration_seconds_bucket[5m]))` trên Prometheus tại `http://localhost:9090`.

## Lưu ý nghiệp vụ

- `MockPaymentGateway` fail ngẫu nhiên ~20% là hành vi nghiệp vụ, không phải lỗi load test. Day 66 chỉ tính error rate HTTP non-2xx, không phân loại `Cancelled` là lỗi.
- `stock` của sản phẩm `a73b5e21-4c0f-4f91-8a61-2e5c9501d101` phải được bump lên 1.000.000 trước khi chạy load test; nếu stock đã cạn thì không chạy tiếp mà phải bump lại bằng SQL trước khi đo.
- Không đổi logic nghiệp vụ hoặc schema; chỉ tune config/DI/code tối thiểu khi có bằng chứng rõ ràng từ metric và full suite pass.

## Kết quả lưu

Các file output thực thi được lưu ở:

- `tools/loadtest/results/day66-baseline.txt`
- `tools/loadtest/results/day66-after-tuning.txt`
- `tools/loadtest/results/day66-gateway.txt`

Không commit file nhị phân, chỉ lưu text output của k6.

## Checklist xác minh Day 66

- [x] Chạy riêng `ChannelFull_LogsWarning_WhenAtCapacity`: 1/1 passed (khoảng 53 giây).
- [x] Baseline Order API, 50 VU trong 5 phút: 0% HTTP failures, 372.488 req/s, p99 466.59 ms, max 4.48 s.
- [x] Thử đúng một tuning hypothesis (Npgsql minimum pool size 10, maximum 100), chạy lại 5 phút và hoàn nguyên vì p99/max xấu hơn: 0% HTTP failures, 342.772 req/s, p99 809.62 ms, max 8.18 s.
- [x] Gateway path, 50 VU trong 1 phút: 0% HTTP failures, 392.915 req/s, p99 417.27 ms, max 1.44 s; 23,798/23,798 checks passed.
- [x] Chạy full solution suite: 139/143 passed. Bốn timeout dưới tải full suite là ba saga transition tests (`PlaceOrder_EndToEnd_SagaConfirmsOrderAndPersistsCompletedState`, `PlaceOrder_PaymentFails_InventoryReleased`, `PlaceOrder_ReserveFails_OrderCancelled`) và `ChannelFull_LogsWarning_WhenAtCapacity`.
- [x] Chạy riêng ba saga transition tests sau đó: 3/3 passed trong 41 giây. `ChannelFull_LogsWarning_WhenAtCapacity` cũng đã pass riêng.

`ChannelFull_LogsWarning_WhenAtCapacity` là test flaky đã biết từ Day 59: nếu fail trong full suite nhưng pass riêng thì ghi nhận là timeout dưới tải, không phải regression đã xác nhận; nếu fail cả khi chạy riêng thì dừng và điều tra. Ba saga transition tests cũng pass riêng sau khi timeout trong full-suite run này, nhưng cần theo dõi nếu tiếp tục tái diễn. Full suite lần này không hoàn toàn xanh; không diễn giải kết quả thành tất cả tests đều pass.
