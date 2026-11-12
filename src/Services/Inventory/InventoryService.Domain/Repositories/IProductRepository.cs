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
    /// Kiểm tra nhanh xem product có tồn tại và số lượng kho hiện tại đã đáp ứng đủ yêu cầu hay chưa.
    /// </summary>
    /// <param name="id">ProductId cần kiểm tra.</param>
    /// <param name="quantity">Số lượng cần có trong kho để coi là đủ.</param>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>True khi product tồn tại và StockQuantity &gt;= quantity; ngược lại false.</returns>
    /// <remarks>
    /// Kiểm tra này dùng ValueTask&lt;bool&gt; vì đây là hot-path ở đường read ví dụ check nhanh trước khi reserve hoặc cache-aside.
    /// Hầu hết trường hợp kiểm tra hoàn thành ngay trên database và không cần allocate Task mới, nên ValueTask giúp giảm overhead
    /// khi request đi qua nhiều lần trên cùng một path hiệu suất cao.
    /// </remarks>
    ValueTask<bool> CheckStockAsync(ProductId id, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Khóa hàng theo row và thực hiện reserve trong một transaction rõ ràng để tránh oversell ở các request đồng thời.
    /// </summary>
    /// <param name="id">ProductId cần reserve.</param>
    /// <param name="quantity">Số lượng cần giữ.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Product sau khi đã trừ kho thành công.</returns>
    /// <remarks>
    /// Method này trả về aggregate Product thay vì DTO Application để không cho Domain phụ thuộc vào layer Application.
    /// Đường reserve thật cần lock row trong database nên logic này tập trung ở Infrastructure; handler chỉ map dữ liệu sau khi reserve.
    /// </remarks>
    Task<Product> ReserveAsync(ProductId id, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm Product theo ID để thao tác ghi; cần tracking vì aggregate sẽ được sửa trong cùng DbContext.
    /// </summary>
    /// <param name="id">ProductId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Product nếu tìm thấy; ngược lại null.</returns>
    Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm Product theo ID ở chế độ đọc thuần, không theo dõi entity để giảm overhead cho query endpoint.
    /// </summary>
    /// <param name="id">ProductId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Product nếu tìm thấy; ngược lại null.</returns>
    Task<Product?> GetByIdReadOnlyAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi các thay đổi đang được repository theo dõi xuống cơ sở dữ liệu.
    /// </summary>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Task đại diện cho thao tác lưu.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
