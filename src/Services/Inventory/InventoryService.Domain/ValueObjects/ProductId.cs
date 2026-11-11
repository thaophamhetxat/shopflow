// File này chứa Value Object định danh cho Product trong Domain của InventoryService.
// Tạo ở Day 48 để các aggregate và repository không truyền Guid trần thay cho ProductId.
// Gateway Day 49 và Saga Day 52 sẽ dùng ID này khi Reserve, Release hoặc tra cứu sản phẩm.

namespace InventoryService.Domain.ValueObjects;

/// <summary>
/// Mã định danh Product được bọc trong Guid để phân biệt với các loại ID khác trong hệ thống.
/// </summary>
public sealed record ProductId(Guid Value)
{
    /// <summary>
    /// Tạo ProductId mới cho sản phẩm vừa được khởi tạo.
    /// </summary>
    /// <returns>ProductId chứa một Guid mới.</returns>
    public static ProductId New()
    {
        return new ProductId(Guid.NewGuid());
    }

    /// <summary>
    /// Bọc một Guid có sẵn; chấp nhận Guid.Empty để API có thể trả về kết quả không tìm thấy thay vì lỗi chuyển đổi.
    /// </summary>
    /// <param name="value">Giá trị Guid cần bọc.</param>
    /// <returns>ProductId chứa Guid được cung cấp.</returns>
    public static ProductId From(Guid value)
    {
        return new ProductId(value);
    }
}
