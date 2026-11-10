// File này chứa sự kiện OrderConfirmedEvent do Aggregate Order phát ra khi đơn được xác nhận.
// Tạo ở Day 41 để chuẩn hóa event nghiệp vụ chính của vòng đời đơn hàng trước khi làm Outbox vào Day 43.
// Domain này giữ event theo ngữ nghĩa nghiệp vụ, không phụ thuộc vào framework hoặc transport.

using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Events;

/// <summary>
/// Sự kiện báo hiệu đơn hàng đã được xác nhận và đang đi vào giai đoạn xử lý tiếp theo như reserve tồn kho và thanh toán.
/// Event này giúp Outbox hoặc Saga biết rằng đơn hàng đã chốt và không còn ở trạng thái Pending.
/// </summary>
public sealed record OrderConfirmedEvent(OrderId OrderId, Money Total, DateTime OccurredOn) : IDomainEvent;
