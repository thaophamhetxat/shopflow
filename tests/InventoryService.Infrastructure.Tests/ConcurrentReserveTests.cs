// File này chứng minh reserve đồng thời trên cùng product không oversell và helper AsyncLock hoạt động đúng.
// Day 58 dùng hai test này để chứng minh row lock ở database là fix chống race, còn AsyncLock chỉ là primitive async.
// Layer: Infrastructure tests; chứng minh oversell không còn xảy ra sau khi dùng SELECT FOR UPDATE trong repository.
// Day 58: test này là evidence cho bug fix về lock row và async-aware helper cho future consumers.
using InventoryService.Application.Commands;
using InventoryService.Domain.Aggregates;
using InventoryService.Domain.Exceptions;
using InventoryService.Domain.ValueObjects;
using InventoryService.Infrastructure.Persistence;
using InventoryService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using ShopFlow.BuildingBlocks;
using Xunit;

namespace InventoryService.Infrastructure.Tests;

/// <summary>
/// Test này kiểm tra hai điều quan trọng của Day 58: reserve đồng thời phải không oversell và AsyncLock phải chặn khóa thứ hai.
/// </summary>
public sealed class ConcurrentReserveTests
{
    private static readonly SemaphoreSlim DatabaseLock = InventoryTestSynchronization.DatabaseLock;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__InventoryDb")
        ?? "Host=localhost;Port=5432;Database=shopflow_inventory;Username=shopflow;Password=shopflow;";

    private static InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new InventoryDbContext(options);
    }

    /// <summary>
    /// Test này tạo product mới với stock 5, reserve 10 request đồng thời từng quantity 1, rồi kiểm tra đúng 5 thành công và 5 thất bại.
    /// Nếu không có row lock, cùng lúc nhiều request đọc stock 5 sẽ vượt quá tồn kho và oversell âm; đây là evidence Day 58 cho race fix.
    /// </summary>
    [Fact]
    public async Task Concurrent_ReserveSameProduct_NoOversell()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var setupContext = CreateContext();
            await setupContext.Database.EnsureCreatedAsync();
            await setupContext.Products.ExecuteDeleteAsync();

            var productId = ProductId.New();
            var product = Product.Create(productId, "Concurrent Product", 99m, "USD", 5);
            setupContext.Products.Add(product);
            await setupContext.SaveChangesAsync();

            var tasks = Enumerable.Range(0, 10)
                .Select(async _ =>
                {
                    await using var context = CreateContext();
                    var handler = new ReserveStockCommandHandler(new ProductRepository(context), new ReserveStockCommandValidator());

                    try
                    {
                        var result = await handler.Handle(new ReserveStockCommand(productId.Value, 1), CancellationToken.None);
                        return result.StockQuantity;
                    }
                    catch (InventoryDomainException)
                    {
                        return -1;
                    }
                })
                .ToArray();

            var results = await Task.WhenAll(tasks);

            Assert.Equal(5, results.Count(stock => stock >= 0));
            Assert.Equal(5, results.Count(stock => stock == -1));

            await using var verificationContext = CreateContext();
            var savedProduct = await verificationContext.Products.SingleAsync(product => product.Id == productId);
            Assert.Equal(0, savedProduct.StockQuantity);
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
    /// Test này đảm bảo AsyncLock chặn người xin khóa thứ hai cho đến khi releaser giải phóng, đồng thời token cancel vẫn được honor.
    /// </summary>
    [Fact]
    public async Task AsyncLock_BlocksSecondAcquirer_UntilReleased()
    {
        var asyncLock = new AsyncLock();

        using var first = await asyncLock.AcquireAsync();

        var secondTask = Task.Run(async () =>
        {
            using var second = await asyncLock.AcquireAsync();
        });

        await Task.Delay(200);
        Assert.False(secondTask.IsCompleted);

        first.Dispose();
        await secondTask;

        using var held = await asyncLock.AcquireAsync();
        var cancellationSource = new CancellationTokenSource();
        var blockedAcquisition = asyncLock.AcquireAsync(cancellationSource.Token);
        cancellationSource.CancelAfter(200);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await blockedAcquisition);
    }
}
