// File này triển khai repository Payment bằng EF Core cho PaymentService.
// Tạo ở Day 48 để lưu và truy vấn aggregate Payment mà không dùng Include vì không có collection con.
// Gateway Day 49 và Saga Day 52 sẽ dùng repository này cho các bước Charge/Refund.

using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Aggregates;
using PaymentService.Domain.Repositories;
using PaymentService.Domain.ValueObjects;

namespace PaymentService.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repository PostgreSQL cho Payment, thao tác trực tiếp với một aggregate không có collection con.
/// </summary>
public sealed class PaymentRepository : IPaymentRepository
{
    private readonly PaymentDbContext _dbContext;

    /// <summary>
    /// Tạo repository với context payment hiện tại.
    /// </summary>
    /// <param name="dbContext">Context EF Core quản lý Payment.</param>
    public PaymentRepository(PaymentDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Đưa Payment mới vào ChangeTracker để lưu ở lần SaveChangesAsync tiếp theo.
    /// </summary>
    /// <param name="payment">Aggregate Payment cần thêm.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Task đại diện cho thao tác thêm.</returns>
    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        await _dbContext.Payments.AddAsync(payment, cancellationToken);
    }

    /// <summary>
    /// Tìm Payment theo strongly typed ID, trả null khi không tồn tại.
    /// </summary>
    /// <param name="id">PaymentId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Payment nếu tồn tại; ngược lại null.</returns>
    public Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken cancellationToken = default)
    {
        // Dùng tracking cho đường ghi vì aggregate Payment có thể được cập nhật trong cùng DbContext trước khi SaveChangesAsync.
        return _dbContext.Payments.FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);
    }

    /// <summary>
    /// Chỉ đọc payment cho endpoint GET mà không theo dõi entity để tránh tracking overhead và ràng buộc change tracking.
    /// </summary>
    /// <param name="id">PaymentId cần tìm.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Payment nếu tồn tại; ngược lại null.</returns>
    public Task<Payment?> GetByIdReadOnlyAsync(PaymentId id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);
    }

    /// <summary>
    /// Lưu mọi thay đổi Payment đang được context theo dõi.
    /// </summary>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Task đại diện cho thao tác lưu.</returns>
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
