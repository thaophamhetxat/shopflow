using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Queries;

/// <summary>
/// Contract cho read-model projection của Order được truy vấn trực tiếp từ store mà không cần nạp aggregate.
/// Interface này thuộc về Application layer để tránh Domain phụ thuộc vào DTO và dependency ngược chiều.
/// </summary>
public interface IOrderReadModelRepository
{
    Task<OrderDetailDto?> GetOrderDetailAsync(OrderId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummaryDto>> ListOrderSummariesByCustomerAsync(
        CustomerId customerId,
        OrderId? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);
}
