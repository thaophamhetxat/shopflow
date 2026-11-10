// File này chứa message outbox được lưu trong cùng transaction với Order để Day 43 có thể đảm bảo event không bị mất giữa lưu Order và publish downstream.
// Layer Infrastructure này giữ dữ liệu outbox cho processor nền, và Day 45/52/57 sẽ sử dụng nó để thêm idempotency, saga và refactor processor.

using System.Text.Json;

namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// Message chờ xử lý trong outbox của OrderService; mỗi record tương ứng với một domain event của aggregate Order vừa được lưu thành công.
/// Message ở trạng thái chưa xử lý khi `ProcessedAt` null và được processor nền lấy ra để publish đi các bước tiếp theo.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>
    /// Mã định danh của message, dùng như khóa chính trong bảng outbox_messages để tránh trùng và dễ theo dõi từng event.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Tên aggregate phát ra sự kiện, ví dụ Order, để processor và lưu trữ biết đây là event của bounded context nào.
    /// </summary>
    public string AggregateType { get; set; } = string.Empty;

    /// <summary>
    /// Mã định danh của aggregate trong event để downstream biết event thuộc order nào.
    /// </summary>
    public string AggregateId { get; set; } = string.Empty;

    /// <summary>
    /// Tên kiểu domain event đầy đủ, được dùng để deserialize hoặc log khi processor publish message.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung JSON của sự kiện đã được serialize, cho phép processor publish lại mà không cần truy cập aggregate trực tiếp.
    /// </summary>
    public string EventData { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm sự kiện xảy ra trong domain, để thứ tự xử lý và audit rõ ràng hơn.
    /// </summary>
    public DateTime OccurredOn { get; set; }

    /// <summary>
    /// Thời điểm message đã được publish thành công. `null` nghĩa là chưa xử lý, khác null nghĩa là đã xử lý và không publish lại.
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Tạo một message outbox từ object domain event đang được rao trong aggregate.
    /// </summary>
    /// <param name="aggregateId">Mã định danh của aggregate, ví dụ `OrderId` dưới dạng string.</param>
    /// <param name="aggregateType">Tên aggregate gốc, thường là `Order`.</param>
    /// <param name="domainEvent">Sự kiện domain cần lưu để publish sau.</param>
    /// <returns>OutboxMessage đã có ID, metadata và payload JSON đầy đủ.</returns>
    public static OutboxMessage Create(string aggregateId, string aggregateType, object domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            AggregateId = aggregateId,
            AggregateType = aggregateType,
            EventType = domainEvent.GetType().FullName ?? domainEvent.GetType().Name,
            EventData = JsonSerializer.Serialize(domainEvent),
            OccurredOn = DateTime.UtcNow,
            ProcessedAt = null
        };
    }
}
