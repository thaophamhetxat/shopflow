// File này chứa implementation repository lưu `saga_state` trong PostgreSQL bằng DbContext của OrderService.
// Layer Infrastructure này dùng EF Core để persist state machine của Saga, phục vụ recover sau restart và kiểm tra trạng thái hiện tại trong Day 52.

using Microsoft.EntityFrameworkCore;
using OrderService.Application.Sagas;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Sagas;

/// <summary>
/// Repository Entity Framework của `saga_state`, lưu trạng thái của mỗi Saga instance theo orderId.
/// </summary>
public sealed class SagaStateRepository : ISagaStateRepository
{
    private readonly OrderDbContext _dbContext;

    /// <summary>
    /// Khởi tạo repository với DbContext đã được đăng ký trong DI.
    /// </summary>
    /// <param name="dbContext">DbContext dùng để đọc/ghi bảng `saga_state`.</param>
    public SagaStateRepository(OrderDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Đọc trạng thái saga của một order, null nếu chưa từng chạy hoặc chưa khởi tạo checkpoint.
    /// </summary>
    /// <param name="orderId">OrderId cần query.</param>
    /// <param name="cancellationToken">Token hủy khi đọc DB.</param>
    /// <returns>Đối tượng `SagaState` nếu tồn tại, ngược lại null.</returns>
    public async Task<SagaState?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(state => state.OrderId == orderId, cancellationToken);
    }

    /// <summary>
    /// Giao diện phụ đọc tương đương, dùng cho resume/retry từ cùng một order.
    /// </summary>
    public Task<SagaState?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return GetAsync(orderId, cancellationToken);
    }

    /// <summary>
    /// Lưu một trạng thái mới hoặc cập nhật trạng thái hiện hữu theo orderId.
    /// </summary>
    /// <param name="state">Đối tượng SagaState cần persist.</param>
    /// <param name="cancellationToken">Token hủy khi save DB.</param>
    public async Task SaveAsync(SagaState state, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.SagaStates
            .FirstOrDefaultAsync(entry => entry.OrderId == state.OrderId, cancellationToken);

        if (existing is null)
        {
            _dbContext.SagaStates.Add(state);
        }
        else
        {
            existing.State = state.State;
            existing.UpdatedAt = state.UpdatedAt;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Cập nhật nhanh trạng thái theo `orderId` mà không cần tạo entity từ ngoài.
    /// </summary>
    public async Task UpsertAsync(Guid orderId, SagaStateStatus status, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.SagaStates
            .FirstOrDefaultAsync(entry => entry.OrderId == orderId, cancellationToken);

        if (existing is null)
        {
            _dbContext.SagaStates.Add(SagaState.Create(orderId, status));
        }
        else
        {
            existing.UpdateState(status);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
