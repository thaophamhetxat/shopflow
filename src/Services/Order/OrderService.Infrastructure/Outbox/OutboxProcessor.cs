// File này chứa processor nền cho outbox của OrderService, tách producer poll DB khỏi consumer publish và chuẩn bị cho Day 52/57 nâng cấp transport và graceful shutdown.
// Layer Infrastructure này sẽ chạy như background service để đọc message chưa xử lý, publish ra RabbitMQ và đánh dấu ProcessedAt trong database.
// Day 61 gom các ID đã publish thành batch và flush ProcessedAt bằng một UPDATE cuối chu kỳ.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Application.Sagas;
using OrderService.Infrastructure.Persistence;
using ShopFlow.BuildingBlocks;

namespace OrderService.Infrastructure.Outbox;

/// <summary>
/// Bộ đếm metric cho OutboxProcessor: `ProcessedCount` tăng theo từng message xử lý xong, `LastLag` đo độ trễ từ thời điểm sự kiện ra đời đến khi publish hoàn tất.
/// </summary>
public sealed class OutboxProcessorMetrics
{
    private long _processedCount;

    /// <summary>
    /// Số message đã được publish thành công và lưu `ProcessedAt` trong DB.
    /// </summary>
    public long ProcessedCount => Interlocked.Read(ref _processedCount);

    /// <summary>
    /// Độ trễ gần nhất của message mới xử lý xong, tính bằng `DateTime.UtcNow - OccurredOn`.
    /// </summary>
    public TimeSpan LastLag { get; private set; }

    /// <summary>
    /// Ghi nhận message vừa xử lý thành công. Chỉ dùng `Interlocked.Increment` để tránh race condition khi nhiều consumer cùng chạy.
    /// </summary>
    /// <param name="occurredOn">Thời điểm sự kiện được ghi trong outbox.</param>
    public void RecordProcessed(DateTime occurredOn)
    {
        Interlocked.Increment(ref _processedCount);
        LastLag = DateTime.UtcNow - occurredOn;
    }
}

