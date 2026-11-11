// File này chứa DTO đọc Product của InventoryService Application.
// Tạo ở Day 48 để API trả dữ liệu tồn kho mà không làm lộ aggregate Domain.
// Gateway Day 49 và Saga Day 52 có thể dùng hợp đồng này khi đọc sản phẩm.

namespace InventoryService.Application.Queries;

/// <summary>
/// Dữ liệu sản phẩm được trả về cho API sau khi Reserve, Release hoặc truy vấn.
/// </summary>
/// <param name="ProductId">Mã định danh sản phẩm.</param>
/// <param name="Name">Tên sản phẩm.</param>
/// <param name="PriceAmount">Giá trị giá sản phẩm.</param>
/// <param name="PriceCurrency">Mã tiền tệ của giá.</param>
/// <param name="StockQuantity">Số lượng tồn kho hiện tại.</param>
public sealed record ProductDto(
    Guid ProductId,
    string Name,
    decimal PriceAmount,
    string PriceCurrency,
    int StockQuantity);
