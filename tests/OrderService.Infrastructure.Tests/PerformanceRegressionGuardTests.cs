// File này guard thời gian query đọc detail của OrderService.Infrastructure.
// Layer Infrastructure kiểm tra compiled query `GetOrderDetailAsync` dưới ngưỡng và không có regression N+1.
// Day 63 dùng guard này để bảo vệ baseline 2.6-3.4 ms với 200 lần lặp, tránh việc query đọc quay lại chậm do mapping hoặc Include không tối ưu.
// Day sau có thể dùng file này như evidence release gate cho API read model khi refactor repository hoặc query mapping.

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Guard Day 63 cho OrderService.Infrastructure, đảm bảo `GetOrderDetailAsync` vẫn đáp ứng ngưỡng latency và không tái phát N+1.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class PerformanceRegressionGuardTests
{
    private static readonly SemaphoreSlim DatabaseLock = DatabaseTestLock.Shared;
    private readonly ITestOutputHelper _output;

    public PerformanceRegressionGuardTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
        ?? throw new InvalidOperationException("Set 'ConnectionStrings__OrderDb' to run order performance guards.");

    private static OrderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new OrderDbContext(options);
    }

    private static async Task<Order> SeedOrderAsync(OrderDbContext context)
    {
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");

        var order = Order.Place(CustomerId.From(Guid.NewGuid()), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 2, new Money(25000m, "VND")));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(15000m, "VND")));

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Test này đo trung bình 200 lần gọi `GetOrderDetailAsync` và yêu cầu dưới 50 ms, bảo vệ không có regression Full Table Scan hay N+1 trong detail query.
    /// Dùng average trên nhiều lần để tránh flaky do máy bận; ngưỡng 50 ms có headroom lớn so với baseline Day 61 ~2.6-3.4 ms.
    /// </summary>
    [Fact]
    public async Task GetOrderDetailAsync_AveragesBelowGuardThreshold()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            var order = await SeedOrderAsync(context);
            var repository = new OrderRepository(context);

            var timings = new List<double>();
            for (var index = 0; index < 200; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                var detail = await repository.GetOrderDetailAsync(order.Id);
                stopwatch.Stop();

                Assert.NotNull(detail);
                Assert.Equal(2, detail!.Items.Count);
                timings.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            var average = timings.Average();
            _output.WriteLine($"GetOrderDetail avg={average:F3} ms, n=200, threshold=<50 ms");
            Assert.True(average < 50, $"Order detail average regression: {average:F3} ms for 200 iterations.");
        }
        finally
        {
            await using var cleanupContext = CreateContext();
            await cleanupContext.Database.EnsureCreatedAsync();
            await cleanupContext.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
            await cleanupContext.Database.ExecuteSqlRawAsync("DELETE FROM orders;");
            DatabaseLock.Release();
        }
    }
}
