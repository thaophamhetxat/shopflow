// File này chứa test tích hợp cho Day 46: read model projection và keyset pagination cho OrderService.Infrastructure.
// Tạo ở Day 46 để kiểm chứng `GetOrderById` và `ListOrdersByCustomer` chạy bằng projection SQL thật trên PostgreSQL local, không dùng mock hay OFFSET.
// Ngày 47 sẽ dùng DTO này để phát triển endpoint API đọc dữ liệu order mà không lộ aggregate Domain ra ngoài.
// Day 61 bổ sung evidence cursor Base64Url, compiled-query timings và kiểm tra đúng một lệnh đọc.

using System.Diagnostics;
using InventoryService.Domain.Aggregates;
using InventoryService.Domain.ValueObjects;
using InventoryService.Infrastructure.Persistence;
using InventoryService.Infrastructure.Persistence.Repositories;
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
using Xunit.Abstractions;
using Xunit;

namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Test tích hợp Day 46 chứng minh query đọc bằng projection và keyset pagination hoạt động trên PostgreSQL thật, cần Docker Postgres đang chạy ở localhost:5432.
/// </summary>
[Collection("Infrastructure Database")]
public sealed class ReadModelProjectionTests
{
    private static readonly SemaphoreSlim DatabaseLock = DatabaseTestLock.Shared;
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Khởi tạo test class với output sink để benchmark ghi số đo thật vào kết quả test.
    /// </summary>
    /// <param name="output">Output sink của xUnit.</param>
    public ReadModelProjectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

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
                var cursor = OrderListCursor.Encode(firstPage[^1].CreatedAt, firstPage[^1].OrderId);
                var secondPage = await mediator.Send(new ListOrdersByCustomerQuery(customerId, cursor, 2));

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

    // Test này chứng minh Evidence Day 61: cursor URL-safe base64 chứa CreatedAt và OrderId, trang sau trả đúng order thứ ba.
    [Fact]
    public async Task ListOrdersByCustomer_WithBase64Cursor_ReturnsSecondPage()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();
            var idempotencyKeys = Enumerable.Range(0, 3).Select(_ => $"base64-cursor-{Guid.NewGuid():N}").ToList();

            try
            {
                await ResetDatabaseAsync(provider, idempotencyKeys.ToArray());
                var mediator = provider.GetRequiredService<IMediator>();
                var customerId = Guid.NewGuid();

                for (var index = 0; index < idempotencyKeys.Count; index++)
                {
                    await mediator.Send(new PlaceOrderCommand(
                        customerId,
                        $"{index + 1} Base64 Cursor Street",
                        "Hà Nội",
                        "Việt Nam",
                        "100000",
                        new List<PlaceOrderItem>
                        {
                            new(Guid.NewGuid(), 1, 100m + index, "USD")
                        },
                        idempotencyKeys[index]));
                    await Task.Delay(25);
                }

                var firstPage = await mediator.Send(new ListOrdersByCustomerQuery(customerId, null, 2));
                Assert.Equal(2, firstPage.Count);
                var cursor = OrderListCursor.Encode(firstPage[^1].CreatedAt, firstPage[^1].OrderId);
                Assert.DoesNotContain("+", cursor);
                Assert.DoesNotContain("/", cursor);
                Assert.DoesNotContain("=", cursor);

                var secondPage = await mediator.Send(new ListOrdersByCustomerQuery(customerId, cursor, 2));

                Assert.Single(secondPage);
                Assert.NotEqual(firstPage[^1].OrderId, secondPage[0].OrderId);

                using var scope = provider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var expectedOrderId = await dbContext.Orders
                    .Where(order => order.CustomerId == CustomerId.From(customerId))
                    .OrderByDescending(order => order.CreatedAt)
                    .ThenByDescending(order => order.Id)
                    .Select(order => order.Id.Value)
                    .Skip(2)
                    .SingleAsync();
                Assert.Equal(expectedOrderId, secondPage[0].OrderId);
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
                Assert.Single(firstPage);

                var secondCursor = OrderListCursor.Encode(firstPage[0].CreatedAt, firstPage[0].OrderId);
                var secondPage = await mediator.Send(new ListOrdersByCustomerQuery(customerId, secondCursor, 1));
                Assert.Single(secondPage);
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
                var firstPage = await readModel.ListOrderSummariesByCustomerAsync(CustomerId.From(customerId), null, null, 2);
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
                    firstPage[^1].CreatedAt,
                    OrderService.Domain.ValueObjects.OrderId.From(firstPage[^1].OrderId),
                    2);

