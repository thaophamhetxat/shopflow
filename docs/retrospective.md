# Retrospective – Day 38 đến Day 67

## 1. Bài học quan trọng

### 1) Vấn đề stale DI có thể gây hiểu nhầm lớn nếu không kiểm tra runtime
Trong giai đoạn trước, `OrderService.Application` còn giữ các registration in-memory cho repository và idempotency store. Ban đầu, điều này trông giống như một lỗi production rõ ràng. Nhưng sau khi xác minh kỹ, thực tế runtime lại được override bởi `OrderService.Infrastructure` với implementation EF Core + Redis. Bài học là: không nên suy đoán từ một file đơn lẻ khi mà kết cấu composition root đang tách rõ ràng giữa application và infrastructure.

### 2) Cold start cần được validate bằng health + smoke flow, không chỉ build
Một repo có thể build tốt nhưng vẫn fail khi start từ đầu. Day 67 nhấn mạnh rằng hệ thống cần phải khởi động trên Docker infra thật, chạy health checks, rồi kiểm tra một order đi qua gateway. Chỉ khi có kết quả thực tế từ `/health` và smoke to `Confirmed` mới đáng tin cậy.

### 3) Placeholder không phải là “dữ liệu không ảnh hưởng” nếu để lâu
Những `Placeholder.cs` không chứa logic giao dịch, nhưng nếu tồn tại quá lâu sẽ tạo nhầm lẫn về architecture, độ hoàn thiện và tính sẵn sàng của repo. Việc dọn sạch chúng giúp giảm rủi ro trong final review và làm rõ phần nào đã được triển khai, phần nào đang là scaffolding.

### 4) Test suite cần được hiểu đúng theo ngữ cảnh
Đã có tập test performance-sensitive từng flaky khi chạy cùng lúc trong full-suite. Nếu chỉ nhìn vào dạng “full suite failed” mà không tách riêng từng case, ta dễ nhầm với regression thật. Bài học là phân loại fail theo ngữ cảnh và xác nhận từng case riêng rẽ thay vì bỏ qua.

### 5) Tài liệu là phần bắt buộc để chốt trạng thái hiện tại
Repo từng bị lạc giữa các mô tả day-by-day cũ, mệnh đề “sẽ có” và thông tin không còn đúng với code. Day 67 cho thấy việc viết README + Definition of Done không chỉ là công việc phụ; đó là phần làm rõ thực tế và tránh báo cáo sai về project state.

## 2. Cải thiện trong tương lai

### Cải thiện 1: thắt chặt contract và versioning cho môi trường local
Cần chuẩn hóa rõ ràng giữa:
- `docker-compose.yml` password container
- connection string runtime local
- appsettings development

Việc tách biệt rõ ràng giúp không bị bội nhầm giữa môi trường phát triển và môi trường container.

### Cải thiện 2: khóa version / dependency policy cho CI
Có warning `NU1603` và `NU1902` cho gói external là tín hiệu đáng xử lý. Tương lai nên có chính sách lock version hoặc update pipeline để tránh dependency drift khiến build/test khác nhau giữa máy cá nhân và CI.

### Cải thiện 3: tăng độ rõ ràng của “reproducible validation”
Mỗi feature nên đi kèm với một script validation tối thiểu: build, health, smoke, và metrics. Điều này giúp cộng tác viên không cần tự suy diễn từ log mà có thể lặp lại kết quả với một lệnh chuẩn.

## 3. Kết luận

Quá trình từ Day 38 đến Day 67 đã chứng minh ShopFlow không còn là một repo chỉ có khung sườn; nó đã đạt đến mức có thể cold start, health check, smoke order, event bus traffic và kết quả test rõ ràng. Kết quả lớn nhất không nằm ở “code nhiều hơn”, mà ở việc làm rõ đâu là phần đã ổn định và đâu là phần vẫn cần ràng buộc, kiểm soát hoặc cập nhật thêm.
