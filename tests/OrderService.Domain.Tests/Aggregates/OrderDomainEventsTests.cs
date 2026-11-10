// File này chứa test chứng minh Order Aggregate phát ra domain event đúng thời điểm.
// Tạo ở Day 41 để kiểm tra Evidence rằng ngay khi Place/Confirm/Cancel xảy ra, aggregate phải phát ra event tương ứng.
// Domain này dùng event test để bảo đảm event không bị quên khi trạng thái đơn thay đổi.

using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.Events;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Tests.Aggregates;

public class OrderDomainEventsTests
{
    // Test này chứng minh Evidence Day 41: Order mới tạo phải phát OrderPlacedEvent ngay khi Place được gọi.
    [Fact]
    public void Place_CreatesOrderPlacedEvent()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));

        var domainEvent = Assert.Single(order.DomainEvents);
        var placedEvent = Assert.IsType<OrderPlacedEvent>(domainEvent);

        Assert.Equal(order.Id, placedEvent.OrderId);
        Assert.Equal(order.CustomerId, placedEvent.CustomerId);
    }

    // Test này chứng minh Evidence Day 41: order được xác nhận phải phát OrderConfirmedEvent để báo hiệu trạng thái đã chốt.
    [Fact]
    public void Confirm_CreatesOrderConfirmedEvent()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(150m, "USD")));

        order.Confirm();

        var confirmedEvent = Assert.IsType<OrderConfirmedEvent>(order.DomainEvents.Single(x => x is OrderConfirmedEvent));
        Assert.Equal(order.Id, confirmedEvent.OrderId);
        Assert.Equal(order.CustomerId, confirmedEvent.CustomerId);
    }

    // Test này chứng minh Evidence Day 41: order bị hủy phải phát OrderCancelledEvent để thông báo các bên liên quan tiến hành xử lý hủy.
    [Fact]
    public void Cancel_CreatesOrderCancelledEvent()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));

        order.Cancel();

        var cancelledEvent = Assert.IsType<OrderCancelledEvent>(order.DomainEvents.Single(x => x is OrderCancelledEvent));
        Assert.Equal(order.Id, cancelledEvent.OrderId);
        Assert.Equal(order.CustomerId, cancelledEvent.CustomerId);
    }
}
