// File này chứa handler query lấy chi tiết đơn hàng của OrderService.Application.
// Tạo ở Day 45 để phản ánh repository thực tế và map data từ aggregate sang DTO nghiêm ngặt.
// Application layer giữ query này như contract rõ ràng cho UI/API ở các day sau mà không để cấu trúc domain lộ ra.

using MediatR;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Queries;

/// <summary>
/// Handler cho GetOrderByIdQuery, đọc Order theo Id từ repository và map sang dto chứa trạng thái, tổng tiền và từng item.
/// Nếu không tìm thấy đơn thì trả null theo quy tắc query: không tìm thấy là kết quả hợp lệ, không phải exception nghiệp vụ.
/// </summary>
public sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly IOrderRepository _orderRepository;

    /// <summary>
    /// Khởi tạo handler với repository truy vấn Order từ PostgreSQL.
    /// </summary>
    /// <param name="orderRepository">Repository dùng để đọc aggregate Order kèm danh sách item.</param>
    public GetOrderByIdQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    /// <summary>
    /// Tìm Order theo id và trả về DTO nếu tồn tại; null nếu không tìm thấy.
    /// </summary>
    /// <param name="request">Query yêu cầu đọc đơn theo OrderId.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị cancel.</param>
    /// <returns>OrderDto đầy đủ nếu đơn tồn tại, ngược lại null.</returns>
    public async Task<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(request.OrderId);
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        // GetById trả null khi không tìm thấy (không ném exception): query khác command, không tìm thấy là kết quả hợp lệ, không phải lỗi nghiệp vụ.
        var items = order.Items
            .Select(item => new OrderItemDto(
                item.ProductId,
                item.Quantity,
                item.UnitPrice.Amount,
                item.UnitPrice.Currency,
                item.LineTotal.Amount))
            .ToArray();

        return new OrderDto(
            order.Id.Value,
            order.CustomerId.Value,
            order.Status.ToString(),
            order.Total.Amount,
            order.Total.Currency,
            order.CreatedAt,
            items);
    }
}
