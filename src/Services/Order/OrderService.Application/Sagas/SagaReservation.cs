// File này lưu trạng thái release từng item đã reserve trong saga order để phục vụ compensating transaction và resume sau crash.
// Layer Application định nghĩa schema này để `OrderSaga` và repository được dùng chung theo cùng một model trạng thái release kho.
// Day 53 tạo file này để hỗ trợ compensation idempotent và tránh double-reserve trên restart.
// Các Day sau như Day 54/55 có thể đọc `saga_reservations` để audit và recovery mà không làm phá model hiện tại.

namespace OrderService.Application.Sagas;

/// <summary>
/// Ghi nhận mỗi sản phẩm đã reserve thành công trong một order để saga có thể release lại theo cách idempotent khi xảy ra fail hoặc timeout.
/// </summary>
public sealed class SagaReservation
{
    /// <summary>
    /// Mã đơn hàng đang giữ kho cho item này.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Mã sản phẩm đã reserve trong đơn.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Số lượng từng item được giữ kho trong lần reserve này.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Trạng thái hiện tại của reservation: Reserved khi kho đang bị giữ, Released khi compensation đã release thất bại/hoàn tất.
    /// </summary>
    public SagaReservationStatus Status { get; set; } = SagaReservationStatus.Reserved;

    /// <summary>
    /// Thời điểm ghi nhận reservation đầu tiên để theo dõi cửa sổ crash hẹp và kiểm tra resume.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm update cuối cùng của row, dùng để tránh release lại khi saga retry.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Tạo row `saga_reservations` mới cho một item đã reserve thành công.
    /// </summary>
    /// <param name="orderId">Mã order đang hold stock.</param>
    /// <param name="productId">Mã sản phẩm đã reserve.</param>
    /// <param name="quantity">Số lượng reserve này.</param>
    /// <returns>Row reservation đã chuẩn hóa `created_at`/`updated_at`.</returns>
    public static SagaReservation Create(Guid orderId, Guid productId, int quantity)
    {
        var now = DateTime.UtcNow;
        return new SagaReservation
        {
            OrderId = orderId,
            ProductId = productId,
            Quantity = quantity,
            Status = SagaReservationStatus.Reserved,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Đánh dấu reservation đã được bù trừ với InventoryService.
    /// </summary>
    public void MarkReleased()
    {
        Status = SagaReservationStatus.Released;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Trạng thái của row `saga_reservations`; `Reserved` nghĩa item đang bị giữ kho còn chưa release, `Released` nghĩa đã bù trừ xong và không retry thêm.
/// </summary>
public enum SagaReservationStatus
{
    Reserved,
    Released
}
