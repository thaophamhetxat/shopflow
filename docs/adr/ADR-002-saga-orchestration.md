# ADR-002: Chọn Saga Orchestration

- Trạng thái: Accepted
- Ngày: 2026-11-11

## Tình huống

Luồng đặt hàng cần đi qua ba service theo thứ tự: Order tạo đơn, Inventory giữ hàng bằng Reserve, Payment thu tiền bằng Charge, rồi Order xác nhận đơn bằng Confirm. Nếu Reserve thất bại vì hết hàng thì phải hủy đơn; nếu Charge thất bại thì phải trả kho trước rồi hủy đơn.

Mỗi bước và kết quả của nó cần được lưu để có thể tiếp tục hoặc bù trừ sau khi tiến trình khởi động lại. Sau Day 50, ba service vẫn chưa được nối thành luồng này; mỗi service chỉ có endpoint riêng.

## Quyết định

Chọn **Saga Orchestration**. Trong Day 52, `OrderSaga` ở Order Service sẽ điều phối state machine `PendingInventory → PendingPayment → Completed`, gọi Inventory và Payment trực tiếp qua HTTP nội bộ, rồi persist trạng thái từng bước vào bảng `saga_state`.

Chọn gọi trực tiếp các API nội bộ thay vì đi vòng qua Gateway. Gateway Day 49 là điểm vào cho lưu lượng từ client; gọi trực tiếp tránh thêm một hop trung gian giữa các service và giữ luồng Saga trong Order Service. Địa chỉ các service cần được cấu hình khi triển khai, không mã hóa địa chỉ môi trường vào Saga.

Đây là quyết định kiến trúc cho các ngày triển khai sau, không phải mô tả code đã có ở Day 51.

## Các phương án

| Phương án | Ưu điểm | Nhược điểm |
|---|---|---|
| **Orchestration (chọn)** | Một orchestrator giữ luồng tổng thể; dễ xem trạng thái của một đơn, trace/debug và xác định thứ tự bù. | Orchestrator là điểm tập trung; khi nó dừng, Saga tạm dừng đến khi khởi động lại. Thêm service mới thường cần cập nhật orchestrator. |
| **Choreography (không chọn)** | Mỗi service phản ứng với event của mình; không có orchestrator trung tâm và có thể thêm service mới mà ít sửa code hiện hữu. | Luồng bị phân tán qua nhiều event handler, khó nhìn trạng thái toàn cục và trace một đơn; bảo đảm thứ tự compensation khó hơn. |

## Vì sao chọn

1. Luồng hiện tại chỉ có ba bước tuyến tính nên một state machine tập trung dễ theo dõi.
2. Khi bước sau thất bại, compensation cần chạy đúng thứ tự: lỗi thanh toán thì trả kho rồi mới hủy đơn.
3. Cần một nơi duy nhất để xem bước hiện tại của một đơn và tiếp tục sau restart.
4. Với một người đang học Saga lần đầu, Orchestration giúp thấy rõ mỗi bước, lỗi và hành động bù trong cùng một luồng.

## Trade-off

- Orchestrator phải bảo đảm idempotency khi gọi lại một bước sau timeout hoặc restart. Reserve lặp lại không được giữ kho hai lần; xử lý cụ thể ở Day 53.
- Timeout cho từng bước phải được đặt rõ; Day 53 dùng timeout Saga 30 giây.
- Nếu orchestrator dừng giữa chừng, Saga ở trạng thái dở dang cho đến khi tiến trình được khởi động lại. Persist `saga_state` sau mỗi bước ở Day 52 là cơ sở để recover.
- Orchestrator tập trung trách nhiệm điều phối; thêm bước hoặc service thường đòi hỏi sửa state machine này.

## Hệ quả

- Day 52 triển khai `OrderSaga`, bảng `saga_state` và state machine theo quyết định này.
- Day 53 triển khai compensation cùng timeout 30 giây.
- Day 54 bổ sung resilience, gồm retry/circuit breaker cho các lần gọi HTTP của orchestrator.
- Event Sourcing ở Day 55 không thay thế Saga: event store lưu lịch sử sự kiện, còn Saga điều phối một luồng nghiệp vụ phân tán.
