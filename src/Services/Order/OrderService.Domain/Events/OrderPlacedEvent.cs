// File này chứa sự kiện OrderPlacedEvent do Aggregate Order phát ra khi đơn mới được tạo.
// Tạo ở Day 41 để chuẩn hóa luồng domain event cho việc ghi Outbox và theo dõi trạng thái đơn hàng từ bên ngoài.
// Domain này tập trung sự kiện nghiệp vụ, không chứa HTTP hoặc database logic.

using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Events;

/// <summary>
/// Sự kiện báo hiệu đơn hàng vừa được đặt thành công và mang theo toàn bộ dữ liệu cần thiết để tái dựng trạng thái đơn khi replay event stream.
/// Chúng ta cần Items và ShippingAddress ở đây vì event stream là source of truth theo ADR-001: nếu chỉ giữ Total/CustomerId mà không có sản phẩm và địa chỉ, replay sẽ không đủ thông tin để rebuild aggregate Order và sẽ mất dữ liệu thật trên các node khác.
/// Lưu ý tương thích: các row outbox cũ vẫn có thể deserialize thiếu Items/ShippingAddress nhờ `System.Text.Json` sẽ map property thiếu về default; saga vẫn đọc `OrderId`, `CustomerId` và `Total` mà không crash.
/// </summary>
public sealed record OrderPlacedEvent(
    OrderId OrderId,
    CustomerId CustomerId,
    IReadOnlyList<OrderItem> Items,
    Address ShippingAddress,
    Money Total,
    DateTime OccurredOn) : IDomainEvent;
