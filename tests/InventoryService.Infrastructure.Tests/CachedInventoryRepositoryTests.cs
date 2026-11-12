// File này chứa test cache-aside cho InventoryService.Infrastructure, kiểm tra cache hit, lock, invalidate và fallback khi Redis down.
// Layer Infrastructure test xác nhận decorator Redis không phá semantics database và hoạt động đúng trên PostgreSQL thật.
// Day 62 tạo file này để chứng minh cache read path, invalidation và pool settings trong Inventory API.

using System.Text.Json;
using InventoryService.Domain.Aggregates;
using InventoryService.Domain.ValueObjects;
using InventoryService.Infrastructure.Persistence;
using InventoryService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace InventoryService.Infrastructure.Tests;

/// <summary>
/// Test cache-aside Redis cho Inventory repository, đảm bảo hit cache, invalidate sau reserve/release và fallback khi Redis không khả dụng.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class CachedInventoryRepositoryTests
{
    private static readonly SemaphoreSlim DatabaseLock = InventoryTestSynchronization.DatabaseLock;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__InventoryDb")
        ?? "Host=localhost;Port=5432;Database=shopflow_inventory;Username=shopflow;Password=shopflow123;";

    private static InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new InventoryDbContext(options);
    }

    private static IConnectionMultiplexer CreateRedis()
    {
        return ConnectionMultiplexer.Connect("localhost:6379");
    }

    private static async Task ClearRedisAsync(IConnectionMultiplexer redis, Guid productId)
    {
        var database = redis.GetDatabase();
        await database.KeyDeleteAsync($"inventory:product:{productId}");
        await database.KeyDeleteAsync($"inventory:product:{productId}:lock");
    }

    /// <summary>
    /// Redis cache phải trả đúng sản phẩm cũ trên request thứ hai dù DB đã đổi, chứng minh cache-aside hoạt động đúng.
    /// </summary>
    [Fact]
    public async Task GetProductReadOnly_ReturnsCachedValue_OnSecondCall()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
            await context.Products.ExecuteDeleteAsync();

            var productId = ProductId.New();
            var product = Product.Create(productId, "Kiếm Sắt", 250000m, "VND", 10);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            using var redis = CreateRedis();
            var repository = new CachedInventoryRepository(new ProductRepository(context), redis, NullLogger<CachedInventoryRepository>.Instance);

            var first = await repository.GetByIdReadOnlyAsync(productId);
            Assert.NotNull(first);
            Assert.Equal(10, first!.StockQuantity);
            Assert.True(await redis.GetDatabase().KeyExistsAsync($"inventory:product:{productId.Value}"));

            await context.Products
                .Where(item => item.Id == productId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.StockQuantity, 99));

            var second = await repository.GetByIdReadOnlyAsync(productId);
            Assert.NotNull(second);
            Assert.Equal(10, second!.StockQuantity);

            await ClearRedisAsync(redis, productId.Value);
            var third = await repository.GetByIdReadOnlyAsync(productId);
            Assert.NotNull(third);
            Assert.Equal(99, third!.StockQuantity);
        }
        finally
        {
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.EnsureCreatedAsync();
            await cleanupContext.Products.ExecuteDeleteAsync();
            DatabaseLock.Release();
        }
    }

    /// <summary>
    /// Cache-stock path phải hit trên bộ nhớ ngay cả khi row DB bị xóa, cho đến khi cache bị invalidated.
    /// </summary>
    [Fact]
    public async Task CheckStockAsync_UsesCache_WhenProductCached()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
            await context.Products.ExecuteDeleteAsync();

            var productId = ProductId.New();
            var product = Product.Create(productId, "Bùa Hộ Mệnh", 120000m, "VND", 10);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            using var redis = CreateRedis();
            var repository = new CachedInventoryRepository(new ProductRepository(context), redis, NullLogger<CachedInventoryRepository>.Instance);
            var warmProduct = await repository.GetByIdReadOnlyAsync(productId);
            Assert.NotNull(warmProduct);

            await using var deleteContext = CreateContext();
            await deleteContext.Products
                .Where(item => item.Id == productId)
                .ExecuteDeleteAsync();

            Assert.True(await repository.CheckStockAsync(productId, 5));
            Assert.False(await repository.CheckStockAsync(productId, 11));

            await ClearRedisAsync(redis, productId.Value);
            Assert.False(await repository.CheckStockAsync(productId, 5));
        }
        finally
        {
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.EnsureCreatedAsync();
            await cleanupContext.Products.ExecuteDeleteAsync();
            DatabaseLock.Release();
        }
    }

    /// <summary>
    /// Reserve phải xóa cache product để tránh đọc stale stock từ Redis sau khi giá trị kho đã đổi.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_InvalidatesCachedProduct()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
            await context.Products.ExecuteDeleteAsync();

            var productId = ProductId.New();
            var product = Product.Create(productId, "Áo Giáp Da", 180000m, "VND", 10);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            using var redis = CreateRedis();
            var repository = new CachedInventoryRepository(new ProductRepository(context), redis, NullLogger<CachedInventoryRepository>.Instance);
            await repository.GetByIdReadOnlyAsync(productId);

            var reserved = await repository.ReserveAsync(productId, 3);
            Assert.Equal(7, reserved.StockQuantity);
            Assert.False(await redis.GetDatabase().KeyExistsAsync($"inventory:product:{productId.Value}"));

            var reloaded = await repository.GetByIdReadOnlyAsync(productId);
            Assert.NotNull(reloaded);
            Assert.Equal(7, reloaded!.StockQuantity);
        }
        finally
        {
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.EnsureCreatedAsync();
            await cleanupContext.Products.ExecuteDeleteAsync();
            DatabaseLock.Release();
        }
    }

    /// <summary>
    /// Release qua GetByIdAsync + SaveChangesAsync phải xóa cache trước khi đọc lại để đảm bảo dữ liệu mới nhất hiển thị.
    /// </summary>
    [Fact]
    public async Task ReleaseStock_InvalidatesCachedProduct()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
            await context.Products.ExecuteDeleteAsync();

            var productId = ProductId.New();
            var product = Product.Create(productId, "Thuốc Hồi Phục", 50000m, "VND", 10);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            using var redis = CreateRedis();
            var repository = new CachedInventoryRepository(new ProductRepository(context), redis, NullLogger<CachedInventoryRepository>.Instance);
            await repository.GetByIdReadOnlyAsync(productId);

            var tracked = await repository.GetByIdAsync(productId);
            Assert.NotNull(tracked);
            tracked!.Release(4);
            await repository.SaveChangesAsync();

            Assert.False(await redis.GetDatabase().KeyExistsAsync($"inventory:product:{productId.Value}"));
            var reloaded = await repository.GetByIdReadOnlyAsync(productId);
            Assert.NotNull(reloaded);
            Assert.Equal(14, reloaded!.StockQuantity);
        }
        finally
        {
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.EnsureCreatedAsync();
            await cleanupContext.Products.ExecuteDeleteAsync();
            DatabaseLock.Release();
        }
    }

    /// <summary>
    /// Khi nhiều request concurrent cùng miss cache, lock SETNX phải giữ đúng giá trị và không làm sai stock.
    /// </summary>
    [Fact]
    public async Task ConcurrentCacheMiss_ReturnsCorrectProduct_ForAllCallers()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
            await context.Products.ExecuteDeleteAsync();

            var productId = ProductId.New();
            var product = Product.Create(productId, "Nhẫn Trữ Vật", 500000m, "VND", 10);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            using var redis = CreateRedis();
            var repository = new CachedInventoryRepository(new ProductRepository(context), redis, NullLogger<CachedInventoryRepository>.Instance);
            await ClearRedisAsync(redis, productId.Value);

            var tasks = Enumerable.Range(0, 10)
                .Select(_ => Task.Run(async () => await repository.GetByIdReadOnlyAsync(productId)))
                .ToArray();

            var products = await Task.WhenAll(tasks);
            Assert.All(products, item => Assert.NotNull(item));
            Assert.All(products, item => Assert.Equal(10, item!.StockQuantity));
        }
        finally
        {
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.EnsureCreatedAsync();
            await cleanupContext.Products.ExecuteDeleteAsync();
            DatabaseLock.Release();
        }
    }

    /// <summary>
    /// Connection string InventoryDb phải chứa pool setting tối thiểu 20/5 để áp dụng tuning tối ưu cho PostgreSQL local.
    /// </summary>
    [Fact]
    public void ConnectionString_HasPoolSizeSettings()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var appSettingsPath = Path.Combine(repoRoot, "src", "Services", "Inventory", "InventoryService.API", "appsettings.json");

        var content = File.ReadAllText(appSettingsPath);
        var jsonStart = content.IndexOf('{');
        var json = jsonStart >= 0 ? content[jsonStart..] : content;

        using var document = JsonDocument.Parse(json);
        var connectionString = document.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty("InventoryDb")
            .GetString();

        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var builder = new NpgsqlConnectionStringBuilder(connectionString!);
        Assert.Equal(20, builder.MaxPoolSize);
        Assert.Equal(5, builder.MinPoolSize);
    }

    /// <summary>
    /// Khi Redis ngừng hoạt động, repository phải fallback về database mà không ném exception cho client.
    /// </summary>
    [Fact]
    public async Task RedisUnavailable_FallsBackToDatabase()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
            await context.Products.ExecuteDeleteAsync();

            var productId = ProductId.New();
            var product = Product.Create(productId, "Kiếm Sắt", 250000m, "VND", 5);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var deadRedis = ConnectionMultiplexer.Connect("localhost:6390,abortConnect=false");
            var repository = new CachedInventoryRepository(new ProductRepository(context), deadRedis, NullLogger<CachedInventoryRepository>.Instance);

            var result = await repository.GetByIdReadOnlyAsync(productId);
            Assert.NotNull(result);
            Assert.Equal(5, result!.StockQuantity);
        }
        finally
        {
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.EnsureCreatedAsync();
            await cleanupContext.Products.ExecuteDeleteAsync();
            DatabaseLock.Release();
        }
    }
}
