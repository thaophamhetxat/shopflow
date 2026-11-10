# ADR-001: Chọn Event Sourcing cho Order

- Trạng thái: Đã chốt
- Ngày: 2026-11-11

## Tình huống

Order là aggregate nghiệp vụ cốt lõi của ShopFlow và có vòng đời rõ ràng: Place -> Pending, Confirm, Cancel, Fulfill (nếu mở rộng trong tương lai). Đã có các domain event như `OrderPlaced`, `OrderConfirmed`, `OrderCancelled` và pattern Outbox được dùng để ghi sự kiện ra ngoài hệ thống. Vì Order là trung tâm của luồng đặt hàng, cần khả năng theo dõi lịch sử thay đổi, tái dựng trạng thái và mở rộng quy trình nghiệp vụ mà không phụ thuộc hoàn toàn vào một snapshot hiện tại.

## Quyết định

Chúng ta quyết định chọn Event Sourcing cho Order như một mô hình quyết định trong tương lai của hệ thống: trạng thái hiện tại của Order sẽ được suy ra từ chuỗi sự kiện bất biến (`OrderPlaced`, `OrderConfirmed`, `OrderCancelled`), thay vì chỉ lưu trạng thái cuối cùng trong database. Mỗi sự kiện đại diện cho một thay đổi nghiệp vụ đã xảy ra, được theo dõi theo thời gian và có thể replay lại để tái tạo aggregate ở bất kỳ thời điểm nào.

## Vì sao chọn

1. Tính toàn vẹn lịch sử nghiệp vụ: Order là tài liệu kinh doanh quan trọng; Event Sourcing giúp ghi lại đúng trình tự thay đổi và hỗ trợ audit.
2. Khả năng phục hồi và tái dựng: khi cần rebuild aggregate hoặc debug sản phẩm, ta có thể replay sự kiện thay vì chỉ dựa vào snapshot hiện thời.
3. Dễ mở rộng các flow tương lai: khi thêm Fulfill, compensation, billing, inventory hoặc notification, event sẽ là nguyên liệu chuẩn để các bounded context khác xử lý mà không cần sửa thêm logic vào model hiện tại.
4. Đồng nhất với Outbox và domain event hiện có: hệ thống đã có khái niệm phát event từ aggregate và ghi event bằng Outbox; Event Sourcing là bước tiếp theo logic và chuẩn hóa nhất cho việc lưu sự kiện như nguồn chân lý.

## Trade-off

- Ưu điểm:
  - Cho phép audit, replay, debug và phân tích nghiệp vụ chính xác.
  - Tách biệt source of truth khỏi read model / projection.
  - Dễ phát triển các workflow theo hướng event-driven.
- Nhược điểm:
  - Cần quản lý lifecycle của event và snapshot rõ ràng hơn so với lưu trạng thái cuối cùng đơn giản.
  - Độ phức tạp tăng khi cần xử lý versioning event và migration schema.
  - Replay và rebuild state có thể tiêu tốn tài nguyên nếu sự kiện quá nhiều.

## Các phương án bị loại bỏ

### 1. Chỉ lưu trạng thái hiện tại của Order

Đây là phương án đơn giản nhất nhưng không đủ để đáp ứng yêu cầu audit, replay và khôi phục lịch sử nghiệp vụ. Nếu dữ liệu chỉ là snapshot cuối cùng, chúng ta không thể biết đúng thứ tự các sự kiện đã xảy ra và không thể tái tạo quá trình Order đã sống như thế nào.

### 2. Chỉ dùng Outbox như source of truth

Outbox rất cần thiết để đảm bảo sự kiện được gửi và lưu an toàn, nhưng nó không thay thế cho Event Sourcing. Outbox là cơ chế giao tiếp và xử lý bất đồng bộ; nó không phải là nguồn dữ liệu nghiệp vụ chính để tái dựng aggregate.

### 3. Không lưu event gì cả

Phương án này tối giản nhất nhưng làm mất trace nghiệp vụ, gây khó khăn cho debug, kiểm toán và tích hợp các bên liên quan. Nó không phù hợp với yêu cầu của nền tảng Order trong ShopFlow.

## Hệ quả

- Event Sourcing sẽ là quyết định kiến trúc cho Day 55, khi mã nguồn mới về `order_events`, `Order.Apply` và snapshot bắt đầu được triển khai.
- Trong Day 44, chúng ta không thêm code production; chỉ ghi lại quyết định để tránh drift giữa design và implementation thực tế.
- Khi implement sau này, cần giữ nguyên dòng sự kiện và nhắm đến mục tiêu: event là nguồn dữ liệu nhất quán, snapshot chỉ là tối ưu hóa hiệu năng, không phải source of truth.
