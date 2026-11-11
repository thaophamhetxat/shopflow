// File này chứa ngoại lệ Application khi Product được yêu cầu không tồn tại.
// Tạo ở Day 48 để query/command phân biệt rõ trường hợp thiếu dữ liệu với lỗi hệ thống.
// API Day 48 ánh xạ ngoại lệ này thành phản hồi 404 Problem Details.

namespace InventoryService.Application;

/// <summary>
/// Ngoại lệ cho biết ProductId được yêu cầu không có trong repository.
/// </summary>
public sealed class ProductNotFoundException : Exception
{
    /// <summary>
    /// Tạo ngoại lệ với ID sản phẩm không tìm thấy.
    /// </summary>
    /// <param name="productId">ID sản phẩm được yêu cầu.</param>
    public ProductNotFoundException(Guid productId)
        : base($"Không tìm thấy sản phẩm với ProductId '{productId}'.")
    {
        ProductId = productId;
    }

    /// <summary>Mã sản phẩm không được tìm thấy.</summary>
    public Guid ProductId { get; }
}
