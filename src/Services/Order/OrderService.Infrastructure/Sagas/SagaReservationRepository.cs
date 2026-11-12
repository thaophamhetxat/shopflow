// File này chứa implementation repository lưu `saga_reservations` trong PostgreSQL bằng DbContext của OrderService.
// Layer Infrastructure chịu trách nhiệm thực thi contract application và map row `saga_reservations` sang EF Core để xử lý compensate và resume.
// Day 53 tạo repository này để support compensation idempotent và model `saga_reservations` hoạt động với `OrderSaga`.
// Day sau có thể dùng table này để audit direct recovery mà không làm gián đoạn logic Day 53.

using Microsoft.EntityFrameworkCore;
using OrderService.Application.Sagas;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Sagas;

/// <summary>
/// Repository EF Core cho bảng `saga_reservations`, lưu từng sản phẩm đã reserve để `OrderSaga` release lại đúng cách.
/// </summary>
public sealed class SagaReservationRepository : ISagaReservationRepository
{
    private readonly OrderDbContext _dbContext;

    /// <summary>
    /// Khởi tạo repository với DbContext của OrderService.
    /// </summary>
    /// <param name="dbContext">DbContext được DI inject và quản lý transaction order DB.</param>
    public SagaReservationRepository(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SagaReservation>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SagaReservations
            .AsNoTracking()
            .Where(reservation => reservation.OrderId == orderId)
            .OrderBy(reservation => reservation.ProductId)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SagaReservation?> GetByOrderIdAndProductIdAsync(Guid orderId, Guid productId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SagaReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(reservation => reservation.OrderId == orderId && reservation.ProductId == productId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(SagaReservation reservation, CancellationToken cancellationToken = default)
    {
        // Đường ghi cần theo dõi reservation hiện hữu để cập nhật trạng thái và số lượng bằng SaveChangesAsync.
        var existing = await _dbContext.SagaReservations
            .FirstOrDefaultAsync(entry => entry.OrderId == reservation.OrderId && entry.ProductId == reservation.ProductId, cancellationToken);

        if (existing is null)
        {
            _dbContext.SagaReservations.Add(reservation);
        }
        else
        {
            existing.Quantity = reservation.Quantity;
            existing.Status = reservation.Status;
            existing.UpdatedAt = reservation.UpdatedAt;
            if (existing.CreatedAt == default)
            {
                existing.CreatedAt = reservation.CreatedAt;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task MarkReleasedAsync(Guid orderId, Guid productId, CancellationToken cancellationToken = default)
    {
        // MarkReleased sửa aggregate reservation vừa tải nên truy vấn này giữ tracking.
        var reservation = await _dbContext.SagaReservations
            .FirstOrDefaultAsync(entry => entry.OrderId == orderId && entry.ProductId == productId, cancellationToken);

        if (reservation is null)
        {
            return;
        }

        reservation.MarkReleased();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(Guid orderId, Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        // Upsert sửa reservation hiện có trong cùng context, vì vậy entity cần được theo dõi.
        var existing = await _dbContext.SagaReservations
            .FirstOrDefaultAsync(entry => entry.OrderId == orderId && entry.ProductId == productId, cancellationToken);

        if (existing is null)
        {
            _dbContext.SagaReservations.Add(SagaReservation.Create(orderId, productId, quantity));
        }
        else
        {
            existing.Quantity = quantity;
            existing.Status = SagaReservationStatus.Reserved;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
