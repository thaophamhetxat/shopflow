using Microsoft.EntityFrameworkCore;
using OrderService.Domain;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.Enums;
using OrderService.Domain.Events;
using OrderService.Domain.Exceptions;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Persistence;
using Xunit;

namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Test này chứng minh Day 55: event stream giữ source of truth để tái dựng aggregate, snapshot chỉ là tối ưu hiệu năng, còn outbox vẫn lưu cùng transaction với event store.
/// Mỗi test đều kiểm tra một mặt của event sourcing và replay từ event stream thay vì dựa trên snapshot như nguồn thực sự.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class EventStoreTests
{
    private static readonly SemaphoreSlim DatabaseLock = DatabaseTestLock.Shared;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
        ?? "Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;Password=ShopFlow123!;";

    private static OrderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new OrderDbContext(options);
    }

    private static async Task ResetDatabaseAsync(OrderDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM order_events;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM order_snapshots;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
        await context.Database.ExecuteSqlRawAsync("DELETE FROM orders;");
    }

    [Fact]
    public async Task Order_RebuiltFromEvents_HasCorrectState()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ResetDatabaseAsync(context);

            var customerId = CustomerId.New();
            var shippingAddress = new Address("123 Lê Lợi", "Hà Nội", "Vietnam", "100000");
            var order = Order.Place(customerId, shippingAddress);
            order.AddItem(new OrderItem(Guid.NewGuid(), 2, new Money(25m, "USD")));
            order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(40m, "USD")));

            context.Orders.Add(order);
            await context.SaveChangesAsync();

            order.Confirm();
            await context.SaveChangesAsync();

            var eventStore = new EventStore(context);
            var rehydrated = await eventStore.LoadAsync(order.Id);

            Assert.NotNull(rehydrated);
            Assert.Equal(order.Id, rehydrated!.Id);
            Assert.Equal(customerId, rehydrated.CustomerId);
            Assert.Equal(shippingAddress, rehydrated.ShippingAddress);
            Assert.Equal(2, rehydrated.Items.Count);
            Assert.Equal(OrderStatus.Confirmed, rehydrated.Status);
            Assert.Equal(new Money(90m, "USD"), rehydrated.Total);
            Assert.Equal(2, rehydrated.Version);
            Assert.All(rehydrated.Items, item => Assert.NotEqual(Guid.Empty, item.ProductId));
        }
        finally
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ResetDatabaseAsync(context);
            DatabaseLock.Release();
        }
    }

    [Fact]
    public async Task Order_LoadedFromSnapshot_ReplaysDeltaEvents()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ResetDatabaseAsync(context);

            var customerId = CustomerId.New();
            var shippingAddress = new Address("88 Đinh Tiên Hoàng", "Đà Nẵng", "Vietnam", "550000");
            var orderId = OrderId.New();
            var baseEvent = new OrderPlacedEvent(
                orderId,
                customerId,
                new[]
                {
                    new OrderItem(Guid.NewGuid(), 1, new Money(10m, "USD")),
                    new OrderItem(Guid.NewGuid(), 2, new Money(15m, "USD"))
                },
                shippingAddress,
                new Money(40m, "USD"),
                DateTime.UtcNow);

            var eventStore = new EventStore(context);
            var stagedEvents = new List<IDomainEvent>();
            for (var i = 0; i < 10; i++)
            {
                stagedEvents.Add(baseEvent with { OccurredOn = DateTime.UtcNow.AddMinutes(i + 1) });
            }

            stagedEvents.Add(new OrderConfirmedEvent(orderId, new Money(40m, "USD"), DateTime.UtcNow.AddMinutes(11)));
            await eventStore.AppendAsync(orderId, stagedEvents, 0, CancellationToken.None);
            await context.SaveChangesAsync();

            var loaded = await eventStore.LoadAsync(orderId);

            Assert.NotNull(loaded);
            Assert.Equal(orderId, loaded!.Id);
            Assert.Equal(OrderStatus.Confirmed, loaded.Status);
            Assert.Equal(40m, loaded.Total.Amount);
            Assert.Equal(11, loaded.Version);
        }
        finally
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ResetDatabaseAsync(context);
            DatabaseLock.Release();
        }
    }

    // Header comment tiếng Việt: test này chứng minh Evidence Day 55 cần thiết cho optimistic concurrency trong event stream.
    // Hai writer cùng ghi vào cùng stream với cùng expectedVersion 0, writer thứ hai sẽ bị chặn bởi ConcurrencyException,
    // và stream thực tế vẫn chỉ giữ đúng 1 event sau khi stale append thất bại, không tự ghi thêm bản chồng lấn.
    [Fact]
    public async Task EventStore_ConcurrentAppend_ThrowsConcurrencyException()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ResetDatabaseAsync(context);

            var orderId = OrderId.New();
            var customerId = CustomerId.New();
            var shippingAddress = new Address("12 Trần Hưng Đạo", "Hải Phòng", "Vietnam", "040000");
            var firstEvent = new OrderPlacedEvent(
                orderId,
                customerId,
                new[]
                {
                    new OrderItem(Guid.NewGuid(), 2, new Money(25m, "USD"))
                },
                shippingAddress,
                new Money(50m, "USD"),
                DateTime.UtcNow);

            var eventStore = new EventStore(context);
            await eventStore.AppendAsync(orderId, new[] { firstEvent }, 0, CancellationToken.None);
            await context.SaveChangesAsync();

            var staleEvent = firstEvent with { OccurredOn = DateTime.UtcNow.AddMinutes(1) };
            await Assert.ThrowsAsync<ConcurrencyException>(() => eventStore.AppendAsync(orderId, new[] { staleEvent }, 0, CancellationToken.None));

            var remainingEvents = await eventStore.GetEventsAsync(orderId, 0, CancellationToken.None);
            Assert.Single(remainingEvents);
            Assert.Single(await context.OrderEvents.AsNoTracking().ToListAsync());
        }
        finally
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ResetDatabaseAsync(context);
            DatabaseLock.Release();
        }
    }

    [Fact]
    public async Task Order_SaveChanges_StoresEventsAndOutboxInSameTransaction()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ResetDatabaseAsync(context);

            var order = Order.Place(CustomerId.New(), new Address("55 Hoàng Văn Thụ", "TP.HCM", "Vietnam", "700000"));
            order.AddItem(new OrderItem(Guid.NewGuid(), 3, new Money(12m, "USD")));

            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var outboxCount = await context.OutboxMessages.CountAsync();
            var eventCount = await context.OrderEvents.CountAsync();

            Assert.Equal(1, outboxCount);
            Assert.Equal(1, eventCount);
            Assert.Equal(1, order.Version);
        }
        finally
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ResetDatabaseAsync(context);
            DatabaseLock.Release();
        }
    }
}
