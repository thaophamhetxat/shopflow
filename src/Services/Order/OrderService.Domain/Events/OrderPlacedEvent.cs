// File này chứa sự kiện OrderPlacedEvent do Aggregate Order phát ra khi đơn mới được tạo.
// Tạo ở Day 41 để chuẩn hóa luồng domain event cho việc ghi Outbox và theo dõi trạng thái đơn hàng từ bên ngoài.
// Domain này tập trung sự kiện nghiệp vụ, không chứa HTTP hoặc database logic.

using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Events;

/// <summary>
/// Sự kiện báo hiệu đơn hàng vừa được đặt thành công và sẵn sàng cho các bước tiếp theo như kiểm kho, thanh toán hoặc gửi thông báo.
/// Event này là thông tin cho Outbox Day 43, Saga Day 52 và các consumer khác biết rằng một đơn mới đã xuất hiện trong hệ thống.
/// </summary>
public sealed record OrderPlacedEvent(OrderId OrderId, CustomerId CustomerId, Money Total, DateTime OccurredOn) : IDomainEvent;
