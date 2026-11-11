// File này chứa orchestrator saga cho luồng OrderService, điều phối trật tự Reserve -> Pay -> Confirm trong Day 52.
// Layer Application định nghĩa contract này để OutboxProcessor và Infrastructure có thể gọi cùng một flow chủ động mà không lộ chi tiết điểm cuối HTTP.

namespace OrderService.Application.Sagas;

/// <summary>
/// Contract điều phối luồng Saga của một order theo state machine PendingInventory -> PendingPayment -> Completed.
/// </summary>
public interface IOrderSaga
{
    /// <summary>
    /// Chạy saga cho orderId đã được lưu ở outbox; nếu order không tồn tại hoặc đã xác nhận, method không làm gì thêm.
    /// </summary>
    /// <param name="orderId">OrderId cần điều phối.</param>
    /// <param name="cancellationToken">Token hủy khi request bị dừng.</param>
    /// <returns>Tác vụ hoàn tất sau khi saga xử lý xong hoặc dừng ở trạng thái Failed.</returns>
    Task RunAsync(Guid orderId, CancellationToken cancellationToken = default);
}
