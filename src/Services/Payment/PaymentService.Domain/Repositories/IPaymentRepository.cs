// File này định nghĩa hợp đồng lưu trữ Payment thuộc PaymentService Domain.
// Tạo ở Day 48 để Application thao tác với aggregate mà không phụ thuộc EF Core.
// Gateway Day 49 và Saga Day 52 sẽ dùng hợp đồng này khi Charge, tra cứu hoặc Refund.

using PaymentService.Domain.Aggregates;
using PaymentService.Domain.ValueObjects;

namespace PaymentService.Domain.Repositories;

/// <summary>
/// Hợp đồng thêm Payment, đọc theo ID và lưu các thay đổi của aggregate.
/// </summary>
public interface IPaymentRepository
{
    /// <summary>
    /// Đưa Payment mới vào vùng theo dõi của repository để lưu ở lần SaveChangesAsync tiếp theo.
    /// </summary>
    /// <param name="payment">Aggregate Payment cần thêm.</param>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Task đại diện cho thao tác thêm.</returns>
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm Payment theo ID để thao tác ghi; cần tracking vì aggregate sẽ được sửa trong cùng DbContext.
    /// </summary>
    /// <param name="id">PaymentId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Payment nếu tìm thấy; ngược lại null.</returns>
    Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm Payment theo ID ở chế độ đọc thuần, không theo dõi entity cho các endpoint query.
    /// </summary>
    /// <param name="id">PaymentId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Payment nếu tìm thấy; ngược lại null.</returns>
    Task<Payment?> GetByIdReadOnlyAsync(PaymentId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi các thay đổi đang được repository theo dõi xuống cơ sở dữ liệu.
    /// </summary>
    /// <param name="cancellationToken">Token hủy thao tác bất đồng bộ.</param>
    /// <returns>Task đại diện cho thao tác lưu.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
