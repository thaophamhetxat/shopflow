using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Repositories;
using Xunit;


namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Test t�ch h?p luu v� d?c l?i Order t? PostgreSQL th?t d? ki?m tra mapping EF Core cho aggregate, owned entity, value object v� index.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class OrderRepositoryIntegrationTests
{
    private static readonly SemaphoreSlim DatabaseLock = DatabaseTestLock.Shared;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
        ?? "Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;Password=ShopFlow123!;";

    /// <summary>
    /// T?o context theo connection string local d�ng cho database Order c?a m�i tru?ng development.
    /// </summary>
    /// <returns>DbContext d� du?c c?u h�nh d�nh cho PostgreSQL local.</returns>
    private static OrderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new OrderDbContext(options);
    }

    /// <summary>
    /// Test n�y ch?ng minh Aggregate Order c� th? luu xu?ng PostgreSQL v� d?c l?i d?y d?, tuong ?ng v?i Evidence Day 42 mapping Order + OrderItem + Money + Address.
    /// Test n�y C?N Docker Postgres dang ch?y tr�n localhost:5432; kh�ng ph?i unit test ch?y m?i l�c m?i noi.
    /// </summary>
    [Fact]
    public async Task SaveAndReadOrder_ShouldPersistAggregateFromPostgres()
    {
        await DatabaseLock.WaitAsync();
        try
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
                var shippingAddress = new Address("123 L� L?i", "H� N?i", "Vietnam", "100000");
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
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test n�y ch?ng minh Evidence Day 42: repository ph?i tr? v? null khi t�m ki?m Order theo Id chua t?ng luu, thay v� n�m exception ho?c tr? object r?ng.
    [Fact]
    public async Task GetById_ReturnsNull_WhenNotFound()
    {
        await DatabaseLock.WaitAsync();
        try
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
        finally
        {
            DatabaseLock.Release();
        }
    }
}
