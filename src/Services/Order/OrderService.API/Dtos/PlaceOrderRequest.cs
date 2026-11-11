// File này chứa contract JSON cho POST /api/v1/orders, tách rời khỏi command application và giữ tên field đúng theo schema nghiệp vụ Day 45.
// Tạo ở Day 47 để API v1 nhận body theo dạng tương thích client cũ, trong khi IdempotencyKey vẫn đi qua header riêng để không phá command contract cũ.
// Gateway Day 49 có thể proxy request tới endpoint này mà không cần biết chi tiết của aggregate hoặc handler.
namespace OrderService.API.Dtos;

/// <summary>
/// Contract JSON của request tạo đơn ở API v1, giữ 1–1 với PlaceOrderCommand trong Application nhưng bỏ trường IdempotencyKey ra khỏi body.
/// Trường IdempotencyKey truyền qua header X-Idempotency-Key theo quy ước Day 45 để không làm client phải gửi key lẫn trong payload.
/// </summary>
/// <param name="CustomerId">Mã khách hàng đặt đơn.</param>
/// <param name="Street">Tên đường nhận hàng.</param>
/// <param name="City">Thành phố giao hàng.</param>
/// <param name="Country">Quốc gia giao hàng.</param>
/// <param name="PostalCode">Mã bưu chính tùy chọn.</param>
/// <param name="Items">Danh sách sản phẩm cần đặt mua.</param>
public sealed record PlaceOrderRequest(
    Guid CustomerId,
    string Street,
    string City,
    string Country,
    string? PostalCode,
    List<PlaceOrderRequestItem> Items);

/// <summary>
/// Contract JSON cho một dòng hàng của request đặt đơn, giữ đúng field cần thiết để map ra PlaceOrderItem trong Application.
/// Thông tin giá tiền ở đây sử dụng Amount và Currency riêng, không chứa logic nghiệp vụ dưới domain.
/// </summary>
/// <param name="ProductId">Mã sản phẩm đặt mua.</param>
/// <param name="Quantity">Số lượng đặt mua.</param>
/// <param name="UnitPriceAmount">Đơn giá của sản phẩm theo Amount.</param>
/// <param name="UnitPriceCurrency">Mã tiền tệ của đơn giá.</param>
public sealed record PlaceOrderRequestItem(
    Guid ProductId,
    int Quantity,
    decimal UnitPriceAmount,
    string UnitPriceCurrency);
