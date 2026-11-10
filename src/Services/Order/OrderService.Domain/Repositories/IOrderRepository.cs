// File này chứa interface repository cho Order trong Domain của OrderService.
// Tạo ở Day 42 để định nghĩa hành vi lưu trữ cần thiết và giữ Clean Architecture, tức Domain chỉ nói "muốn lưu gì" còn Infrastructure quyết định "lưu bằng gì".
// Layer Domain này không chứa EF Core hay PostgreSQL, chỉ khai báo contract để Infrastructure và Application sau này dùng chung.

using OrderService.Domain.Aggregates;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Repositories;

/// <summary>
/// Contract lưu trữ cho Aggregate Order, cho phép thêm đơn mới, đọc đơn theo Id và lưu tất cả thay đổi sau khi thao tác với aggregate.
/// Domain định nghĩa đúng 3 thao tác cần thiết cho Day 42, không thêm operation đặc thù cho pagination hay delete vì chúng sẽ đến ở các day sau.
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
    /// Ghi tất cả thay đổi đang được theo dõi vào database PostgreSQL.
    /// </summary>
    /// <param name="cancellationToken">Token hủy thao tác nếu request bị hủy.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
