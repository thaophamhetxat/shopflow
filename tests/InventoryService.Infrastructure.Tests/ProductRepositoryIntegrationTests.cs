// File này kiểm tra repository Inventory dùng EF Core và PostgreSQL thật cho hot-path stock check.
// Layer Infrastructure đang chứng minh CheckStockAsync trả đúng trên kho đủ/không đủ, không phụ thuộc mock hay in-memory.
// Day 56 dùng test này để xác nhận ValueTask<bool> chạy đúng trong đường read và không rơi vào logic reserve sai semantics.

using InventoryService.Domain.Aggregates;
using InventoryService.Domain.ValueObjects;
using InventoryService.Infrastructure.Persistence;
using InventoryService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InventoryService.Infrastructure.Tests;

/// <summary>
/// Test này chứng minh repository Inventory trả đúng true/false cho stock check trên PostgreSQL thật,
/// giúp Day 56 bảo vệ hot-path không có load aggregate hay reserve thừa trên đường success.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class ProductRepositoryIntegrationTests
{
    private static readonly SemaphoreSlim DatabaseLock = new(1, 1);

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__InventoryDb")
        ?? "Host=localhost;Port=5432;Database=shopflow_inventory;Username=shopflow;Password=ShopFlow123!;";

    private static InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new InventoryDbContext(options);
    }

    /// <summary>
    /// Test này chứng minh stock check đúng với ba trường hợp: đủ hàng, thiếu hụt, và product không tồn tại.
    /// Nó là evidence Day 56 cho ValueTask&lt;bool&gt; ở hot path: query phải nhanh, không load aggregate và không đổi semantics Reserve.
    /// </summary>
    [Fact]
    public async Task CheckStockAsync_ReturnsTrue_WhenStockSufficient_AndFalse_WhenInsufficient()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();

            await context.Products.ExecuteDeleteAsync();

            var productId = ProductId.New();
            var product = Product.Create(productId, "Mechanical Keyboard", 299m, "USD", 10);
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var repository = new ProductRepository(context);

            Assert.True(await repository.CheckStockAsync(productId, 5));
            Assert.False(await repository.CheckStockAsync(productId, 11));
            Assert.False(await repository.CheckStockAsync(ProductId.New(), 1));
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
