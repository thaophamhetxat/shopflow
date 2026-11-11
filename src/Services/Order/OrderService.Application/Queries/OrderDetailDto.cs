// File này chứa DTO đọc chi tiết cho một order của OrderService.Application.
// Tạo ở Day 46 để các query đọc đơn hàng bằng projection SQL, không tải cả aggregate về rồi map tay.
// Day 47 sẽ dùng DTO này cho API xem chi tiết đơn, trong khi Day 61 mới có cơ chế cursor phức tạp hơn ở phần pagination cuối cùng.

namespace OrderService.Application.Queries;

/// <summary>
/// DTO đọc cho chi tiết một đơn hàng, chứa thông tin đơn hàng và toàn bộ item của đơn, nhưng vẫn ở dạng read model tách biệt khỏi aggregate Domain.
/// DTO này dựa trên query projection để tránh nạp cả Order aggregate và Domain entity khi đọc chi tiết đơn hàng theo Id.
/// </summary>
public sealed record OrderDetailDto(
    Guid OrderId,
    Guid CustomerId,
    string Status,
    decimal TotalAmount,
    string TotalCurrency,
    DateTime CreatedAt,
    IReadOnlyList<OrderDetailItemDto> Items);

/// <summary>
/// DTO đọc cho một dòng hàng trong order detail, chứa thông tin sản phẩm và giá trị cần thiết để hiển thị trong màn hình chi tiết.
/// Mỗi item được trả về dưới dạng read model và không chứa logic nghiệp vụ Domain nào.
/// </summary>
public sealed record OrderDetailItemDto(
    Guid ProductId,
    int Quantity,
    decimal UnitPriceAmount,
    string UnitPriceCurrency,
    decimal LineTotalAmount);
