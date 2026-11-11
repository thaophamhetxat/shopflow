using MediatR;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Queries;

/// <summary>
/// Handler cho ListOrdersByCustomerQuery, nhận list summary theo customer bằng repository read model projection và giữ logic paging ở tầng query.
/// Handler này không ném exception khi khách hàng chưa có đơn nào; nó trả list rỗng theo quy tắc query đọc dữ liệu.
/// </summary>
public sealed class ListOrdersByCustomerQueryHandler : IRequestHandler<ListOrdersByCustomerQuery, IReadOnlyList<OrderSummaryDto>>
{
    private readonly IOrderRepository _orderRepository;

    /// <summary>
    /// Khởi tạo handler với repository đọc projection cho danh sách order theo customer.
    /// </summary>
    /// <param name="orderRepository">Repository thực thi query list summary bằng projection SQL.</param>
    public ListOrdersByCustomerQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    /// <summary>
    /// Lấy trang list order của khách hàng theo keyset cursor, sau khi clamp pageSize về 1–50 để tránh client gởi số quá lớn hoặc quá nhỏ.
    /// </summary>
    /// <param name="request">Query yêu cầu khách hàng và cursor trang hiện tại.</param>
    /// <param name="cancellationToken">Token hủy khi handler đang chạy.</param>
    /// <returns>Danh sách summary theo trang, rỗng nếu khách hàng chưa có đơn.</returns>
    public async Task<IReadOnlyList<OrderSummaryDto>> Handle(ListOrdersByCustomerQuery request, CancellationToken cancellationToken)
    {
        // Clamp PageSize 1–50: giới hạn nhỏ để tránh request phá database với pageSize quá lớn, đồng thời giữ đồng nhất với quy tắc day 46 chỉ cho phép tối đa 50 item/trang.
        var boundedPageSize = Math.Clamp(request.PageSize, 1, 50);

        var customerId = CustomerId.From(request.CustomerId);
        OrderId? cursor = request.CursorOrderId is Guid cursorOrderId ? OrderId.From(cursorOrderId) : null;

        var result = await _orderRepository.ListOrderSummariesByCustomerAsync(customerId, cursor, boundedPageSize, cancellationToken);
        return result.Cast<OrderSummaryDto>().ToList();
    }
}
