// File này chứa DTO dùng để trả về thông tin đơn hàng từ Application layer.
// Tạo ở Day 41 để chuẩn hóa response của query và command PlaceOrder, trước khi Day 42 thêm persistence và API.
// Application layer dùng DTO để tránh làm lộ trực tiếp aggregate hay entity sang tầng trên.

namespace OrderService.Application.Queries;

/// <summary>
/// DTO đại diện cho thông tin một đơn hàng sau khi được map từ Aggregate Order.
/// DTO này cho phép API hoặc UI nhận dữ liệu ổn định mà không phụ thuộc trực tiếp vào domain implementation.
/// </summary>
public sealed record OrderDto(
    Guid OrderId,
    Guid CustomerId,
    string Status,
    string Street,
    string City,
    string Country,
    string? PostalCode,
    decimal TotalAmount,
    string Currency,
    IReadOnlyCollection<OrderItemDto> Items);

/// <summary>
/// DTO đại diện cho một dòng hàng trong đơn hàng, chứa thông tin sản phẩm và giá trị tính toán của item.
/// DTO này hữu ích khi trả về chi tiết order cho nơi gọi mà không cần quan sát aggregate internals.
/// </summary>
public sealed record OrderItemDto(Guid ProductId, int Quantity, decimal UnitPrice, string Currency);
