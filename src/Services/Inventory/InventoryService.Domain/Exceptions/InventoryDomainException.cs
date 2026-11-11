// File này chứa ngoại lệ cho các quy tắc nghiệp vụ của InventoryService.
// Tạo ở Day 48 để Reserve/Release báo lỗi rõ ràng và API có thể ánh xạ lỗi sang Problem Details.
// Gateway Day 49 và Saga Day 52 sẽ dựa vào loại lỗi này để xử lý xung đột tồn kho.

namespace InventoryService.Domain.Exceptions;

/// <summary>
/// Ngoại lệ báo một thao tác với Product vi phạm bất biến tồn kho hoặc dữ liệu sản phẩm.
/// </summary>
public sealed class InventoryDomainException : Exception
{
    /// <summary>
    /// Tạo ngoại lệ với thông điệp mô tả quy tắc Inventory bị vi phạm.
    /// </summary>
    /// <param name="message">Mô tả lỗi nghiệp vụ bằng tiếng Việt.</param>
    public InventoryDomainException(string message)
        : base(message)
    {
    }
}
