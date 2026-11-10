// File này chứa test cho Aggregate Order trong Domain của OrderService.
// Tạo ở Day 40 để chứng minh mọi invariant nghiệp vụ của Order đều được kiểm soát qua AddItem, Confirm và Cancel.
// Domain này dùng tests để chứng minh Evidence Day 40: vi phạm invariant ném OrderDomainException, không phải exception chung.

using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.Exceptions;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Tests.Aggregates;

public class OrderTests
{
    // Chứng minh Evidence Day 40: Order mới phải ở trạng thái Pending, rỗng item và tổng tiền bằng 0 ban đầu.
    [Fact]
    public void Place_CreatesPendingOrder()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));

        Assert.Equal(OrderService.Domain.Enums.OrderStatus.Pending, order.Status);
        Assert.Empty(order.Items);
        Assert.Equal(0m, order.Total.Amount);
    }

    // Chứng minh Evidence Day 40: thêm item vào đơn Pending phải tạo item đầu tiên và tổng tiền bằng đúng LineTotal.
    [Fact]
    public void AddItem_ToPendingOrder_AddsItem()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));
        var item = new OrderItem(Guid.NewGuid(), 2, new Money(10m, "USD"));

        order.AddItem(item);

        Assert.Single(order.Items);
        Assert.Equal(item.LineTotal.Amount, order.Total.Amount);
        Assert.Equal(item.LineTotal.Currency, order.Total.Currency);
    }

    // Chứng minh Evidence Day 40: không thể thêm item sau khi xác nhận, vì đơn đã chốt và không cho phép lệch tổng tiền.
    [Fact]
    public void AddItem_AfterConfirm_ThrowsDomainException()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(10m, "USD")));
        order.Confirm();

        Assert.Throws<OrderDomainException>(() => order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(20m, "USD"))));
    }

    // Chứng minh Evidence Day 40: đơn đã hủy không được thêm hàng lần nữa, đây là vi phạm trạng thái nghiệp vụ.
    [Fact]
    public void AddItem_AfterCancel_ThrowsDomainException()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));
        order.Cancel();

        Assert.Throws<OrderDomainException>(() => order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(10m, "USD"))));
    }

    // Chứng minh Evidence Day 40: cùng đơn không được trộn nhiều loại tiền, nếu vậy phải ném domain exception.
    [Fact]
    public void AddItem_DifferentCurrency_ThrowsDomainException()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(10m, "USD")));

        Assert.Throws<OrderDomainException>(() => order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(50m, "VND"))));
    }

    // Chứng minh Evidence Day 40: xác nhận đơn rỗng là lỗi nghiệp vụ, không được vượt qua invariant không có hàng.
    [Fact]
    public void Confirm_EmptyOrder_ThrowsDomainException()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));

        Assert.Throws<OrderDomainException>(() => order.Confirm());
    }

    // Chứng minh Evidence Day 40: tổng tiền bằng 0 không tạo giá trị kinh doanh, xác nhận đơn này bị chặn.
    [Fact]
    public void Confirm_ZeroTotal_ThrowsDomainException()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(0m, "USD")));

        Assert.Throws<OrderDomainException>(() => order.Confirm());
    }

    // Chứng minh Evidence Day 40: đơn có hàng hợp lệ và tổng > 0 thì Confirm chuyển trạng thái sang Confirmed.
    [Fact]
    public void Confirm_WithItems_SetsConfirmed()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(10m, "USD")));

        order.Confirm();

        Assert.Equal(OrderService.Domain.Enums.OrderStatus.Confirmed, order.Status);
    }

    // Chứng minh Evidence Day 40: xác nhận lần hai là vi phạm trạng thái, không được phép sữa lại trạng thái một đơn đã chốt.
    [Fact]
    public void Confirm_Twice_ThrowsDomainException()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(10m, "USD")));
        order.Confirm();

        Assert.Throws<OrderDomainException>(() => order.Confirm());
    }

    // Chứng minh Evidence Day 40: đơn Pending có thể hủy và chuyển sang trạng thái Cancelled.
    [Fact]
    public void Cancel_FromPending_SetsCancelled()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));

        order.Cancel();

        Assert.Equal(OrderService.Domain.Enums.OrderStatus.Cancelled, order.Status);
    }

    // Chứng minh Evidence Day 40: đơn Confirmed vẫn cho phép hủy nếu quy trình nghiệp vụ yêu cầu, rồi trạng thái chuyển sang Cancelled.
    [Fact]
    public void Cancel_FromConfirmed_SetsCancelled()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(10m, "USD")));
        order.Confirm();

        order.Cancel();

        Assert.Equal(OrderService.Domain.Enums.OrderStatus.Cancelled, order.Status);
    }

    // Chứng minh Evidence Day 40: hủy lần hai là sai trạng thái nghiệp vụ và phải ném domain exception thay vì bỏ qua.
    [Fact]
    public void Cancel_Twice_ThrowsDomainException()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Le Loi", "Hanoi", "Vietnam", "100000"));
        order.Cancel();

        Assert.Throws<OrderDomainException>(() => order.Cancel());
    }
}
