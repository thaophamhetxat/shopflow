// File này chứa sự kiện OrderCancelledEvent do Aggregate Order phát ra khi đơn bị hủy.
// Tạo ở Day 41 để chuẩn hóa sự kiện hủy đơn trước khi dùng Outbox hoặc Saga để xử lý tác vụ phụ như hoàn tiền và giải phóng cong suất kho.
// Domain này chỉ ghi rõ nghiệp vụ đã xảy ra, không quyết định transport hay lưu trữ.

using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Events;

/// <summary>
/// Sự kiện báo hiệu đơn hàng đã bị hủy và cần xử lý các tác vụ phụ liên quan như giải phóng kho hoặc hoàn tiền.
/// Event này cho phép các service khác biết trạng thái hủy của đơn mà không cần đọc trực tiếp aggregate.
/// </summary>
public sealed record OrderCancelledEvent(OrderId OrderId, CustomerId CustomerId, Money Total, DateTime OccurredOn) : IDomainEvent;
