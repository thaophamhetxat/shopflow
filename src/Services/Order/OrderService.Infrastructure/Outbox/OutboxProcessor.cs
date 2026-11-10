// File này chứa processor nền cho outbox của OrderService, tách producer poll DB khỏi consumer publish và chuẩn bị cho Day 52/57 nâng cấp transport và graceful shutdown.
// Layer Infrastructure này sẽ chạy như background service để đọc message chưa xử lý, publish log giả và đánh dấu ProcessedAt trong database.

using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Outbox;

/// <summary>
/// Background service đọc message chưa xử lý từ bảng outbox, đẩy sang channel để consumer publish theo hàng đợi và giới hạn concurrency bằng SemaphoreSlim.
/// Processor này là worker nền của Day 43: nó không gửi message sang RabbitMQ thật, chỉ ghi log và xác nhận đã xử lý bằng `ProcessedAt`.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<OutboxMessage> _channel;
    private readonly SemaphoreSlim _publishSemaphore;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Khởi tạo processor với scope factory để mỗi message được publish trong scope/DbContext riêng, tránh dùng DbContext thread-safe lần lượt trong producer và consumer.
    /// </summary>
    /// <param name="scopeFactory">Factory tạo scope cho mỗi lần poll và publish message.</param>
    /// <param name="logger">Logger dùng để ghi thông tin về quá trình publish outbox trong dev/test.</param>
    public OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateUnbounded<OutboxMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _publishSemaphore = new SemaphoreSlim(3);
    }

    /// <summary>
    /// Chạy vòng đời của processor: mỗi lần poll sẽ lấy tối đa 10 outbox message chưa xử lý, đưa vào channel và xử lý theo từng bản ghi trước khi lặp lại.
    /// </summary>
    /// <param name="stoppingToken">Token hủy do host yêu cầu dừng service.</param>
    /// <returns>Task chạy hết vòng đời worker nền; host sẽ chờ worker kết thúc khi shutdown.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Không thể xử lý outbox message trong vòng lặp processor.");
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

    /// <summary>
    /// Thực hiện một vòng xử lý outbox: đọc tối đa 10 message chưa xử lý, đưa vào channel và đồng bộ xử lý từng message bằng `PublishAsync` trong scope/DbContext riêng.
    /// Mỗi message chỉ được đánh dấu `ProcessedAt` khi đọc lại từ DB và vẫn còn `ProcessedAt == null`, để tránh publish trùng trong trường hợp race condition.
    /// </summary>
    /// <param name="cancellationToken">Token hủy của vòng xử lý hiện tại.</param>
    /// <returns>Task hoàn tất sau khi tất cả message trong vòng này đã được publish và đánh dấu xử lý.</returns>
    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var pendingMessages = await dbContext.OutboxMessages
            .Where(x => x.ProcessedAt == null)
            .OrderBy(x => x.OccurredOn)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
        {
            return;
        }

        foreach (var message in pendingMessages)
        {
            await _channel.Writer.WriteAsync(message, cancellationToken);
        }

        var publishTasks = new List<Task>();
        for (var i = 0; i < pendingMessages.Count; i++)
        {
            var message = await _channel.Reader.ReadAsync(cancellationToken);
            await _publishSemaphore.WaitAsync(cancellationToken);
            publishTasks.Add(PublishAsync(message, cancellationToken));
        }

        await Task.WhenAll(publishTasks);
    }

    private async Task PublishAsync(OutboxMessage message, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            var persistedMessage = await dbContext.OutboxMessages
                .FirstOrDefaultAsync(x => x.Id == message.Id, stoppingToken);

            if (persistedMessage is null || persistedMessage.ProcessedAt is not null)
            {
                return;
            }

            // Day 43 publish = ghi log + đánh dấu ProcessedAt, CHƯA publish sang RabbitMQ thật (Day 52 mới nối broker): đừng tưởng message đã tới service khác.
            _logger.LogInformation(
                "Đã publish message outbox {EventType} cho aggregate {AggregateType}/{AggregateId}.",
                persistedMessage.EventType,
                persistedMessage.AggregateType,
                persistedMessage.AggregateId);

            persistedMessage.ProcessedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Dừng service khi shutdown và không tiếp tục xử lý thêm message khỏi queue.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Không thể publish message outbox {MessageId}.", message.Id);
        }
        finally
        {
            _publishSemaphore.Release();
        }
    }
}
