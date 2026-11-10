using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Persistence;
using Xunit;


namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Test tích hợp kiểm tra `OrderDbContext.SaveChangesAsync` sẽ lưu Order và outbox message đồng thời trong một transaction PostgreSQL thật.
/// Đây là Evidence Day 43 cho nguyên tắc atomicity: nếu Order lưu thành công thì event cũng tồn tại; nếu Order fail thì không có outbox riêng lẻ.
/// </summary>
public sealed class OutboxIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
        ?? throw new InvalidOperationException("Test tích hợp cần biến môi trường 'ConnectionStrings__OrderDb' trỏ tới PostgreSQL local.");

    /// <summary>
    /// Tạo DbContext theo connection string local của Order database, cho phép test tích hợp chạy chỉ khi Docker Postgres đang hoạt động trên máy dev.
    /// </summary>
    /// <returns>DbContext được cấu hình sẵn để kết nối PostgreSQL local.</returns>
    private static OrderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new OrderDbContext(options);
    }

    // Test này chứng minh Day 43: khi Order phát event trong `SaveChangesAsync`, EF Core ghi both `orders` và `outbox_messages` trong cùng transaction. Cần Docker Postgres đang chạy trên localhost:5432.
    [Fact]
    public async Task SaveChangesAsync_WhenOrderRaisesDomainEvents_ShouldPersistOutboxMessagesInSameTransaction()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");

        try
        {
            var order = Order.Place(CustomerId.New(), new Address("456 Nguyễn Trãi", "Đà Nẵng", "Vietnam", "550000"));
            order.AddItem(new OrderItem(Guid.NewGuid(), 2, new Money(20m, "USD")));
            order.Confirm();

            await context.Orders.AddAsync(order);
            await context.SaveChangesAsync();

            var outboxMessages = await context.OutboxMessages
                .OrderBy(x => x.OccurredOn)
                .ToListAsync();

            Assert.Equal(2, outboxMessages.Count);
            Assert.All(outboxMessages, message => Assert.Null(message.ProcessedAt));
            Assert.Collection(
                outboxMessages,
                first => Assert.EndsWith(nameof(OrderService.Domain.Events.OrderPlacedEvent), first.EventType),
                second => Assert.EndsWith(nameof(OrderService.Domain.Events.OrderConfirmedEvent), second.EventType));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");
        }
    }
}
