// File này triển khai repository Product bằng EF Core cho InventoryService.
// Tạo ở Day 48 để command/query đọc và lưu aggregate mà không dùng Include vì Product không có collection con.
// Gateway Day 49 và Saga Day 52 sẽ truy cập tồn kho qua hợp đồng repository này.

using InventoryService.Domain.Aggregates;
using InventoryService.Domain.Repositories;
using InventoryService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository PostgreSQL cho Product; không nạp collection phụ vì aggregate không có collection con.
/// </summary>
public sealed class ProductRepository : IProductRepository
{
    private readonly InventoryDbContext _dbContext;

    /// <summary>
    /// Tạo repository với context inventory hiện tại.
    /// </summary>
    /// <param name="dbContext">Context EF Core quản lý Product.</param>
    public ProductRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Đưa Product mới vào ChangeTracker để lưu ở lần SaveChangesAsync tiếp theo.
    /// </summary>
    /// <param name="product">Aggregate Product cần thêm.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Task đại diện cho thao tác thêm.</returns>
    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        await _dbContext.Products.AddAsync(product, cancellationToken);
    }

    /// <summary>
    /// Kiểm tra product nhanh gọn: trả true khi ID tồn tại và tồn kho đủ yêu cầu; false nếu không tồn tại hoặc thiếu hàng.
    /// </summary>
    /// <param name="id">ProductId cần kiểm tra.</param>
    /// <param name="quantity">Số lượng phải có trên kho để xem là đủ.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>True khi kho đủ; ngược lại false.</returns>
    public ValueTask<bool> CheckStockAsync(ProductId id, int quantity, CancellationToken cancellationToken = default)
    {
        // Không load aggregate hay trả về entity; query này chỉ cần AnyAsync với filter đúng semantics cho stock check nhanh.
        // Chúng ta không nối method này vào Reserve để giữ nguyên quy tắc aggregate guard và tránh query thừa trên đường success.
        return new ValueTask<bool>(_dbContext.Products
            .AsNoTracking()
            .AnyAsync(product => product.Id == id && product.StockQuantity >= quantity, cancellationToken));
    }

    /// <summary>
    /// Tìm Product theo strongly typed ID, trả null khi không tồn tại.
    /// </summary>
    /// <param name="id">ProductId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Product nếu tồn tại; ngược lại null.</returns>
    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Products.FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
    }

    /// <summary>
    /// Lưu mọi thay đổi Product đang được context theo dõi.
    /// </summary>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Task đại diện cho thao tác lưu.</returns>
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
