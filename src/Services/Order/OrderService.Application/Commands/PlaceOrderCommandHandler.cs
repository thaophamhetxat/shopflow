// File này chứa handler xử lý command đặt đơn hàng của OrderService.Application.
// Tạo ở Day 41 để chuyển dữ liệu input thành Aggregate Order và trả về OrderId cho tầng gọi.
// Application layer thực hiện các bước: map input sang domain object, gọi Aggregate và trả về mã đơn.

using MediatR;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Commands;

/// <summary>
/// Xử lý PlaceOrderCommand theo thứ tự: chuyển CustomerId và tạo Address, tạo Aggregate Order, map từng dòng hàng thành OrderItem rồi thêm vào đơn.
/// Cuối cùng handler trả về OrderId; việc lưu đơn sẽ được bổ sung khi repository được tạo ở Day 42.
/// </summary>
public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, OrderId>
{
    public Task<OrderId> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var customerId = CustomerId.From(request.CustomerId);
        var shippingAddress = new Address(request.Street, request.City, request.Country, request.PostalCode);
        var order = Order.Place(customerId, shippingAddress);

        foreach (var item in request.Items)
        {
            var money = new Money(item.UnitPriceAmount, item.UnitPriceCurrency);
            var orderItem = new OrderItem(item.ProductId, item.Quantity, money);
            order.AddItem(orderItem);
        }

        return Task.FromResult(order.Id);
    }
}
