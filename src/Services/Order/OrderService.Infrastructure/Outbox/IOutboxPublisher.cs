using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Outbox;

/// <summary>
/// Contract để publish 1 message outbox ra transport thực tế. Day 57 dùng RabbitMQ nhưng hành vi ở interface vẫn tách khỏi processor,
/// giúp bất kỳ transport nào cũng có thể thay thế mà không làm đổi logic lưu trữ và retry của outbox worker.
/// </summary>
public interface IOutboxPublisher
{
    /// <summary>
    /// Publish message ra broker hoặc transport khác. Nếu publish thất bại, exception được ném lên để processor giữ message ở trạng thái chưa xử lý và retry sau.
    /// </summary>
    /// <param name="message">Message outbox cần gửi đi.</param>
    /// <param name="cancellationToken">Token hủy trong trường hợp shutdown hoặc timeout.</param>
    /// <returns>Task hoàn tất khi transport đã ghi thành công.</returns>
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
