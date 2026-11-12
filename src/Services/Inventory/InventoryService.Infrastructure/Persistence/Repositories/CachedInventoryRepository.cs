// File này chứa decorator Redis cache-aside cho Inventory repository, giúp đường đọc hot-path đáp ứng nhanh hơn
// mà vẫn giữ nguyên semantics database và domain rule của ProductRepository.
// Tạo ở Day 62 để cache đọc Inventory trên Redis và invalidate tự động sau reserve/release/save.
// Day 63+ có thể dùng cùng decorator này cho các endpoint hoặc background job cần cache access pattern tương tự.

using System.Text.Json;
using InventoryService.Domain.Aggregates;
using InventoryService.Domain.Repositories;
using InventoryService.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace InventoryService.Infrastructure.Persistence.Repositories;

/// <summary>
/// Decorator cache-aside cho ProductRepository, đọc Product từ Redis trước và invalidate sau các thao tác ghi.
/// </summary>
public sealed class CachedInventoryRepository : IProductRepository
{
    private const string CachePrefix = "inventory:product:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly IProductRepository _inner;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CachedInventoryRepository> _logger;
    private readonly HashSet<Guid> _trackedProductIds = new();

    /// <summary>
    /// Tạo decorator cache-aside với repository gốc và connection multiplexer Redis dùng cho cache + lock phân tán.
    /// </summary>
    /// <param name="inner">Repository gốc lưu dữ liệu thật trên PostgreSQL.</param>
    /// <param name="redis">ConnectionMultiplexer duy nhất chia sẻ giữa cache và lock.</param>
    /// <param name="logger">Logger để ghi cảnh báo khi Redis lỗi.</param>
    public CachedInventoryRepository(IProductRepository inner, IConnectionMultiplexer redis, ILogger<CachedInventoryRepository> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Thêm Product vào DB và ghi lại id để SaveChangesAsync invalidate cache khi write thành công.
    /// </summary>
    /// <param name="product">Product mới cần lưu.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        await _inner.AddAsync(product, cancellationToken);
        TrackProductId(product.Id.Value);
    }

    /// <summary>
    /// Kiểm tra stock bằng cache trước; nếu miss mới đọc database và warm cache.
    /// </summary>
    /// <param name="id">ID sản phẩm cần kiểm tra.</param>
    /// <param name="quantity">Số lượng tối thiểu cần có.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>True nếu tồn kho trong cache hoặc database đủ điều kiện, ngược lại false.</returns>
    public async ValueTask<bool> CheckStockAsync(ProductId id, int quantity, CancellationToken cancellationToken = default)
    {
        var cachedProduct = await TryGetCachedProductAsync(id);
        if (cachedProduct is not null)
        {
            return cachedProduct.StockQuantity >= quantity;
        }

        var product = await _inner.GetByIdReadOnlyAsync(id, cancellationToken);
        if (product is not null)
        {
            await SetCachedProductAsync(product);
        }

        return product is not null && product.StockQuantity >= quantity;
    }

    /// <summary>
    /// Reserve hàng qua repository gốc rồi xóa cache product vì số lượng đã đổi ngay.
    /// </summary>
    /// <param name="id">ID sản phẩm cần reserve.</param>
    /// <param name="quantity">Số lượng cần giữ.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Product sau khi reserve thành công.</returns>
    public async Task<Product> ReserveAsync(ProductId id, int quantity, CancellationToken cancellationToken = default)
    {
        var product = await _inner.ReserveAsync(id, quantity, cancellationToken);
        await DeleteCachedProductAsync(id);
        return product;
    }

    /// <summary>
    /// Đọc product theo ID ở đường ghi, cần tracked entity cho SaveChangesAsync nên gọi inner trực tiếp.
    /// </summary>
    /// <param name="id">ID sản phẩm cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Product nếu có; ngược lại null.</returns>
    public async Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
    {
        var product = await _inner.GetByIdAsync(id, cancellationToken);
        if (product is not null)
        {
            TrackProductId(product.Id.Value);
        }

        return product;
    }

    /// <summary>
    /// Đường đọc chính: ưu tiên hit cache, miss sẽ lấy lock SETNX để giảm stampede và warm cache lại.
    /// </summary>
    /// <param name="id">ID sản phẩm cần đọc.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Product nếu tìm thấy; ngược lại null.</returns>
    public async Task<Product?> GetByIdReadOnlyAsync(ProductId id, CancellationToken cancellationToken = default)
    {
        var cachedProduct = await TryGetCachedProductAsync(id);
        if (cachedProduct is not null)
        {
            return cachedProduct;
        }

        var lockKey = GetLockKey(id);
        var lockAcquired = await TryAcquireLockAsync(lockKey);
        if (lockAcquired)
        {
            try
            {
                // Lock chỉ là best-effort chống stampede; correctness vẫn dựa vào DB + TTL + invalidate sau write.
                // Token hết hạn hoặc process crash vẫn không làm khóa treo vĩnh viễn vì TTL 5 giây được đặt trên lock key.
                var product = await _inner.GetByIdReadOnlyAsync(id, cancellationToken);
                if (product is not null)
                {
                    await SetCachedProductAsync(product);
                }

                return product;
            }
            finally
            {
                await DeleteKeyAsync(lockKey);
            }
        }

        await Task.Delay(LockRetryDelay, cancellationToken);

        var retryCachedProduct = await TryGetCachedProductAsync(id);
        if (retryCachedProduct is not null)
        {
            return retryCachedProduct;
        }

        var fallbackProduct = await _inner.GetByIdReadOnlyAsync(id, cancellationToken);
        if (fallbackProduct is not null)
        {
            await SetCachedProductAsync(fallbackProduct);
        }

        return fallbackProduct;
    }

