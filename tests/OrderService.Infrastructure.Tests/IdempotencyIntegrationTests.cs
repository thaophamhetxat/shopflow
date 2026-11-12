// File này chứa test tích hợp cho Day 45: PlaceOrder, CancelOrder và GetOrderById chạy thật qua PostgreSQL + Redis local.
// Tạo ở Day 45 để chứng minh handler, repository và idempotency hoạt động với hạ tầng thực tế, không mock/hình dung. 
// Test này CẦN Docker Postgres + Redis đang chạy trên localhost:5432 và localhost:6379; nếu chưa chạy thì sẽ fail vì không có infra để kết nối.

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderService.Application;
using OrderService.Application.Commands;
using OrderService.Application.Queries;
using OrderService.Infrastructure.Persistence;
using Xunit;

namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Test tích hợp kiểm chứng Day 45: PlaceOrder 2 lần cùng key chỉ tạo 1 order, GetOrderById query + CancelOrder command hoạt động thật trên PostgreSQL và Redis local.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class IdempotencyIntegrationTests
{
    private static readonly SemaphoreSlim DatabaseLock = DatabaseTestLock.Shared;

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderDb"] = Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
                    ?? "Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;Password=shopflow123;",
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    private static async Task ResetDatabaseAsync(IServiceProvider provider, params string[] idempotencyKeys)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await dbContext.Database.MigrateAsync();
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM orders;");

        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        foreach (var key in idempotencyKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            await cache.RemoveAsync($"order:idempotency:{key}");
        }
    }

    // Test này chứng minh Evidence Day 45: cùng một IdempotencyKey gửi 2 lần thì hệ thống chỉ tạo 1 Order và trả về cùng OrderId.
    [Fact]
    public async Task PlaceOrder_TwiceWithSameIdempotencyKey_CreatesOneOrder()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider, "same-key");
                var mediator = provider.GetRequiredService<IMediator>();
                var command = new PlaceOrderCommand(
                    Guid.NewGuid(),
                    "123 Lê Lợi",
                    "Hà Nội",
                    "Việt Nam",
                    "100000",
                    new List<PlaceOrderItem>
                    {
                        new(Guid.NewGuid(), 2, 200000m, "VND")
                    },
                    "same-key");

                var firstOrderId = await mediator.Send(command);
                var secondOrderId = await mediator.Send(command);

                Assert.Equal(firstOrderId, secondOrderId);

                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var orderCount = await dbContext.Orders.CountAsync();
                Assert.Equal(1, orderCount);
            }
            finally
            {
                await ResetDatabaseAsync(provider, "same-key");
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 45: Order không có IdempotencyKey vẫn được tạo đúng 1 đơn như request bình thường.
    [Fact]
    public async Task PlaceOrder_WithoutIdempotencyKey_CreatesOrderNormally()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider);
                var mediator = provider.GetRequiredService<IMediator>();
                var command = new PlaceOrderCommand(
                    Guid.NewGuid(),
                    "45 Trần Hưng Đạo",
                    "Đà Nẵng",
                    "Việt Nam",
                    "550000",
                    new List<PlaceOrderItem>
                    {
                        new(Guid.NewGuid(), 1, 150000m, "VND")
                    });

                var orderId = await mediator.Send(command);

                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var orderCount = await dbContext.Orders.CountAsync();

                Assert.NotEqual(default, orderId);
                Assert.Equal(1, orderCount);
            }
            finally
            {
                await ResetDatabaseAsync(provider);
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 45: khác key thì hệ thống xem là 2 request khác nhau và tạo 2 Order khác nhau.
    [Fact]
    public async Task PlaceOrder_DifferentKeys_CreateTwoOrders()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider, "key-one", "key-two");
                var mediator = provider.GetRequiredService<IMediator>();

                var firstOrderId = await mediator.Send(new PlaceOrderCommand(
                    Guid.NewGuid(),
                    "12 Hùng Vương",
                    "Huế",
                    "Việt Nam",
                    "500000",
                    new List<PlaceOrderItem> { new(Guid.NewGuid(), 1, 120000m, "VND") },
                    "key-one"));

                var secondOrderId = await mediator.Send(new PlaceOrderCommand(
                    Guid.NewGuid(),
                    "12 Hùng Vương",
                    "Huế",
                    "Việt Nam",
                    "500000",
                    new List<PlaceOrderItem> { new(Guid.NewGuid(), 1, 90000m, "VND") },
                    "key-two"));

                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var orderCount = await dbContext.Orders.CountAsync();

                Assert.NotEqual(firstOrderId, secondOrderId);
                Assert.Equal(2, orderCount);
            }
            finally
            {
                await ResetDatabaseAsync(provider, "key-one", "key-two");
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 45: GetOrderById phải đọc thật Order từ PostgreSQL và map sang DTO không null với dữ liệu đúng.
    [Fact]
    public async Task GetOrderById_ReturnsDto_WhenFound()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider, "query-key");
                var mediator = provider.GetRequiredService<IMediator>();
                var orderId = await mediator.Send(new PlaceOrderCommand(
                    Guid.NewGuid(),
                    "99 Bạch Đằng",
                    "Cần Thơ",
                    "Việt Nam",
                    "700000",
                    new List<PlaceOrderItem>
                    {
                        new(Guid.NewGuid(), 3, 50000m, "VND")
                    },
                    "query-key"));

                OrderDetailDto? dto = await mediator.Send(new GetOrderByIdQuery(orderId.Value));

                Assert.NotNull(dto);
                Assert.Equal(orderId.Value, dto!.OrderId);
                Assert.Equal("Pending", dto.Status);
                Assert.Equal(150000m, dto.TotalAmount);
                Assert.Equal("VND", dto.TotalCurrency);
            }
            finally
            {
                await ResetDatabaseAsync(provider, "query-key");
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 45: query không tìm thấy đơn phải trả null, không ném exception, khác với command hủy đơn.
    [Fact]
    public async Task GetOrderById_ReturnsNull_WhenNotFound()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider);
                var mediator = provider.GetRequiredService<IMediator>();

                OrderDetailDto? dto = await mediator.Send(new GetOrderByIdQuery(Guid.NewGuid()));

                Assert.Null(dto);
            }
            finally
            {
                await ResetDatabaseAsync(provider);
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 45: CancelOrder phải đọc đơn thật, đổi trạng thái thành Cancelled rồi lưu lại qua repository.
    [Fact]
    public async Task CancelOrder_CancelsExistingOrder()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider, "cancel-key");
                var mediator = provider.GetRequiredService<IMediator>();
                var orderId = await mediator.Send(new PlaceOrderCommand(
                    Guid.NewGuid(),
                    "77 Nguyễn Văn Cừ",
                    "Nha Trang",
                    "Việt Nam",
                    "650000",
                    new List<PlaceOrderItem>
                    {
                        new(Guid.NewGuid(), 2, 100000m, "VND")
                    },
                    "cancel-key"));

                await mediator.Send(new CancelOrderCommand(orderId.Value));
                OrderDetailDto? dto = await mediator.Send(new GetOrderByIdQuery(orderId.Value));

                Assert.NotNull(dto);
                Assert.Equal("Cancelled", dto!.Status);
            }
            finally
            {
                await ResetDatabaseAsync(provider, "cancel-key");
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 45: cancel đơn không tồn tại phải ném OrderNotFoundException rõ ràng thay vì trả về null.
    [Fact]
    public async Task CancelOrder_NotFound_ThrowsOrderNotFoundException()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider);
                var mediator = provider.GetRequiredService<IMediator>();

                await Assert.ThrowsAsync<OrderNotFoundException>(() => mediator.Send(new CancelOrderCommand(Guid.NewGuid())));
            }
            finally
            {
                await ResetDatabaseAsync(provider);
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }
}
