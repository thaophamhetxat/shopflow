// File này chứa handler xử lý command đặt đơn hàng của OrderService.Application.
// Tạo ở Day 41 để chuyển dữ liệu input thành Aggregate Order, kiểm tra dữ liệu đặt hàng và trả về DTO rõ ràng cho tầng trên.
// Application layer thực hiện các bước: validate input, map sang domain object, gọi Aggregate, trả về DTO.

using MediatR;
using OrderService.Application.Queries;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Commands;

/// <summary>
/// Xử lý PlaceOrderCommand theo thứ tự: chuyển command sang CustomerId và Address hợp lệ, tạo aggregate Order, map từng item command sang OrderItem, thêm vào aggregate, rồi trả về OrderDto.
/// Handler này đang là khung vì chưa có repository, nên logic lưu dữ liệu sẽ được hoàn thiện ở Day 42 khi EF Core được thêm vào.
/// </summary>
public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, OrderDto>
{
    public Task<OrderDto> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var customerId = CustomerId.From(request.CustomerId);
        var shippingAddress = new Address(request.Street, request.City, request.Country, request.PostalCode);
        var order = Order.Place(customerId, shippingAddress);

        foreach (var item in request.Items)
        {
            var money = new Money(item.UnitPrice, item.Currency);
            var orderItem = new OrderItem(item.ProductId, item.Quantity, money);
            order.AddItem(orderItem);
        }

        var orderDto = new OrderDto(
            order.Id.Value,
            order.CustomerId.Value,
            order.Status.ToString(),
            order.ShippingAddress.Street,
            order.ShippingAddress.City,
            order.ShippingAddress.Country,
            order.ShippingAddress.PostalCode,
            order.Total.Amount,
            order.Total.Currency,
            order.Items.Select(x => new OrderItemDto(x.ProductId, x.Quantity, x.UnitPrice.Amount, x.UnitPrice.Currency)).ToList());

        return Task.FromResult(orderDto);
    }
}
