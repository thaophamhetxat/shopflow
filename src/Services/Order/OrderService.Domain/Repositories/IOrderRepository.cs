// File này chứa interface repository cho Order trong Domain của OrderService.
// Tạo ở Day 42 để định nghĩa hành vi lưu trữ cần thiết và giữ Clean Architecture, tức Domain chỉ nói "muốn lưu gì" còn Infrastructure quyết định "lưu bằng gì".
// Day 46 bổ sung 2 method đọc projection để Application đọc read model tùy theo query thay vì tải cả aggregate Order về.

using OrderService.Application.Queries;
using OrderService.Domain.Aggregates;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Repositories;

/// <summary>
/// Contract lưu trữ cho Aggregate Order, cho phép thêm đơn mới, đọc đơn theo Id và lưu tất cả thay đổi sau khi thao tác với aggregate.
/// Day 46 chỉ bổ sung đọc projection cho detail và list summary để Application trả DTO mà không cần nạp aggregate về.
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// Thêm một Order mới vào bộ nhớ của ChangeTracker để EF Core ghi tiếp khi gọi SaveChangesAsync.
    /// </summary>
    /// <param name="order">Aggregate Order cần lưu xuống database.</param>
    /// <param name="cancellationToken">Token hủy thao tác nếu request bị hủy.</param>
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm Order theo Id và load toàn bộ item của order bằng Include để đọc lại đơn đã lưu đầy đủ.
    /// Trả về null khi không tìm thấy đơn nào với Id tương ứng trong database.
    /// </summary>
    /// <param name="id">OrderId của đơn cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác nếu request bị hủy.</param>
    /// <returns>Order tương ứng nếu tồn tại, ngược lại là null.</returns>
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc chi tiết đơn hàng bằng projection read model, chỉ select đúng cột cần cho DTO mà không nạp aggregate và không dùng Include.
    /// Trả về null khi không tìm thấy order tương ứng với id.
    /// </summary>
    /// <param name="id">OrderId của đơn cần đọc chi tiết.</param>
    /// <param name="cancellationToken">Token hủy thao tác nếu request bị hủy.</param>
    /// <returns>OrderDetailDto đã được project từ SQL nếu tồn tại, ngược lại null.</returns>
    Task<OrderDetailDto?> GetOrderDetailAsync(OrderId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc danh sách summary theo khách hàng bằng keyset cursor, trả về tối đa pageSize item và không dùng OFFSET.
    /// </summary>
    /// <param name="customerId">CustomerId của khách hàng cần đọc lịch sử đơn hàng.</param>
    /// <param name="cursor">OrderId của đơn cuối trang trước để dùng làm cursor keyset, null nghĩa trang đầu.</param>
    /// <param name="pageSize">Số lượng item tối đa trên một trang, thường là 20 và clamp về 50 ở handler.</param>
    /// <param name="cancellationToken">Token hủy thao tác nếu request bị hủy.</param>
    /// <returns>Danh sách summary dưới dạng OrderSummaryDto.</returns>
    Task<IReadOnlyList<OrderSummaryDto>> ListOrderSummariesByCustomerAsync(CustomerId customerId, OrderId? cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi tất cả thay đổi đang được theo dõi vào database PostgreSQL.
    /// </summary>
    /// <param name="cancellationToken">Token hủy thao tác nếu request bị hủy.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
