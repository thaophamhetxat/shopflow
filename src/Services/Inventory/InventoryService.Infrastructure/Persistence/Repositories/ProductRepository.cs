// File này triển khai repository Product bằng EF Core cho InventoryService.
// Tạo ở Day 48 để command/query đọc và lưu aggregate mà không dùng Include vì Product không có collection con.
// Gateway Day 49 và Saga Day 52 sẽ truy cập tồn kho qua hợp đồng repository này.
// Day 61 biên dịch stock-check query; Reserve vẫn giữ transaction và SELECT FOR UPDATE như trước.

using System.Data;
using InventoryService.Application;
using InventoryService.Domain.Aggregates;
using InventoryService.Domain.Repositories;
using InventoryService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

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
    private readonly ILogger<ProductRepository> _logger;

    /// <summary>
    /// Tạo repository với context inventory hiện tại và logger.
    /// </summary>
    /// <param name="dbContext">Context EF Core quản lý Product.</param>
    /// <param name="logger">Logger ghi cảnh báo khi reservation cần retry sau lỗi serialization.</param>
    public ProductRepository(InventoryDbContext dbContext, ILogger<ProductRepository>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger ?? NullLogger<ProductRepository>.Instance;
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
    /// <exception cref="PostgresException">Ném lỗi PostgreSQL không phải lỗi serialization hoặc lỗi serialization sau năm lần thử.</exception>
    public async Task<Product> ReserveAsync(ProductId id, int quantity, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Product? product = null;
            try
            {
                // Serializable + FOR UPDATE có thể khiến PostgreSQL hủy transaction đang tranh chấp row bằng SQLSTATE 40001.
                // Năm lần thử tổng cộng là giới hạn để contention không tạo vòng lặp vô hạn; mỗi lần bắt đầu transaction mới.
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                try
                {
                    // Retry phải chạy lại SELECT FOR UPDATE để đọc stock mới nhất và áp dụng domain rule trên dữ liệu đã commit.
                    // Transaction tường minh giữ row lock tới khi reservation được lưu và commit.
                    product = await _dbContext.Products
                        .FromSqlRaw("SELECT * FROM products WHERE id = {0} FOR UPDATE", id.Value)
                        .SingleOrDefaultAsync(cancellationToken);

                    if (product is null)
                    {
                        throw new ProductNotFoundException(id.Value);
                    }

                    // Lỗi nghiệp vụ như hết hàng không phải lỗi serialization nên không được retry.
                    product.Reserve(quantity);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return product;
                }
                catch
                {
                    // Nếu Reserve ném thiếu hàng hoặc thao tác ghi lỗi, rollback để transaction không lưu trạng thái dở dang.
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            catch (Exception exception) when (FindPostgresException(exception)?.SqlState == PostgresErrorCodes.SerializationFailure)
            {
                if (attempt == maxAttempts)
                {
                    throw;
                }

                // EF có thể bọc lỗi provider; chỉ SQLSTATE 40001 mới retry. Detach entity rollback để retry không dùng stock cũ trong ChangeTracker.
                var trackedProduct = product ?? _dbContext.Products.Local.FirstOrDefault(candidate => candidate.Id == id);
                if (trackedProduct is not null)
                {
                    _dbContext.Entry(trackedProduct).State = EntityState.Detached;
                }

                _logger.LogWarning(
                    exception,
                    "ReserveAsync serialization failure {SqlState}; retrying attempt {Attempt}/{MaxAttempts} for ProductId {ProductId}.",
                    PostgresErrorCodes.SerializationFailure,
                    attempt,
                    maxAttempts,
                    id.Value);

                // Backoff lũy thừa 50/100/200/400 ms giúp giao dịch có thời gian thoát tranh chấp.
                // Jitter ngẫu nhiên phá đồng bộ retry khi nhiều transaction cùng bị abort, tránh chúng va chạm lại theo nhịp cố định.
                var backoffMilliseconds = 50 * (1 << (attempt - 1)) + Random.Shared.Next(0, 50);
                await Task.Delay(TimeSpan.FromMilliseconds(backoffMilliseconds), cancellationToken);
            }
        }

        throw new InvalidOperationException("ReserveAsync completed without a reservation result.");
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
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
