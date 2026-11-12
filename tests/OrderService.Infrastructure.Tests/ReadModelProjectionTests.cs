// File này chứa test tích hợp cho Day 46: read model projection và keyset pagination cho OrderService.Infrastructure.
// Tạo ở Day 46 để kiểm chứng `GetOrderById` và `ListOrdersByCustomer` chạy bằng projection SQL thật trên PostgreSQL local, không dùng mock hay OFFSET.
// Ngày 47 sẽ dùng DTO này để phát triển endpoint API đọc dữ liệu order mà không lộ aggregate Domain ra ngoài.

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderService.Application;
using OrderService.Application.Commands;
using OrderService.Application.Queries;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Repositories;
using Xunit;

namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Test tích hợp Day 46 chứng minh query đọc bằng projection và keyset pagination hoạt động trên PostgreSQL thật, cần Docker Postgres đang chạy ở localhost:5432.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class ReadModelProjectionTests
{
    private static readonly SemaphoreSlim DatabaseLock = DatabaseTestLock.Shared;

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderDb"] = Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
                    ?? "Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;Password=ShopFlow123!;",
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
        });
        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    private static OrderDbContext CreateLoggingDbContext(Action<string> captureSqlLog)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
            ?? "Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;******;";
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString)
            .LogTo(
                captureSqlLog,
                new[] { DbLoggerCategory.Database.Command.Name },
                LogLevel.Information)
            .Options;

        return new OrderDbContext(options);
    }

    private static async Task ResetDatabaseAsync(IServiceProvider provider, params string[] idempotencyKeys)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        await dbContext.Database.MigrateAsync();
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM order_items;");
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM orders;");

        foreach (var key in idempotencyKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            await cache.RemoveAsync($"order:idempotency:{key}");
        }
    }

    // Test này chứng minh Evidence Day 46: GetOrderById query bằng projection trả về OrderDetailDto với đúng OrderId, Status, Total và item count khi order đã được lưu thật trong PostgreSQL.
    [Fact]
    public async Task GetOrderById_Projection_ReturnsDetailDto()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();
            var idempotencyKey = $"projection-detail-{Guid.NewGuid():N}";

            try
            {
                await ResetDatabaseAsync(provider, idempotencyKey);
                var mediator = provider.GetRequiredService<IMediator>();

                var customerId = Guid.NewGuid();
                var firstProductId = Guid.NewGuid();
                var secondProductId = Guid.NewGuid();
                var orderId = await mediator.Send(new PlaceOrderCommand(
                    customerId,
                    "12 Lê Lợi",
                    "Hà Nội",
                    "Việt Nam",
                    "100000",
                    new List<PlaceOrderItem>
                    {
                        new(firstProductId, 2, 25000m, "VND"),
                        new(secondProductId, 1, 15000m, "VND")
                    },
                    idempotencyKey));

                var detail = await mediator.Send(new GetOrderByIdQuery(orderId.Value));

                Assert.NotNull(detail);
                Assert.Equal(orderId.Value, detail!.OrderId);
                Assert.Equal(customerId, detail.CustomerId);
                Assert.Equal("Pending", detail.Status);
                Assert.Equal(65000m, detail.TotalAmount);
                Assert.Equal("VND", detail.TotalCurrency);
                Assert.Equal(2, detail.Items.Count);
                var firstItem = Assert.Single(detail.Items, item => item.ProductId == firstProductId);
                Assert.Equal(2, firstItem.Quantity);
                Assert.Equal(25000m, firstItem.UnitPriceAmount);
                Assert.Equal(50000m, firstItem.LineTotalAmount);
                var secondItem = Assert.Single(detail.Items, item => item.ProductId == secondProductId);
                Assert.Equal(1, secondItem.Quantity);
                Assert.Equal(15000m, secondItem.UnitPriceAmount);
                Assert.Equal(15000m, secondItem.LineTotalAmount);
            }
            finally
            {
                await ResetDatabaseAsync(provider, idempotencyKey);
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 46: GetOrderById query không tìm thấy đơn hàng thì trả null thay vì ném exception, giữ đúng behavior query read-only.
    [Fact]
    public async Task GetOrderById_Projection_ReturnsNull_WhenNotFound()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider);
                var mediator = provider.GetRequiredService<IMediator>();

                var result = await mediator.Send(new GetOrderByIdQuery(Guid.NewGuid()));

                Assert.Null(result);
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

    // Test này chứng minh Evidence Day 46: list order theo customer dùng keyset cursor trả đúng 2 item đầu tiên theo CreatedAt giảm dần rồi OrderId giảm dần và không trùng lặp.
    [Fact]
    public async Task ListOrders_Keyset_FirstPage_ReturnsPageSize_InOrder()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();
            var idempotencyKeys = Enumerable.Range(0, 5).Select(_ => $"page-one-{Guid.NewGuid():N}").ToList();

            try
            {
                await ResetDatabaseAsync(provider, idempotencyKeys.ToArray());
                var mediator = provider.GetRequiredService<IMediator>();
                var customerId = Guid.NewGuid();
                var expectedOrderIds = new List<Guid>();

                for (var index = 0; index < 5; index++)
                {
                    var orderId = await mediator.Send(new PlaceOrderCommand(
                        customerId,
                        $"{index + 1} Test Street",
                        "Hà Nội",
                        "Việt Nam",
                        "100000",
                        new List<PlaceOrderItem>
                        {
                            new(Guid.NewGuid(), 1, 100m + index, "USD")
                        },
                        idempotencyKeys[index]));

                    expectedOrderIds.Add(orderId.Value);
                    await Task.Delay(25);
                }

                var actualPage = await mediator.Send(new ListOrdersByCustomerQuery(customerId, null, 2));

                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var expectedOrderIdsFromDb = await dbContext.Orders
                    .Where(order => order.CustomerId == CustomerId.From(customerId))
                    .OrderByDescending(order => order.CreatedAt)
                    .ThenByDescending(order => order.Id)
                    .Select(order => order.Id.Value)
                    .Take(2)
                    .ToListAsync();

                Assert.Equal(2, actualPage.Count);
                Assert.Equal(expectedOrderIdsFromDb, actualPage.Select(dto => dto.OrderId).ToList());
            }
            finally
            {
                await ResetDatabaseAsync(provider, idempotencyKeys.ToArray());
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 46: keyset trang thứ hai lấy đúng các order tiếp theo và không chồng lấn với trang đầu khi cursor là OrderId của item cuối cùng ở trang trước.
    [Fact]
    public async Task ListOrders_Keyset_SecondPage_ReturnsNextOrders_NoOverlap()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();
            var idempotencyKeys = Enumerable.Range(0, 5).Select(_ => $"page-two-{Guid.NewGuid():N}").ToList();

            try
            {
                await ResetDatabaseAsync(provider, idempotencyKeys.ToArray());
                var mediator = provider.GetRequiredService<IMediator>();
                var customerId = Guid.NewGuid();
                var createdOrderIds = new List<Guid>();

                for (var index = 0; index < 5; index++)
                {
                    var orderId = await mediator.Send(new PlaceOrderCommand(
                        customerId,
                        $"{index + 1} Cursor Street",
                        "Đà Nẵng",
                        "Việt Nam",
                        "500000",
                        new List<PlaceOrderItem>
                        {
                            new(Guid.NewGuid(), 1, 200m + index, "USD")
                        },
                        idempotencyKeys[index]));

                    createdOrderIds.Add(orderId.Value);
                    await Task.Delay(25);
                }

                var firstPage = await mediator.Send(new ListOrdersByCustomerQuery(customerId, null, 2));
                var cursorOrderId = firstPage[^1].OrderId;
                var secondPage = await mediator.Send(new ListOrdersByCustomerQuery(customerId, cursorOrderId, 2));

                Assert.Equal(2, secondPage.Count);
                Assert.Empty(firstPage.Select(dto => dto.OrderId).Intersect(secondPage.Select(dto => dto.OrderId)));

                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var expectedOrderIdsFromDb = await dbContext.Orders
                    .Where(order => order.CustomerId == CustomerId.From(customerId))
                    .OrderByDescending(order => order.CreatedAt)
                    .ThenByDescending(order => order.Id)
                    .Select(order => order.Id.Value)
                    .Skip(2)
                    .Take(2)
                    .ToListAsync();

                Assert.Equal(expectedOrderIdsFromDb, secondPage.Select(dto => dto.OrderId).ToList());
            }
            finally
            {
                await ResetDatabaseAsync(provider, idempotencyKeys.ToArray());
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 46 fix 3: khi 2 order cùng customer có cùng CreatedAt, keyset tie-breaker theo Id vẫn giữ đúng thứ tự trang sau và không bỏ sót đơn hàng.
    [Fact]
    public async Task ListOrdersByCustomer_ReturnsSecondPage_WhenOrdersShareCreatedAt()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();
            var idempotencyKeys = Enumerable.Range(0, 2).Select(_ => $"tie-breaker-{Guid.NewGuid():N}").ToList();

            try
            {
                await ResetDatabaseAsync(provider, idempotencyKeys.ToArray());
                var mediator = provider.GetRequiredService<IMediator>();
                var customerId = Guid.NewGuid();
                var createdOrderIds = new List<Guid>();

                foreach (var idempotencyKey in idempotencyKeys)
                {
                    var orderId = await mediator.Send(new PlaceOrderCommand(
                        customerId,
                        "Shared CreatedAt Street",
                        "Hà Nội",
                        "Việt Nam",
                        "250000",
                        new List<PlaceOrderItem>
                        {
                            new(Guid.NewGuid(), 1, 100m, "VND")
                        },
                        idempotencyKey));

                    createdOrderIds.Add(orderId.Value);
                }

                var sharedCreatedAt = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                using (var scope = provider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                    foreach (var orderId in createdOrderIds)
                    {
                        await dbContext.Database.ExecuteSqlRawAsync(
                            "UPDATE orders SET created_at = {0} WHERE id = {1}",
                            sharedCreatedAt,
                            orderId);
                    }
                }

                var firstPage = await mediator.Send(new ListOrdersByCustomerQuery(customerId, null, 1));
                Assert.Equal(1, firstPage.Count);

                var secondPage = await mediator.Send(new ListOrdersByCustomerQuery(customerId, firstPage[0].OrderId, 1));
                Assert.Equal(1, secondPage.Count);
                Assert.NotEqual(firstPage[0].OrderId, secondPage[0].OrderId);
                Assert.Contains(firstPage[0].OrderId, createdOrderIds);
                Assert.Contains(secondPage[0].OrderId, createdOrderIds);
            }
            finally
            {
                await ResetDatabaseAsync(provider, idempotencyKeys.ToArray());
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    // Test này chứng minh Evidence Day 46: list order theo customer khi chưa có đơn nào phải trả list rỗng, không null và không ném exception.
    [Fact]
    public async Task ListOrders_NoOrders_ReturnsEmpty()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider);
                var mediator = provider.GetRequiredService<IMediator>();
                var customerId = Guid.NewGuid();

                var results = await mediator.Send(new ListOrdersByCustomerQuery(customerId, null, 20));

                Assert.NotNull(results);
                Assert.Empty(results);
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

    /// <summary>
    /// Evidence Day 60: xác nhận trang đầu và trang cursor của list summary đều thực thi đúng một DbCommand.
    /// </summary>
    /// <returns>Task đại diện cho test tích hợp PostgreSQL.</returns>
    [Fact]
    public async Task ListOrdersByCustomer_ExecutesSingleQuery()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var sqlLogs = new List<string>();
            var idempotencyKeys = Enumerable.Range(0, 3).Select(_ => $"single-query-list-{Guid.NewGuid():N}").ToList();
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider, idempotencyKeys.ToArray());
                var mediator = provider.GetRequiredService<IMediator>();
                var customerId = Guid.NewGuid();

                foreach (var idempotencyKey in idempotencyKeys)
                {
                    await mediator.Send(new PlaceOrderCommand(
                        customerId,
                        "60 Query Street",
                        "Hà Nội",
                        "Việt Nam",
                        "100000",
                        new List<PlaceOrderItem>
                        {
                            new(Guid.NewGuid(), 1, 100m, "USD"),
                            new(Guid.NewGuid(), 2, 50m, "USD")
                        },
                        idempotencyKey));
                }

                await using var dbContext = CreateLoggingDbContext(message =>
                {
                    lock (sqlLogs)
                    {
                        sqlLogs.Add(message);
                    }
                });
                IOrderReadModelRepository readModel = new OrderRepository(dbContext);

                lock (sqlLogs)
                {
                    sqlLogs.Clear();
                }
                var firstPage = await readModel.ListOrderSummariesByCustomerAsync(CustomerId.From(customerId), null, 2);
                int firstPageCommandCount;
                lock (sqlLogs)
                {
                    firstPageCommandCount = sqlLogs.Count(message => message.Contains("Executed DbCommand", StringComparison.Ordinal));
                    sqlLogs.Clear();
                }

                Assert.Equal(2, firstPage.Count);
                Assert.Equal(1, firstPageCommandCount);

                var secondPage = await readModel.ListOrderSummariesByCustomerAsync(
                    CustomerId.From(customerId),
                    OrderService.Domain.ValueObjects.OrderId.From(firstPage[^1].OrderId),
                    2);

                int secondPageCommandCount;
                lock (sqlLogs)
                {
                    secondPageCommandCount = sqlLogs.Count(message => message.Contains("Executed DbCommand", StringComparison.Ordinal));
                }

                Assert.Single(secondPage);
                Assert.Equal(1, secondPageCommandCount);
            }
            finally
            {
                await ResetDatabaseAsync(provider, idempotencyKeys.ToArray());
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    /// <summary>
    /// Evidence Day 60: xác nhận đọc chi tiết order cùng nhiều item chỉ thực thi đúng một DbCommand.
    /// </summary>
    /// <returns>Task đại diện cho test tích hợp PostgreSQL.</returns>
    [Fact]
    public async Task GetOrderDetail_ExecutesSingleQuery()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var sqlLogs = new List<string>();
            var idempotencyKey = $"single-query-detail-{Guid.NewGuid():N}";
            var provider = BuildServiceProvider();

            try
            {
                await ResetDatabaseAsync(provider, idempotencyKey);
                var mediator = provider.GetRequiredService<IMediator>();
                var orderId = await mediator.Send(new PlaceOrderCommand(
                    Guid.NewGuid(),
                    "60 Detail Street",
                    "Hà Nội",
                    "Việt Nam",
                    "100000",
                    new List<PlaceOrderItem>
                    {
                        new(Guid.NewGuid(), 1, 100m, "USD"),
                        new(Guid.NewGuid(), 2, 50m, "USD"),
                        new(Guid.NewGuid(), 3, 25m, "USD")
                    },
                    idempotencyKey));

                await using var dbContext = CreateLoggingDbContext(message =>
                {
                    lock (sqlLogs)
                    {
                        sqlLogs.Add(message);
                    }
                });
                IOrderReadModelRepository readModel = new OrderRepository(dbContext);
                lock (sqlLogs)
                {
                    sqlLogs.Clear();
                }

                var detail = await readModel.GetOrderDetailAsync(orderId);

                int commandCount;
                lock (sqlLogs)
                {
                    commandCount = sqlLogs.Count(message => message.Contains("Executed DbCommand", StringComparison.Ordinal));
                }

                Assert.NotNull(detail);
                Assert.Equal(3, detail!.Items.Count);
                Assert.Equal(1, commandCount);
            }
            finally
            {
                await ResetDatabaseAsync(provider, idempotencyKey);
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }
}
