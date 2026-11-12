// File này triển khai repository Product bằng EF Core cho InventoryService.
// Tạo ở Day 48 để command/query đọc và lưu aggregate mà không dùng Include vì Product không có collection con.
// Gateway Day 49 và Saga Day 52 sẽ truy cập tồn kho qua hợp đồng repository này.
// Day 61 biên dịch stock-check query; Reserve vẫn giữ transaction và SELECT FOR UPDATE như trước.

using InventoryService.Application;
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
    // Delegate static readonly để chỉ biên dịch expression một lần và tái sử dụng qua mọi instance repository.
    private static readonly Func<InventoryDbContext, ProductId, int, CancellationToken, Task<bool>> CompiledCheckStock =
        EF.CompileAsyncQuery((InventoryDbContext dbContext, ProductId productId, int quantity, CancellationToken cancellationToken) =>
            dbContext.Products
                .AsNoTracking()
                .Any(product => product.Id == productId && product.StockQuantity >= quantity));

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
        // Compiled query cache biểu thức EF Core để tránh re-bien dịch mỗi lần stock check; không nối vào Reserve vì Day 56/58 yêu cầu reserve vẫn dùng transaction và FOR UPDATE riêng.
        return new ValueTask<bool>(CompiledCheckStock(_dbContext, id, quantity, cancellationToken));
    }

    /// <summary>
    /// Reserve hàng bằng SELECT FOR UPDATE trong transaction rõ ràng để chặn race condition giữa nhiều request cùng đọc stock.
    /// </summary>
    /// <param name="id">ProductId cần reserve.</param>
    /// <param name="quantity">Số lượng cần giữ.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Product sau khi đã trừ kho và lưu transaction thành công.</returns>
    /// <exception cref="ProductNotFoundException">Ném khi không tìm thấy product theo id.</exception>
    /// <exception cref="InventoryDomainException">Ném khi số lượng không hợp lệ hoặc kho không đủ; exception này được giữ nguyên theo domain rule.</exception>
    public async Task<Product> ReserveAsync(ProductId id, int quantity, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Transaction tường minh là bắt buộc ở đây: FOR UPDATE chỉ giữ lock trong khoảng transaction.
            // Nếu không mở transaction, SELECT FOR UPDATE sẽ release lock ngay sau câu SELECT và race condition vẫn xảy ra.
            var product = await _dbContext.Products
                .FromSqlRaw("SELECT * FROM products WHERE id = {0} FOR UPDATE", id.Value)
                .SingleOrDefaultAsync(cancellationToken);

            if (product is null)
            {
                throw new ProductNotFoundException(id.Value);
            }

            // Domain rule Reserve phải giữ nguyên: thiếu hàng throw InventoryDomainException, không sửa aggregate.
            product.Reserve(quantity);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return product;
        }
        catch
        {
            // Nếu Reserve ném thiếu hàng hoặc SaveChanges lỗi, rollback transaction để tránh lưu trạng thái bán trùng.
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Tìm Product theo strongly typed ID, trả null khi không tồn tại.
    /// </summary>
    /// <param name="id">ProductId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Product nếu tồn tại; ngược lại null.</returns>
    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
    {
        // Dù truy vấn tại đây có thể đọc aggregate, đường ghi cần entity được theo dõi để EF phát hiện thay đổi và save đúng.
        return _dbContext.Products.FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
    }

    /// <summary>
    /// Chỉ đọc product theo ID cho endpoint query mà không kích hoạt tracking để giảm overhead và tránh sửa đổi ChangeTracker.
    /// </summary>
    /// <param name="id">ProductId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Product nếu tồn tại; ngược lại null.</returns>
    public Task<Product?> GetByIdReadOnlyAsync(ProductId id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
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
