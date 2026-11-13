# Definition of Done – Day 67

## 1. Mục tiêu và phạm vi

Day 67 tập trung vào 3 lớp xác nhận:

1. Chứng minh hệ thống có thể cold start từ trạng thái rỗng và chạy đúng luồng nghiệp vụ chủ đạo.
2. Dọn sạch các phần placeholder / stale configuration đã còn sót lại trong repo.
3. Ghi lại trạng thái thực tế của repo bằng văn bản rõ ràng, không phóng đại hay gán "đã hoàn tất" cho những việc chưa thực sự đạt được.

## 2. Checklist xác thực thực tế

### 2.1. Repo và trạng thái Git
- [x] Repo đang ở branch `main`.
- [x] `git status` sạch trước khi bắt đầu dọn dẹp placeholder, ngoại trừ các file cần xóa theo mục tiêu.
- [x] HEAD khớp với snapshot cần validate: `6862814`.
- [x] Remote `origin` trỏ đến GitHub repo `https://github.com/thaophamhetxat/shopflow.git`.

### 2.2. Hạ tầng cốt lõi
- [x] Docker Compose có PostgreSQL 16, Redis 7, RabbitMQ 3 Management, Jaeger, Prometheus, Grafana.
- [x] `docker compose up -d` đã được chạy lại để cold start từ nền sạch.
- [x] Các service infra hoạt động và health check đủ điều kiện để app local truy cập.

### 2.3. Khởi động và sẵn sàng của service
- [x] `OrderService.API` chạy trên port `5181`.
- [x] `InventoryService.API` chạy trên port `5140`.
- [x] `PaymentService.API` chạy trên port `5199`.
- [x] `ShopFlow.Gateway` chạy trên port `5000`.
- [x] `/health` và `/health/ready` của từng service trả `200 OK`.
- [x] Gateway `/health` và `/health/ready` cũng trả thành công khi downstream còn sẵn sàng.

### 2.4. Smoke test nghiệp vụ
- [x] Đã thực hiện smoke test order thông qua gateway.
- [x] Order đi qua pipeline và đạt trạng thái `Confirmed` trên lần thử hợp lệ đầu tiên.
- [x] RabbitMQ có message xuất hiện trong queue `shopflow.order-events`.

### 2.5. Kiểm thử tự động
- [x] `dotnet test ShopFlow.sln --logger "console;verbosity=minimal"` đã chạy.
- [x] Kết quả giải thích rõ ràng: không xuất hiện regression mới ngoài tập flaky đã biết.
- [x] Các test flaky trong full-suite đã được kiểm độc lập và pass khi chạy riêng rẽ.

### 2.6. Cleanup và repo hygiene
- [x] Các placeholder không có chức năng đã bị xóa: 6 file `Placeholder.cs` trong các module đã xác nhận là shell class giả.
- [x] Đã kiểm tra lại DI: phần in-memory repository trong `OrderService.Application` là stale, nhưng không phải lỗi runtime vì thực tế production registration ở infrastructure layer override lại.
- [x] Không còn các dấu `TODO/FIXME/XXX/HACK` trong `src/` và `tests/`.

### 2.7. Documentation
- [x] README chính đã được cập nhật để phản ánh trạng thái thực tế của repo.
- [x] `docs/definition-of-done.md` đã được tạo.
- [x] `docs/retrospective.md` đã được tạo.

## 3. Trạng thái thực tế – phần cần ghi nhận rõ

### Đã đạt
- Hệ thống local đã cold-start thành công.
- Health endpoints và gateway đầy đủ để chặn traffic không sẵn sàng.
- Luồng order thực tế hoạt động qua gateway và RabbitMQ.
- Placeholder đã được dọn sạch.
- Test suite đã xác nhận toàn bộ solution ổn định theo đúng điều kiện allowed của task.

### Cảnh báo / ràng buộc
- `docker-compose.yml` dùng PostgreSQL password `ShopFlow123!` trong container; runtime appsettings local dùng `shopflow123` cho connection string. Đây là hai giá trị khác nhau và phải được tách biệt rõ ràng.
- Cả solution đều có warning NuGet về package version không khớp với version minimum yêu cầu (`NU1603`) và một advisory `NU1902` cho OpenTelemetry exporter. Đây không phải lỗi build, nhưng là tín hiệu cần xử lý trong giai đoạn maintenance tiếp theo.
- Có một tập test performance-sensitive đã từng flaky trong full-suite, nhưng khi chạy riêng rẽ lại pass, nên tiêu chuẩn Day 67 vẫn chấp nhận tình hình này khi không có regression mới.

### Chưa đạt / cần thực hiện ngoài scope local
- Push lên GitHub `origin/main` cần có quyền/credential phù hợp; nếu không được cấp trong môi trường hiện tại, bước này phải được làm trên máy có quyền ghi chuẩn.
- NTFY push notification chỉ có thể được gửi nếu có URL/topic hợp lệ. Không có cấu hình này trong repo nên bước này không thể thực hiện bằng dữ liệu hiện có.

## 4. Kết luận

Repo ở trạng thái Day 67 đã đủ điều kiện để khẳng định: hệ thống xây dựng trong local environment đã đáp ứng smoke test, infrastructure health và pipeline nghiệp vụ chính. Tài liệu hiện tại phản ánh đúng thực tế thay vì các mô tả quá khứ hoặc chưa từng triển khai.
