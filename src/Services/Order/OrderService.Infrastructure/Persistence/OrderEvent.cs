namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// Bản ghi event trong stream của order, lưu cùng version để optimistic concurrency và replay lại dễ dàng hơn.
/// Bảng `order_events` là backstop DB cho event sourcing: mỗi order có stream theo key `(order_id, version)` và mỗi version chỉ xuất hiện đúng 1 lần.
/// </summary>
public sealed class OrderEvent
{
    /// <summary>
    /// Mã order mà sự kiện thuộc về.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Version của event trong stream, bắt đầu từ 1 cho event đầu tiên của order.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Tên kiểu đầy đủ của event để deserialize khi replay lại stream.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Payload JSON đã serialize cùng với event, dùng lại cho rehydrate, snapshot và audit.
    /// </summary>
    public string EventData { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm sự kiện xảy ra trong domain, giúp sắp xếp event và phục vụ lịch sử.
    /// </summary>
    public DateTime OccurredOn { get; set; }
}
