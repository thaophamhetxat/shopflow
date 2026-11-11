// File này định nghĩa hợp đồng lưu trữ Product thuộc Domain của InventoryService.
// Tạo ở Day 48 để Application thao tác với aggregate mà không phụ thuộc EF Core.
// Gateway Day 49 và Saga Day 52 sẽ dùng các hành vi này khi giữ, trả hoặc đọc tồn kho.

using InventoryService.Domain.Aggregates;
using InventoryService.Domain.ValueObjects;

namespace InventoryService.Domain.Repositories;

/// <summary>
/// Hợp đồng thêm Product, đọc theo ID và lưu thay đổi của aggregate.
/// </summary>
public interface IProductRepository
{
    /// <summary>
    /// Đưa Product mới vào vùng theo dõi của repository để lưu ở lần SaveChangesAsync tiếp theo.
    /// </summary>
    /// <param name="product">Aggregate Product cần thêm.</param>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Task đại diện cho thao tác thêm.</returns>
    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm Product theo ID, trả null nếu không tồn tại.
    /// </summary>
    /// <param name="id">ProductId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Product nếu tìm thấy; ngược lại null.</returns>
    Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi các thay đổi đang được repository theo dõi xuống cơ sở dữ liệu.
    /// </summary>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Task đại diện cho thao tác lưu.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
