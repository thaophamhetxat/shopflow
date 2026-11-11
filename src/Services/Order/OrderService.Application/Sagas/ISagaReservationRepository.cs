// File này định nghĩa contract lưu row `saga_reservations` cho compensate và resume.
// Layer Application giữ repository này để `OrderSaga` không phụ thuộc vào EF Core khi cần đọc danh sách item đã reserve trước đó.
// Day 53 tạo interface này để hỗ trợ idempotent compensation và tránh gọi Reserve lại cho item đã reserve từ crash trước đó.
// Day 54 có thể mở rộng thêm query theo `status` nếu cần thêm resilience mà không ảnh hưởng đến contract Day 53.

namespace OrderService.Application.Sagas;

/// <summary>
/// Repository đọc và cập nhật row `saga_reservations` để saga có thể bù trừ đúng item và không double-release hoặc double-reserve.
/// </summary>
public interface ISagaReservationRepository
{
    /// <summary>
    /// Lấy tất cả reservation của một order theo orderId để saga quyết định item nào còn cần release hoặc bỏ qua khi resume.
    /// </summary>
    /// <param name="orderId">Mã order cần đọc row reservation.</param>
    /// <param name="cancellationToken">Token hủy khi query database.</param>
    /// <returns>Danh sách row `saga_reservations` của order; rỗng khi chưa từng reserve gì.</returns>
    Task<IReadOnlyList<SagaReservation>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy một reservation cụ thể theo orderId + productId để xử lý release/skip theo item.
    /// </summary>
    /// <param name="orderId">Order chứa reservation.</param>
    /// <param name="productId">Sản phẩm cần đọc.</param>
    /// <param name="cancellationToken">Token hủy khi query database.</param>
    /// <returns>Reservation item nếu tồn tại, ngược lại null.</returns>
    Task<SagaReservation?> GetByOrderIdAndProductIdAsync(Guid orderId, Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi hoặc cập nhật một row reservation trong lần reserve mới hoặc retry sau crash.
    /// </summary>
    /// <param name="reservation">Row reservation cần persist.</param>
    /// <param name="cancellationToken">Token hủy khi lưu.</param>
    Task SaveAsync(SagaReservation reservation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật trạng thái record thành `Released` sau khi InventoryService trả về success từ endpoint release.
    /// </summary>
    /// <param name="orderId">OrderId của row cần cập nhật.</param>
    /// <param name="productId">ProductId đang được bù trừ.</param>
    /// <param name="cancellationToken">Token hủy khi update.</param>
    Task MarkReleasedAsync(Guid orderId, Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Khi reserve thành công, lưu một row mới hoặc update nếu row đã tồn tại từ crash trước đó nhưng chưa release.
    /// </summary>
    /// <param name="orderId">Order đang reserve.</param>
    /// <param name="productId">Sản phẩm được reserve.</param>
    /// <param name="quantity">Số lượng reserve.</param>
    /// <param name="cancellationToken">Token hủy khi save.</param>
    Task UpsertAsync(Guid orderId, Guid productId, int quantity, CancellationToken cancellationToken = default);
}
