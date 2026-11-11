using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderService.Infrastructure.Persistence;
using RabbitMQ.Client;

namespace OrderService.Infrastructure.Outbox;

/// <summary>
/// Publisher outbox dựa trên RabbitMQ topic exchange. Connection và channel được tạo lười trên lần publish đầu tiên để API vẫn start được
/// khi broker chưa chạy, đồng thời tránh rơi vào lỗi khởi động do RabbitMQ offline.
/// </summary>
public sealed class RabbitMqOutboxPublisher : IOutboxPublisher, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqOutboxPublisher> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private readonly string _hostName;
    private readonly int _port;
    private readonly string _userName;
    private readonly string _password;
    private readonly string _exchangeName;
    private readonly string _queueName;

    private IConnection? _connection;
    private IChannel? _channel;

    /// <summary>
    /// Khởi tạo publisher đọc RabbitMQ config tùy theo section `RabbitMQ` với giá trị mặc định an toàn khi thiếu key.
    /// </summary>
    /// <param name="configuration">Root configuration chứa section RabbitMQ.</param>
    /// <param name="logger">Logger dùng để ghi cảnh báo hoặc lỗi khi declare exchange/queue.</param>
    public RabbitMqOutboxPublisher(IConfiguration configuration, ILogger<RabbitMqOutboxPublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var rabbitMqSection = _configuration.GetSection("RabbitMQ");
        _hostName = rabbitMqSection["HostName"] ?? "localhost";
        _port = rabbitMqSection.GetValue<int?>("Port") ?? 5672;
        _userName = rabbitMqSection["UserName"] ?? "guest";
        _password = rabbitMqSection["Password"] ?? "guest";
        _exchangeName = rabbitMqSection["Exchange"] ?? "shopflow.events";
        _queueName = rabbitMqSection["Queue"] ?? "shopflow.order-events";
    }

    /// <summary>
    /// Publish 1 outbox message lên exchange topic `shopflow.events` với routing key là tên event cuối cùng.
    /// </summary>
    /// <param name="message">Outbox message chứa EventData JSON để gửi tới downstream.</param>
    /// <param name="cancellationToken">Token hủy nếu shutdown cắt ngang publish.</param>
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await EnsureConnectedAsync(cancellationToken);

        var routingKey = GetRoutingKey(message.EventType);
        var payload = Encoding.UTF8.GetBytes(message.EventData);
        var properties = new BasicProperties { Persistent = true };

        await _channel!.BasicPublishAsync(
            exchange: _exchangeName,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: properties,
            body: payload,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Đã publish event {EventType} lên exchange {ExchangeName} với routing key {RoutingKey}.",
            message.EventType,
            _exchangeName,
            routingKey);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null && _channel is not null)
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null && _channel is not null)
            {
                return;
            }

            var factory = new ConnectionFactory
            {
                HostName = _hostName,
                Port = _port,
                UserName = _userName,
                Password = _password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Topology chốt: exchange topic `shopflow.events` và queue durable `shopflow.order-events` bind với routing key `#` để nhận mọi event.
            // Tạo lại idempotent ở lần publish đầu tiên để hỗ trợ restart service mà không phá topology sẵn có.
            await _channel.ExchangeDeclareAsync(
                _exchangeName,
                ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            await _channel.QueueDeclareAsync(
                _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            await _channel.QueueBindAsync(
                _queueName,
                _exchangeName,
                routingKey: "#",
                arguments: null,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private static string GetRoutingKey(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "unknown";
        }

        var segments = eventType.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? eventType : segments[^1];
    }

    /// <summary>
    /// Đóng channel trước rồi connection sau để tránh rò rỉ broker và đảm bảo delivery/ack hoàn tất trước khi shutdown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection = null;
        }
    }
}
