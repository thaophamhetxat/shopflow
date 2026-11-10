using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Outbox;
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

    private static OutboxProcessor CreateProcessor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(ConnectionString));

        var provider = services.BuildServiceProvider();
        return new OutboxProcessor(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<ILogger<OutboxProcessor>>());
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

    // Test này chứng minh Evidence Day 43: khi processor chạy một vòng xử lý, nó phải đọc outbox chưa xử lý và đánh dấu `ProcessedAt` cho message này bằng PostgreSQL thật.
    [Fact]
    public async Task OutboxProcessor_MarksProcessed()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");

        try
        {
            var order = Order.Place(CustomerId.New(), new Address("789 Hai Bà Trưng", "Huế", "Vietnam", "500000"));
            order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(15m, "USD")));

            await context.Orders.AddAsync(order);
            await context.SaveChangesAsync();

            var processor = CreateProcessor();
            await processor.ProcessPendingAsync();

            await using var verificationContext = CreateContext();
            var persistedMessage = await verificationContext.OutboxMessages.AsNoTracking().SingleAsync();
            Assert.NotNull(persistedMessage.ProcessedAt);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");
        }
    }

    // Test này chứng minh Evidence Day 43: processor phải bỏ qua message đã xử lý, không đánh dấu lại `ProcessedAt` và không publish trùng trong lần xử lý thứ hai.
    [Fact]
    public async Task OutboxProcessor_SkipsAlreadyProcessed()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");

        try
        {
            var order = Order.Place(CustomerId.New(), new Address("12 Lý Thường Kiệt", "Cần Thơ", "Vietnam", "700000"));
            order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(35m, "USD")));

            await context.Orders.AddAsync(order);
            await context.SaveChangesAsync();

            var processor = CreateProcessor();
            await processor.ProcessPendingAsync();

            await using var firstVerificationContext = CreateContext();
            var firstMessage = await firstVerificationContext.OutboxMessages.AsNoTracking().SingleAsync();
            var firstProcessedAt = firstMessage.ProcessedAt;

            await processor.ProcessPendingAsync();

            await using var secondVerificationContext = CreateContext();
            var messages = await secondVerificationContext.OutboxMessages.AsNoTracking().ToListAsync();
            Assert.Single(messages);
            Assert.Equal(firstProcessedAt, messages[0].ProcessedAt);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");
        }
    }
}
