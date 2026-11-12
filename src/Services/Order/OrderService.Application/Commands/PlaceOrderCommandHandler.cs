// File này chứa handler xử lý command đặt đơn hàng của OrderService.Application.
// Tạo ở Day 45 để đặt đơn thật, lưu vào PostgreSQL và chống gửi trùng bằng Redis theo IdempotencyKey.
// Application layer dùng handler này để map dữ liệu đầu vào thành Aggregate Order và trả về OrderId hợp lệ.

using MediatR;
using OrderService.Application.Idempotency;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;
using ShopFlow.BuildingBlocks;

namespace OrderService.Application.Commands;

/// <summary>
/// Xử lý PlaceOrderCommand bằng cách kiểm tra idempotency, tạo Order từ input, lưu xuống repository và ghi kết quả 24h vào Redis.
/// Nếu cùng key gửi trùng trong vòng 24h thì lần thứ hai trả lại OrderId cũ mà không tạo đơn mới.
/// </summary>
public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, OrderId>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IIdempotencyStore _idempotencyStore;

    /// <summary>
    /// Khởi tạo handler với repository lưu Order và store Redis kiểm tra idempotency.
    /// </summary>
    /// <param name="orderRepository">Repository dùng để lưu Aggregate Order xuống PostgreSQL.</param>
    /// <param name="idempotencyStore">Store dùng để cache OrderId theo IdempotencyKey trong 24h.</param>
    public PlaceOrderCommandHandler(IOrderRepository orderRepository, IIdempotencyStore idempotencyStore)
    {
        _orderRepository = orderRepository;
        _idempotencyStore = idempotencyStore;
    }

    /// <summary>
    /// Tạo đơn hàng từ command, nhưng nếu IdempotencyKey đã từng lưu trong Redis thì trả lại OrderId cũ thay vì tạo mới.
    /// </summary>
    /// <param name="request">Command đặt đơn hàng có dữ liệu người mua, địa chỉ, item và key chống trùng.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị cancel.</param>
    /// <returns>OrderId của đơn vừa tạo hoặc OrderId cũ khi key trùng.</returns>
    public async Task<OrderId> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        using var activity = ShopFlowActivitySource.Source.StartActivity(ShopFlowActivitySource.OrderPlace);

        // Kiểm tra idempotency TRƯỚC khi tạo Order: cùng 1 key gửi 2 lần chỉ được tạo 1 Order, lần 2 trả lại OrderId cũ.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingOrderId = await _idempotencyStore.GetAsync<OrderId>(request.IdempotencyKey, cancellationToken);
            if (existingOrderId != default)
            {
                return existingOrderId;
            }
        }

        var customerId = CustomerId.From(request.CustomerId);
        var shippingAddress = new Address(request.Street, request.City, request.Country, request.PostalCode);
        var order = Order.Place(customerId, shippingAddress);
        activity?.SetTag("order.id", order.Id.ToString());
        activity?.SetTag("customer.id", request.CustomerId.ToString());

        foreach (var item in request.Items)
        {
            var money = new Money(item.UnitPriceAmount, item.UnitPriceCurrency);
            var orderItem = new OrderItem(item.ProductId, item.Quantity, money);
            order.AddItem(orderItem);
        }

        await _orderRepository.AddAsync(order, cancellationToken);
        await _orderRepository.SaveChangesAsync(cancellationToken);

        // Chỉ đếm sau khi repository persist thành công để không tính request bị validation hoặc persistence từ chối.
        ShopFlowMetrics.OrdersCreated.Inc();

        // Lưu kết quả với TTL 24h: key chỉ có tác dụng chống gửi trùng trong 24h, quá hạn gửi lại key cũ được tính là lệnh mới.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            await _idempotencyStore.SetAsync(request.IdempotencyKey, order.Id, TimeSpan.FromHours(24), cancellationToken);
        }

        return order.Id;
    }
}