                int secondPageCommandCount;
                lock (sqlLogs)
                {
                    secondPageCommandCount = sqlLogs.Count(message => message.Contains("Executed DbCommand", StringComparison.Ordinal));
                    _output.WriteLine(string.Join(Environment.NewLine, sqlLogs));
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

    // Test này chứng minh Evidence Day 61: hai query compiled/uncompiled trả kết quả giống nhau và ghi avg/p50 thật, không áp đặt ngưỡng cải thiện.
    [Fact]
    public async Task CompiledQueries_Benchmark_ReportsTimings()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            var provider = BuildServiceProvider();
            var idempotencyKey = $"compiled-benchmark-{Guid.NewGuid():N}";
            var productId = ProductId.New();
            var inventoryConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__InventoryDb")
                ?? throw new InvalidOperationException("Thiếu biến môi trường 'ConnectionStrings__InventoryDb' cho benchmark compiled query.");

            try
            {
                await ResetDatabaseAsync(provider, idempotencyKey);
                var mediator = provider.GetRequiredService<IMediator>();
                var orderId = await mediator.Send(new PlaceOrderCommand(
                    Guid.NewGuid(),
                    "61 Benchmark Street",
                    "Hà Nội",
                    "Việt Nam",
                    "100000",
                    new List<PlaceOrderItem>
                    {
                        new(Guid.NewGuid(), 1, 100m, "USD"),
                        new(Guid.NewGuid(), 2, 50m, "USD")
                    },
                    idempotencyKey));

                var inventoryOptions = new DbContextOptionsBuilder<InventoryDbContext>()
                    .UseNpgsql(inventoryConnectionString)
                    .Options;
                await using (var inventoryContext = new InventoryDbContext(inventoryOptions))
                {
                    await inventoryContext.Database.EnsureCreatedAsync();
                    inventoryContext.Products.Add(Product.Create(productId, "Day 61 benchmark product", 10m, "USD", 10));
                    await inventoryContext.SaveChangesAsync();
                }

                await using var compiledOrderContext = CreateLoggingDbContext(_ => { });
                await using var uncompiledOrderContext = CreateLoggingDbContext(_ => { });
                var compiledOrderRepository = new OrderRepository(compiledOrderContext);
                var compiledDetail = await compiledOrderRepository.GetOrderDetailAsync(orderId);
                var uncompiledDetail = await LoadOrderDetailUncompiledAsync(uncompiledOrderContext, orderId);

                Assert.NotNull(compiledDetail);
                Assert.NotNull(uncompiledDetail);
                Assert.Equal(uncompiledDetail!.OrderId, compiledDetail!.OrderId);
                Assert.Equal(uncompiledDetail.CustomerId, compiledDetail.CustomerId);
                Assert.Equal(uncompiledDetail.Status, compiledDetail.Status);
                Assert.Equal(uncompiledDetail.TotalAmount, compiledDetail.TotalAmount);
                Assert.Equal(uncompiledDetail.TotalCurrency, compiledDetail.TotalCurrency);
                Assert.Equal(uncompiledDetail.CreatedAt, compiledDetail.CreatedAt);
                Assert.Equal(uncompiledDetail.Items, compiledDetail.Items);

                await using var compiledInventoryContext = new InventoryDbContext(inventoryOptions);
                await using var uncompiledInventoryContext = new InventoryDbContext(inventoryOptions);
                var compiledProductRepository = new ProductRepository(compiledInventoryContext);
                var compiledStock = await compiledProductRepository.CheckStockAsync(productId, 5);
                var uncompiledStock = await uncompiledInventoryContext.Products
                    .AsNoTracking()
                    .AnyAsync(product => product.Id == productId && product.StockQuantity >= 5);
                Assert.Equal(uncompiledStock, compiledStock);

                var compiledDetailTimes = await MeasureAsync(
                    async () => await compiledOrderRepository.GetOrderDetailAsync(orderId),
                    200);
                var uncompiledDetailTimes = await MeasureAsync(
                    async () => await LoadOrderDetailUncompiledAsync(uncompiledOrderContext, orderId),
                    200);
                var compiledStockTimes = await MeasureAsync(
                    async () => await compiledProductRepository.CheckStockAsync(productId, 5),
                    1000);
                var uncompiledStockTimes = await MeasureAsync(
                    async () => await uncompiledInventoryContext.Products
                        .AsNoTracking()
                        .AnyAsync(product => product.Id == productId && product.StockQuantity >= 5),
                    1000);

                _output.WriteLine($"GetOrderDetail compiled: avg={Average(compiledDetailTimes):F3} ms, p50={Percentile50(compiledDetailTimes):F3} ms, n=200");
                _output.WriteLine($"GetOrderDetail uncompiled: avg={Average(uncompiledDetailTimes):F3} ms, p50={Percentile50(uncompiledDetailTimes):F3} ms, n=200");
                _output.WriteLine($"CheckStock compiled: avg={Average(compiledStockTimes):F3} ms, p50={Percentile50(compiledStockTimes):F3} ms, n=1000");
                _output.WriteLine($"CheckStock uncompiled: avg={Average(uncompiledStockTimes):F3} ms, p50={Percentile50(uncompiledStockTimes):F3} ms, n=1000");
            }
            finally
            {
                await using var cleanupContext = new InventoryDbContext(
                    new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(inventoryConnectionString).Options);
                await cleanupContext.Database.EnsureCreatedAsync();
                await cleanupContext.Products
                    .Where(product => product.Id == productId)
                    .ExecuteDeleteAsync();
                await ResetDatabaseAsync(provider, idempotencyKey);
                provider.Dispose();
            }
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    private static async Task<OrderDetailDto?> LoadOrderDetailUncompiledAsync(OrderDbContext dbContext, OrderId id)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(currentOrder => currentOrder.Items)
            .FirstOrDefaultAsync(currentOrder => currentOrder.Id == id);

        if (order is null)
        {
            return null;
        }

        var items = order.Items.OrderBy(item => item.ProductId).ToList();
        return new OrderDetailDto(
            order.Id.Value,
            order.CustomerId.Value,
            order.Status.ToString(),
            items.Sum(item => item.LineTotal.Amount),
            items.Select(item => item.UnitPrice.Currency).FirstOrDefault() ?? string.Empty,
            order.CreatedAt,
            items.Select(item => new OrderDetailItemDto(
                item.ProductId,
                item.Quantity,
                item.UnitPrice.Amount,
                item.UnitPrice.Currency,
                item.LineTotal.Amount)).ToList());
    }

    private static async Task<double[]> MeasureAsync(Func<Task> operation, int iterations)
    {
        var timings = new double[iterations];
        for (var index = 0; index < iterations; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            await operation();
            stopwatch.Stop();
            timings[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        return timings;
    }

    private static double Average(double[] timings)
    {
        return timings.Average();
    }

    private static double Percentile50(double[] timings)
    {
        return timings.OrderBy(value => value).ElementAt((timings.Length - 1) / 2);
    }
}
