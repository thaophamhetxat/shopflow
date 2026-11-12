// File này guard thời gian và isolation cho InventoryService.Infrastructure.
// Layer Infrastructure kiểm tra transaction reservation vẫn chạy ở Serializable và stock check không mất hiệu năng dưới baseline.
// Day 63 thêm fix này để chống write skew và bảo vệ performance hot path sau khi cache Redis và pool tuning đã hoạt động.
// Day sau có thể dùng cùng file này như evidence cho release gate hoặc regression audit khi thay đổi repository.

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using InventoryService.Domain.Aggregates;
using InventoryService.Domain.ValueObjects;
using InventoryService.Infrastructure.Persistence;
using InventoryService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace InventoryService.Infrastructure.Tests;

/// <summary>
/// Guard Day 63 cho InventoryService.Infrastructure, đảm bảo reserve vẫn dùng SERIALIZABLE và stock check không vượt ngưỡng latency.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class PerformanceRegressionGuardTests
{
    private static readonly SemaphoreSlim DatabaseLock = InventoryTestSynchronization.DatabaseLock;
    private readonly ITestOutputHelper _output;

    public PerformanceRegressionGuardTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__InventoryDb")
        ?? throw new InvalidOperationException("Set 'ConnectionStrings__InventoryDb' to run inventory performance guards.");

    private static InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new InventoryDbContext(options);
    }

    private static async Task<Product> SeedProductAsync(InventoryDbContext context, int stockQuantity)
    {
        await context.Database.EnsureCreatedAsync();
        await context.Products.ExecuteDeleteAsync();

        var product = Product.Create(ProductId.New(), "Guard Product", 99m, "USD", stockQuantity);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private sealed class IsolationLevelInterceptor : DbTransactionInterceptor
    {
        public IsolationLevel? ObservedIsolationLevel { get; private set; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            ObservedIsolationLevel = eventData.IsolationLevel;
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>
    /// Test này chứng minh transaction reserve bắt đầu bằng SERIALIZABLE thay vì ReadCommitted mặc định của PostgreSQL.
    /// Day 63 dùng interceptor EF Core để xác nhận isolation level đúng ngay khi BeginTransactionAsync khởi tạo transaction.
    /// </summary>
    [Fact]
    public async Task ReserveAsync_UsesSerializableIsolationLevel()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var interceptor = new IsolationLevelInterceptor();
            await using var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
                .UseNpgsql(ConnectionString)
                .AddInterceptors(interceptor)
                .Options);

            var product = await SeedProductAsync(context, 10);
            var repository = new ProductRepository(context);
            var reserved = await repository.ReserveAsync(product.Id, 1);

            Assert.Equal(9, reserved.StockQuantity);
            Assert.Equal(IsolationLevel.Serializable, interceptor.ObservedIsolationLevel);
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
    /// Guard này kiểm tra avg latency của stock check vẫn thấp hơn ngưỡng 200 ms trên 1000 lần đo, nên không có regression N+1 hoặc compiled query tệ.
    /// Chúng ta đo trung bình nhiều lần để tránh flaky do máy bận, đúng theo nguyên tắc Day 63.
    /// </summary>
    [Fact]
    public async Task CheckStockAsync_AveragesBelowGuardThreshold()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            var product = await SeedProductAsync(context, 125);
            var repository = new ProductRepository(context);

            var timings = new List<double>();
            for (var index = 0; index < 1000; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                var isEnough = await repository.CheckStockAsync(product.Id, 10);
                stopwatch.Stop();

                Assert.True(isEnough);
                timings.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            var average = timings.Average();
            _output.WriteLine($"CheckStock avg={average:F3} ms, n=1000, threshold=<200 ms");
            Assert.True(average < 200, $"Stock check average regression: {average:F3} ms for 1000 iterations.");
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
