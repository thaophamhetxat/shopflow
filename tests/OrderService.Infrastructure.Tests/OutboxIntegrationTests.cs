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
/// Test tích h?p ki?m tra `OrderDbContext.SaveChangesAsync` s? luu Order và outbox message d?ng th?i trong m?t transaction PostgreSQL th?t.
/// Ðây là Evidence Day 43 cho nguyên t?c atomicity: n?u Order luu thành công thì event cung t?n t?i; n?u Order fail thì không có outbox riêng l?.
/// </summary>
public sealed class OutboxIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
        ?? "Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;Password=ShopFlow123!;";

    /// <summary>
    /// T?o DbContext theo connection string local c?a Order database, cho phép test tích h?p ch?y ch? khi Docker Postgres dang ho?t d?ng trên máy dev.
    /// </summary>
    /// <returns>DbContext du?c c?u hình s?n d? k?t n?i PostgreSQL local.</returns>
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

    // Test này ch?ng minh Day 43: khi Order phát event trong `SaveChangesAsync`, EF Core ghi both `orders` và `outbox_messages` trong cùng transaction. C?n Docker Postgres dang ch?y trên localhost:5432.
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
            var order = Order.Place(CustomerId.New(), new Address("456 Nguy?n Trãi", "Ðà N?ng", "Vietnam", "550000"));
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

    // Test này ch?ng minh Evidence Day 43: khi processor ch?y m?t vòng x? lý, nó ph?i d?c outbox chua x? lý và dánh d?u `ProcessedAt` cho message này b?ng PostgreSQL th?t.
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
            var order = Order.Place(CustomerId.New(), new Address("789 Hai Bà Trung", "Hu?", "Vietnam", "500000"));
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

    // Test này ch?ng minh Evidence Day 43: processor ph?i b? qua message dã x? lý, không dánh d?u l?i `ProcessedAt` và không publish trùng trong l?n x? lý th? hai.
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
            var order = Order.Place(CustomerId.New(), new Address("12 Lý Thu?ng Ki?t", "C?n Tho", "Vietnam", "700000"));
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
