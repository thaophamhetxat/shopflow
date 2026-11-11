// File này định nghĩa repository trạng thái saga để lưu tiến độ của từng order khi outbox trigger đang chạy.
// Layer Application cần contract này để `OrderSaga` biết trạng thái hiện tại mà không phụ thuộc vào EF Core hay Redis cụ thể.

namespace OrderService.Application.Sagas;

/// <summary>
/// Repository lưu trạng thái saga theo orderId, dùng để recover sau khi process bị ngắt giữa chừng.
/// </summary>
public interface ISagaStateRepository
{
    /// <summary>
    /// Đọc trạng thái hiện tại của order này, hoặc null nếu saga chưa được tạo.
    /// </summary>
    /// <param name="orderId">OrderId cần đọc state.</param>
    /// <param name="cancellationToken">Token hủy khi đọc DB.</param>
    /// <returns>Đối tượng SagaState nếu có, ngược lại null.</returns>
    Task<SagaState?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc trạng thái một order theo tên method khác để gọi từ `OrderSaga` khi recovery hoặc resume.
    /// </summary>
    Task<SagaState?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu một trạng thái mới hoặc ghi đè trạng thái hiện hữu cho order này.
    /// </summary>
    Task SaveAsync(SagaState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật nhanh trạng thái cho orderId này mà không cần đọc entity nữa.
    /// </summary>
    Task UpsertAsync(Guid orderId, SagaStateStatus status, CancellationToken cancellationToken = default);
}
