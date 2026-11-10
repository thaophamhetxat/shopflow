using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Repositories;
using Xunit;


namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Test tích hợp lưu và đọc lại Order từ PostgreSQL thật để kiểm tra mapping EF Core cho aggregate, owned entity, value object và index.
/// </summary>
public sealed class OrderRepositoryIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
        ?? throw new InvalidOperationException("Test tích hợp cần biến môi trường 'ConnectionStrings__OrderDb' trỏ tới PostgreSQL local.");

    /// <summary>
    /// Tạo context theo connection string local dùng cho database Order của môi trường development.
    /// </summary>
    /// <returns>DbContext đã được cấu hình dành cho PostgreSQL local.</returns>
    private static OrderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new OrderDbContext(options);
    }

    /// <summary>
    /// Test này chứng minh Aggregate Order có thể lưu xuống PostgreSQL và đọc lại đầy đủ, tương ứng với Evidence Day 42 mapping Order + OrderItem + Money + Address.
    /// Test này CẦN Docker Postgres đang chạy trên localhost:5432; không phải unit test chạy mọi lúc mọi nơi.
    /// </summary>
    [Fact]
    public async Task SaveAndReadOrder_ShouldPersistAggregateFromPostgres()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");

        try
        {
            var repository = new OrderRepository(context);
            var customerId = CustomerId.New();
            var shippingAddress = new Address("123 Lê Lợi", "Hà Nội", "Vietnam", "100000");
            var order = Order.Place(customerId, shippingAddress);

            order.AddItem(new OrderItem(Guid.NewGuid(), 2, new Money(25.50m, "USD")));
            order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(99.00m, "USD")));

            await repository.AddAsync(order);
            await repository.SaveChangesAsync();

            var loadedOrder = await repository.GetByIdAsync(order.Id);

            Assert.NotNull(loadedOrder);
            Assert.Equal(order.Id, loadedOrder!.Id);
            Assert.Equal(order.CustomerId, loadedOrder.CustomerId);
            Assert.Equal(order.ShippingAddress, loadedOrder.ShippingAddress);
            Assert.Equal(order.Status, loadedOrder.Status);
            Assert.Equal(order.Total, loadedOrder.Total);
            Assert.Equal(2, loadedOrder.Items.Count);
            Assert.All(loadedOrder.Items, item => Assert.NotEqual(Guid.Empty, item.Id));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");
        }
    }

    // Test này chứng minh Evidence Day 42: repository phải trả về null khi tìm kiếm Order theo Id chưa từng lưu, thay vì ném exception hoặc trả object rỗng.
    [Fact]
    public async Task GetById_ReturnsNull_WhenNotFound()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");

        try
        {
            var repository = new OrderRepository(context);
            var missingId = OrderId.New();

            var result = await repository.GetByIdAsync(missingId);

            Assert.Null(result);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");
        }
    }
}
