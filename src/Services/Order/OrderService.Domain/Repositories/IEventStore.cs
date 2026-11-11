using OrderService.Domain.Aggregates;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Repositories;

/// <summary>
/// Contract event store cho OrderService: lưu event stream, rehydrate aggregate từ stream và phát hiện race condition khi writer khác cùng lúc append cùng version.
/// Event stream là source of truth theo ADR-001, snapshot chỉ là tối ưu hiệu năng và không được dùng để quyết định trạng thái nghiệp vụ cuối cùng.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Stage một loạt event mới vào stream của order với expected version cho trước. Method này chỉ ghi row staging, không tự gọi SaveChanges.
    /// </summary>
    /// <param name="orderId">Id của order cần append event.</param>
    /// <param name="events">Danh sách event mới theo đúng thứ tự xảy ra.</param>
    /// <param name="expectedVersion">Version hiện tại của stream trước khi append.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    Task AppendAsync(OrderId orderId, IReadOnlyCollection<IDomainEvent> events, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tải aggregate Order từ snapshot mới nhất kèm replay các event còn thiếu, trả về null nếu stream rỗng.
    /// </summary>
    Task<Order?> LoadAsync(OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc các event của stream bắt đầu từ version cụ thể, trả về danh sách sắp xếp tăng dần theo version.
    /// </summary>
    Task<IReadOnlyList<IDomainEvent>> GetEventsAsync(OrderId orderId, int fromVersion, CancellationToken cancellationToken = default);
}