    /// <summary>
    /// Lưu các thay đổi ghi vào DB rồi xóa cache cho tất cả id đã được track trong request hiện tại.
    /// </summary>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Task đại diện cho thao tác lưu.</returns>
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _inner.SaveChangesAsync(cancellationToken);

        var trackedProductIds = _trackedProductIds.ToArray();
        foreach (var productId in trackedProductIds)
        {
            await DeleteCachedProductAsync(ProductId.From(productId));
        }

        _trackedProductIds.Clear();
    }

    private void TrackProductId(Guid productId)
    {
        _trackedProductIds.Add(productId);
    }

    private static string GetCacheKey(ProductId id)
    {
        return $"{CachePrefix}{id.Value}";
    }

    private static string GetLockKey(ProductId id)
    {
        return $"{GetCacheKey(id)}:lock";
    }

    private async Task<Product?> TryGetCachedProductAsync(ProductId id)
    {
        try
        {
            var redisValue = await _redis.GetDatabase().StringGetAsync(GetCacheKey(id));
            if (redisValue.IsNullOrEmpty)
            {
                return null;
            }

            var cachedProduct = JsonSerializer.Deserialize<CachedProduct>(redisValue!);
            if (cachedProduct is null)
            {
                return null;
            }

            // Không serialize trực tiếp entity Product vì aggregate có constructor private và không deserialize ra được.
            // Cache cần lưu DTO thuần và tái tạo aggregate thông qua Product.Create để giữ domain invariant.
            return Product.Create(
                ProductId.From(cachedProduct.Id),
                cachedProduct.Name,
                cachedProduct.PriceAmount,
                cachedProduct.PriceCurrency,
                cachedProduct.StockQuantity);
        }
        catch (RedisConnectionException exception)
        {
            // Redis là cache tối ưu hóa, không phải sự sống còn của API; khi Redis xuống, hệ thống vẫn phải fallback về DB.
            _logger.LogWarning(exception, "Redis unavailable, falling back to database when reading cache for product {ProductId}.", id.Value);
            return null;
        }
        catch (RedisTimeoutException exception)
        {
            _logger.LogWarning(exception, "Redis timeout while reading product cache {ProductId}, falling back to database.", id.Value);
            return null;
        }
    }

    private async Task SetCachedProductAsync(Product product)
    {
        try
        {
            var cachedProduct = new CachedProduct(
                product.Id.Value,
                product.Name,
                product.PriceAmount,
                product.PriceCurrency,
                product.StockQuantity);

            var json = JsonSerializer.Serialize(cachedProduct);
            await _redis.GetDatabase().StringSetAsync(GetCacheKey(product.Id), json, CacheTtl);
        }
        catch (RedisConnectionException exception)
        {
            _logger.LogWarning(exception, "Redis unavailable, falling back to database while warming cache for product {ProductId}.", product.Id.Value);
        }
        catch (RedisTimeoutException exception)
        {
            _logger.LogWarning(exception, "Redis timeout while warming cache for product {ProductId}, falling back to database.", product.Id.Value);
        }
    }

    private async Task DeleteCachedProductAsync(ProductId id)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(GetCacheKey(id));
        }
        catch (RedisConnectionException exception)
        {
            _logger.LogWarning(exception, "Redis unavailable, falling back to database after invalidating cache for product {ProductId}.", id.Value);
        }
        catch (RedisTimeoutException exception)
        {
            _logger.LogWarning(exception, "Redis timeout while invalidating cache for product {ProductId}, falling back to database.", id.Value);
        }
    }

    private async Task DeleteKeyAsync(string key)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(key);
        }
        catch (RedisConnectionException exception)
        {
            _logger.LogWarning(exception, "Redis unavailable while releasing distributed lock on key {LockKey}.", key);
        }
        catch (RedisTimeoutException exception)
        {
            _logger.LogWarning(exception, "Redis timeout while releasing distributed lock on key {LockKey}.", key);
        }
    }

    private async Task<bool> TryAcquireLockAsync(string lockKey)
    {
        try
        {
            // StackExchange.Redis không có API IDistributedCache SETNX nguyên tử, nên Day 62 dùng StringSet với When.NotExists
            // để tạo distributed lock và tránh thundering herd khi nhiều request cùng miss cache trong cùng thời điểm.
            return await _redis.GetDatabase().StringSetAsync(lockKey, "1", LockTtl, When.NotExists);
        }
        catch (RedisConnectionException exception)
        {
            _logger.LogWarning(exception, "Redis unavailable, falling back to database while acquiring lock for key {LockKey}.", lockKey);
            return false;
        }
        catch (RedisTimeoutException exception)
        {
            _logger.LogWarning(exception, "Redis timeout while acquiring lock for key {LockKey}, falling back to database.", lockKey);
            return false;
        }
    }

    private sealed record CachedProduct(Guid Id, string Name, decimal PriceAmount, string PriceCurrency, int StockQuantity);
}
