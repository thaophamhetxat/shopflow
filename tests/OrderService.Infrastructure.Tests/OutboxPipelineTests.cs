// File này chứa evidence Day 57 cho producer/consumer pipeline với bounded channel và RabbitMQ mock,
// để kiểm tra backpressure, graceful shutdown và metric của OutboxProcessor trong môi trường DB thật không dùng InMemory.
// Layer Test dùng cùng `OrderDbContext` real như OutboxIntegrationTests để đảm bảo mỗi scenario phản ánh hành vi thực tế từ PostgreSQL.

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderService.Infrastructure.Outbox;
using OrderService.Infrastructure.Persistence;
using Xunit;

namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Fake publisher dùng trong test Day 57 để ghi lại tất cả message đã publish và cho phép khởi chặn publish bằng TCS để test backpressure/channel full.
/// </summary>
public sealed class FakeOutboxPublisher : IOutboxPublisher
{
    private readonly object _sync = new();

    /// <summary>
    /// Ghi lại message đã publish; dùng lock để thread-safe khi producer/consumer chạy cùng lúc.
    /// </summary>
    public List<OutboxMessage> PublishedMessages { get; } = [];

    /// <summary>
    /// TCS cho phép test chặn publish ở message đầu tiên rồi release sau khi verify warning channel full.
    /// </summary>
    public TaskCompletionSource<object?>? PublishGate { get; set; }

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_sync)
        {
            PublishedMessages.Add(message);
        }

        if (PublishGate is not null)
        {
            await PublishGate.Task.WaitAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Logger provider dùng để thu log từ processor trong test channel-full. Dùng `List<string>` lock-protected để assert log có chuỗi `channel full`.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_sync)
            {
                return _messages.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages, _sync);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<string> messages, object sync) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sync)
            {
                messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

public sealed class OutboxPipelineTests
{
    private static readonly SemaphoreSlim DatabaseLock = DatabaseTestLock.Shared;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb")
        ?? "Host=localhost;Port=5432;Database=shopflow_order;Username=shopflow;Password=shopflow;Include Error Detail=true;";

    private static OrderDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new OrderDbContext(options);
    }

    private static async Task SeedOutboxMessagesAsync(OrderDbContext context, int count)
    {
        var messages = Enumerable.Range(1, count)
            .Select(index => new OutboxMessage
            {
                Id = Guid.NewGuid(),
                AggregateType = "Order",
                AggregateId = $"order-{index}",
                EventType = index == 1 ? "OrderService.Domain.Events.OrderPlacedEvent" : "OrderService.Domain.Events.OrderUpdatedEvent",
                EventData = $@"{{""orderId"":""order-{index}""}}",
                OccurredOn = DateTime.UtcNow.AddMinutes(-count + index),
                ProcessedAt = null
            })
            .ToList();

        context.OutboxMessages.AddRange(messages);
        await context.SaveChangesAsync();
    }

    private static async Task ClearOutboxAsync(OrderDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages;");
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Condition not met within {timeout}.");
    }

    private static async Task WaitForPublishedCountAsync(FakeOutboxPublisher publisher, int expected, TimeSpan timeout)
    {
        await WaitForConditionAsync(() => publisher.PublishedMessages.Count >= expected, timeout);
    }

    /// <summary>
    /// Test này chứng minh graceful shutdown không mất message: sau khi cancel host, processor vẫn drain channel và lưu `ProcessedAt` cho toàn bộ 5 message.
    /// </summary>
    [Fact]
    public async Task GracefulShutdown_DoesNotLoseMessages()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ClearOutboxAsync(context);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(ConnectionString));
            var provider = services.BuildServiceProvider();
            var publisher = new FakeOutboxPublisher();
            var processor = new OutboxProcessor(
                provider.GetRequiredService<IServiceScopeFactory>(),
                publisher,
                provider.GetRequiredService<ILogger<OutboxProcessor>>());

            await SeedOutboxMessagesAsync(context, 5);
            await processor.StartAsync(CancellationToken.None);
            await WaitForPublishedCountAsync(publisher, 5, TimeSpan.FromSeconds(10));

            await processor.StopAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var persistedMessages = await verificationContext.OutboxMessages.AsNoTracking().ToListAsync();
            Assert.Equal(5, persistedMessages.Count);
            Assert.All(persistedMessages, message => Assert.NotNull(message.ProcessedAt));
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    /// <summary>
    /// Test này chứng minh khi channel đầy (capacity 100), producer sẽ log `channel full` và chờ consumer drain theo backpressure.
    /// </summary>
    [Fact]
    public async Task ChannelFull_LogsWarning_WhenAtCapacity()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ClearOutboxAsync(context);

            var loggerProvider = new CapturingLoggerProvider();
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddProvider(loggerProvider));
            services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(ConnectionString));
            var provider = services.BuildServiceProvider();

            var publisher = new FakeOutboxPublisher
            {
                PublishGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            var processor = new OutboxProcessor(
                provider.GetRequiredService<IServiceScopeFactory>(),
                publisher,
                provider.GetRequiredService<ILogger<OutboxProcessor>>());

            await SeedOutboxMessagesAsync(context, 105);
            await processor.StartAsync(CancellationToken.None);

            try
            {
                await WaitForConditionAsync(
                    () => loggerProvider.Messages.Any(message => message.Contains("channel full", StringComparison.OrdinalIgnoreCase)),
                    TimeSpan.FromSeconds(180));

                Assert.Contains(
                    loggerProvider.Messages,
                    message => message.Contains("channel full", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                publisher.PublishGate?.SetResult(null);
                await WaitForConditionAsync(() => publisher.PublishedMessages.Count >= 105, TimeSpan.FromSeconds(180));
                await processor.StopAsync(CancellationToken.None);
            }

            await using var verificationContext = CreateContext();
            var persistedMessages = await verificationContext.OutboxMessages.AsNoTracking().ToListAsync();
            Assert.Equal(105, persistedMessages.Count);
            Assert.All(persistedMessages, message => Assert.NotNull(message.ProcessedAt));
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    /// <summary>
    /// Test này chứng minh `ProcessedCount` tăng đúng sau mỗi message thành công và `LastLag` luôn không âm.
    /// </summary>
    [Fact]
    public async Task ProcessedCount_Increments_AfterEachMessage()
    {
        await DatabaseLock.WaitAsync();
        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            await ClearOutboxAsync(context);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<OrderDbContext>(options => options.UseNpgsql(ConnectionString));
            var provider = services.BuildServiceProvider();
            var publisher = new FakeOutboxPublisher();
            var processor = new OutboxProcessor(
                provider.GetRequiredService<IServiceScopeFactory>(),
                publisher,
                provider.GetRequiredService<ILogger<OutboxProcessor>>());

            await SeedOutboxMessagesAsync(context, 3);
            await processor.ProcessPendingAsync();

            Assert.Equal(3, processor.Metrics.ProcessedCount);
            Assert.True(processor.Metrics.LastLag >= TimeSpan.Zero);
        }
        finally
        {
            DatabaseLock.Release();
        }
    }
}
