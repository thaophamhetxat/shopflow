// File này chứa DTO dùng để trả về thông tin đơn hàng từ Application layer.
// Tạo ở Day 41 để chuẩn hóa response của query đọc Order trước khi Day 42 thêm persistence.
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
    decimal TotalAmount,
    string TotalCurrency,
    DateTime CreatedAt,
    IReadOnlyCollection<OrderItemDto> Items);

/// <summary>
/// DTO đại diện cho một dòng hàng trong đơn, gồm thông tin sản phẩm, đơn giá và tổng tiền của dòng.
/// </summary>
public sealed record OrderItemDto(Guid ProductId, int Quantity, decimal UnitPriceAmount, string UnitPriceCurrency, decimal LineTotalAmount);
