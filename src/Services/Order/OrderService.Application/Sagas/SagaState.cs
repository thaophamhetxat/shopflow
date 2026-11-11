// File này chứa trạng thái lưu trữ của Saga cho từng order, dùng làm checkpoint khi processor outbox bị dừng giữa chừng.
// Layer Application định nghĩa trạng thái này để OrderSaga và infrastructure repository cùng dùng một schema chính xác cho `saga_state`.

namespace OrderService.Application.Sagas;

/// <summary>
/// Trạng thái hiện tại của một saga instance theo orderId; row này được lưu trong bảng `saga_state` để recover sau restart.
/// </summary>
public sealed class SagaState
{
    /// <summary>
    /// OrderId được đại diện dưới dạng Guid, dùng làm khóa chính trong bảng `saga_state`.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Tên trạng thái hiện tại của saga, ví dụ PendingInventory, PendingPayment, Completed, FailedInventory, FailedPayment.
    /// </summary>
    public string State { get; set; } = SagaStateStatus.PendingInventory.ToString();

    /// <summary>
    /// Thời điểm cập nhật cuối cùng của trạng thái này để dễ trace và audit.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Tạo một checkpoint saga mới với trạng thái cụ thể.
    /// </summary>
    /// <param name="orderId">OrderId cần khởi tạo checkpoint.</param>
    /// <param name="status">Trạng thái đầu tiên của saga.</param>
    /// <returns>Đối tượng SagaState đã chuẩn hóa UpdatedAt.</returns>
    public static SagaState Create(Guid orderId, SagaStateStatus status)
    {
        return new SagaState
        {
            OrderId = orderId,
            State = status.ToString(),
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Cập nhật trạng thái hiện tại của saga và thời điểm sửa mới nhất.
    /// </summary>
    /// <param name="status">Trạng thái mới cần lưu.</param>
    public void UpdateState(SagaStateStatus status)
    {
        State = status.ToString();
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Tập hợp trạng thái bắt buộc của Saga. Day 52 chỉ triển khai orchestration happy path với `PendingInventory`, `PendingPayment`, `Completed` và các trạng thái Failed tương ứng.
/// </summary>
public enum SagaStateStatus
{
    PendingInventory,
    PendingPayment,
    Completed,
    FailedInventory,
    FailedPayment
}
