// File này khai báo hợp đồng đọc projection Order ở Application layer.
// Tạo ở Day 46; Day 61 cập nhật tham số keyset thành cặp CreatedAt và OrderId.
// Infrastructure thực thi truy vấn, còn DTO tiếp tục được map ở memory theo quy tắc hybrid.

using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Queries;

/// <summary>
/// Contract cho read-model projection của Order được truy vấn trực tiếp từ store mà không cần nạp aggregate.
/// Interface này thuộc về Application layer để tránh Domain phụ thuộc vào DTO và dependency ngược chiều.
/// </summary>
public interface IOrderReadModelRepository
{
    Task<OrderDetailDto?> GetOrderDetailAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc list summary theo customer với keyset cursor; CreatedAt lọc trong SQL và OrderId làm tie-breaker ở memory.
    /// </summary>
    /// <param name="customerId">Khách hàng cần đọc danh sách.</param>
    /// <param name="cursorCreatedAt">Thời điểm của item cuối trang trước, null nếu là trang đầu.</param>
    /// <param name="cursorOrderId">OrderId của item cuối trang trước, dùng tie-breaker khi timestamp trùng.</param>
    /// <param name="pageSize">Số summary tối đa trả về.</param>
    /// <param name="cancellationToken">Token hủy truy vấn.</param>
    /// <returns>Trang OrderSummaryDto theo thứ tự CreatedAt và OrderId giảm dần.</returns>
    Task<IReadOnlyList<OrderSummaryDto>> ListOrderSummariesByCustomerAsync(
        CustomerId customerId,
        DateTime? cursorCreatedAt,
        OrderId? cursorOrderId,
        int pageSize,
        CancellationToken cancellationToken = default);
}