/// <summary>
/// Background service đọc message chưa xử lý từ bảng outbox, đẩy sang channel có giới hạn để consumer publish theo hàng đợi và giới hạn concurrency bằng `SemaphoreSlim`.
/// Day 52 mở rộng worker này: khi gặp `OrderPlacedEvent`, nó gọi trực tiếp `OrderSaga` của Order Service để điều phối Reserve -> Pay -> Confirm mà không nối RabbitMQ.
/// Day 57 đổi sang producer/consumer thật với channel bounded + broker RabbitMQ và graceful shutdown drain.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    public const int ChannelCapacity = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxPublisher _publisher;
    private readonly Channel<OutboxMessage> _channel;
    private readonly ConcurrentDictionary<Guid, byte> _queuedMessageIds = new();
    private readonly SemaphoreSlim _publishSemaphore;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Khởi tạo processor với scope factory để mỗi message được publish trong scope/DbContext riêng, tránh dùng DbContext thread-safe lần lượt trong producer và consumer.
    /// Channel bounded 100 làm backpressure: nếu broker chậm, producer sẽ xếp hàng đợi chờ, tránh OOM vì consumer không kịp xử lý.
    /// </summary>
    /// <param name="scopeFactory">Factory tạo scope cho mỗi lần poll và publish message.</param>
    /// <param name="publisher">Publisher thực tế dùng để gửi message đến broker hoặc transport khác.</param>
    /// <param name="logger">Logger dùng để ghi thông tin về quá trình publish outbox trong dev/test.</param>
    public OutboxProcessor(IServiceScopeFactory scopeFactory, IOutboxPublisher publisher, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _logger = logger;
        _channel = Channel.CreateBounded<OutboxMessage>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _publishSemaphore = new SemaphoreSlim(3);
        Metrics = new OutboxProcessorMetrics();
    }

    /// <summary>
    /// Metric cho phép test kiểm tra số message xử lý xong và độ trễ gần nhất của message just-processed.
    /// </summary>
    public OutboxProcessorMetrics Metrics { get; }

    /// <summary>
    /// Chạy producer và consumer song song: producer poll DB mỗi 5s, consumer đọc channel và publish từng message với semaphore giới hạn concurrency.
    /// Khi host shutdown, producer dừng poll, channel được complete để consumer drain hết message còn lại trước khi thoát; không mất message trong queue.
    /// </summary>
    /// <param name="stoppingToken">Token hủy do host yêu cầu dừng service.</param>
    /// <returns>Task chạy hết vòng đời worker nền; host sẽ chờ worker kết thúc khi shutdown.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var producerTask = Task.Run(async () =>
        {
            try
            {
                await ProduceAsync(stoppingToken);
            }
            finally
            {
                _channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        var consumerTask = ConsumeAsync(stoppingToken);

        await Task.WhenAll(producerTask, consumerTask);
    }

    /// <summary>
    /// Thực hiện một vòng poll: đọc tối đa 10 message chưa xử lý, đưa vào channel với backpressure nếu channel đã đầy.
    /// Semantics vẫn giữ nguyên như cũ cho test public API: message được đọc từ DB, queue sẽ đẩy vào channel rồi được drain ngay bằng `DrainChannelAsync`.
    /// </summary>
    /// <param name="cancellationToken">Token hủy của vòng xử lý hiện tại.</param>
    /// <returns>Task hoàn tất sau khi tất cả message trong vòng này đã được publish và đánh dấu xử lý.</returns>
    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        await ProduceOnceAsync(cancellationToken);
        await DrainChannelAsync(cancellationToken);
    }

    private async Task ProduceAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProduceOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Không thể poll outbox message trong producer.");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProduceOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var queuedMessageIds = _queuedMessageIds.Keys.ToArray();
        var pendingMessages = await dbContext.OutboxMessages
            .Where(x => x.ProcessedAt == null)
            .Where(x => !queuedMessageIds.Contains(x.Id))
            .OrderBy(x => x.OccurredOn)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var message in pendingMessages)
        {
            if (!_queuedMessageIds.TryAdd(message.Id, 0))
            {
                continue;
            }

            var enqueued = false;
            try
            {
                if (_channel.Writer.TryWrite(message))
                {
                    enqueued = true;
                    continue;
                }

                _logger.LogWarning(
                    "channel full: capacity {Capacity} reached while buffering event {EventType}; waiting until consumer drains to preserve backpressure and avoid OOM.",
                    ChannelCapacity,
                    message.EventType);
                await _channel.Writer.WriteAsync(message, cancellationToken);
                enqueued = true;
            }
            finally
            {
                if (!enqueued)
                {
                    _queuedMessageIds.TryRemove(message.Id, out _);
                }
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(stoppingToken))
            {
                var processedIds = new List<Guid>();
                var attemptedIds = new List<Guid>();
                try
                {
                    while (_channel.Reader.TryRead(out var message))
                    {
                        attemptedIds.Add(message.Id);
                        await _publishSemaphore.WaitAsync(stoppingToken);
                        try
                        {
                            if (await PublishAsync(message, stoppingToken))
                            {
                                processedIds.Add(message.Id);
                            }
                        }
                        finally
                        {
                            _publishSemaphore.Release();
                        }
                    }
                }
                finally
                {
                    var flushed = false;
                    try
                    {
                        await FlushProcessedAsync(processedIds, CancellationToken.None);
                        flushed = true;
                    }
                    finally
                    {
                        foreach (var messageId in attemptedIds)
                        {
                            if (flushed || !processedIds.Contains(messageId))
                            {
                                _queuedMessageIds.TryRemove(messageId, out _);
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Khi shutdown phát sinh cancellation, consumer vẫn phải drain channel bằng token không hủy để hoàn tất publish cho các message còn lại.
            await DrainChannelAsync(CancellationToken.None);
        }
    }

    private async Task DrainChannelAsync(CancellationToken cancellationToken)
    {
        var processedIds = new List<Guid>();
        var attemptedIds = new List<Guid>();
        try
        {
            while (_channel.Reader.TryRead(out var message))
            {
                attemptedIds.Add(message.Id);
                await _publishSemaphore.WaitAsync(cancellationToken);
                try
                {
                    if (await PublishAsync(message, cancellationToken))
                    {
                        processedIds.Add(message.Id);
                    }
                }
                finally
                {
                    _publishSemaphore.Release();
                }
            }
        }
        finally
        {
            var flushed = false;
            try
            {
                await FlushProcessedAsync(processedIds, CancellationToken.None);
                flushed = true;
            }
            finally
            {
                foreach (var messageId in attemptedIds)
                {
                    if (flushed || !processedIds.Contains(messageId))
                    {
                        _queuedMessageIds.TryRemove(messageId, out _);
                    }
                }
            }
        }
    }

    private async Task<int> FlushProcessedAsync(IReadOnlyCollection<Guid> processedIds, CancellationToken cancellationToken)
    {
        if (processedIds.Count == 0)
        {
            return 0;
        }

        var processedAt = DateTime.UtcNow;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        // Crash sau publish/saga nhưng trước flush sẽ khiến message được phát lại; at-least-once đã là semantics từ Day 57, còn saga bỏ qua lần chạy lại nhờ guard Completed của Day 52.
        return await dbContext.OutboxMessages
            .Where(message => processedIds.Contains(message.Id) && message.ProcessedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.ProcessedAt, processedAt), cancellationToken);
    }

    private async Task<bool> PublishAsync(OutboxMessage message, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            var persistedMessage = await dbContext.OutboxMessages
                .FirstOrDefaultAsync(x => x.Id == message.Id, stoppingToken);

            if (persistedMessage is null || persistedMessage.ProcessedAt is not null)
            {
                return false;
            }

            using (var activity = ShopFlowActivitySource.Source.StartActivity(ShopFlowActivitySource.OutboxProcess))
            {
                // Span này là root span vì trace context của request gốc không được lưu trong outbox_messages; schema Day 64 không đổi nên không có traceparent row.
                // Khi message bị publish từ hàng đợi sau nhiều phút, ta không thể nối trace về POST gốc ban đầu, nên span phải đứng độc lập ở đầu xử lý mỗi message.
                activity?.SetTag("outbox.message_id", persistedMessage.Id.ToString());
                activity?.SetTag("outbox.event_type", persistedMessage.EventType);

                // Đẩy message to broker trước khi trigger saga và set ProcessedAt: nếu broker lỗi, message vẫn còn ở DB và sẽ được poll lại sau.
                // Cách này đảm bảo at-least-once: không có message nào mất chỉ vì publish broker tạm thời down.
                await _publisher.PublishAsync(persistedMessage, stoppingToken);

                if (persistedMessage.EventType.EndsWith("OrderPlacedEvent", StringComparison.Ordinal))
                {
                    var orderSaga = scope.ServiceProvider.GetService<IOrderSaga>();
                    if (orderSaga is not null && Guid.TryParse(persistedMessage.AggregateId, out var orderId))
                    {
                        await orderSaga.RunAsync(orderId, stoppingToken);
                    }
                    else if (orderSaga is null)
                    {
                        _logger.LogDebug(
                            "Bỏ qua trigger OrderSaga cho {EventType} vì không có IOrderSaga được đăng ký trong container.",
                            persistedMessage.EventType);
                    }
                }
            }

            Metrics.RecordProcessed(persistedMessage.OccurredOn);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Khi shutdown, message chưa vào batch flush nên vẫn được poll lại để giữ semantics at-least-once.
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Không thể publish message outbox {MessageId}.", message.Id);
            return false;
        }
    }
}
