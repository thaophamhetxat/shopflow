// File này chứa bộ test bổ sung cho Aggregate Order trong Domain của OrderService.
// Tạo ở Day 44 để lấp khoảng trống còn lại trong bộ test của Order aggregate, tập trung vào trạng thái biên và event nghiệp vụ đã chốt từ Day 40 và Day 41.
// Đây là phần kiểm tra không sửa production code, chỉ thêm các ví dụ phức tạp hơn để đảm bảo invariant không còn trống khi chạy dotnet test.

using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.Events;
using OrderService.Domain.Exceptions;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Tests.Aggregates;

public class OrderAggregateAdditionalTests
{
    // Chứng minh Evidence Day 40: order đang ở trạng thái Confirmed không được thêm any item nữa dù item mới có giá trị hợp lệ.
    [Fact]
    public void AddItem_AfterConfirmed_Throws()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(120m, "USD")));
        order.Confirm();

        Assert.Throws<OrderDomainException>(() => order.AddItem(new OrderItem(Guid.NewGuid(), 2, new Money(80m, "USD"))));
    }

    // Chứng minh Evidence Day 40: khi order đã bị hủy, step tiếp theo không thể đưa hàng vào để sửa lại trạng thái nghiệp vụ.
    [Fact]
    public void AddItem_AfterCancelled_Throws()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.Cancel();

        Assert.Throws<OrderDomainException>(() => order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(50m, "USD"))));
    }

    // Chứng minh Evidence Day 40: confirm lần hai trên cùng một order phải bị chặn để không thay đổi trạng thái nhiều lần.
    [Fact]
    public void Confirm_AfterConfirm_Throws()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(150m, "USD")));
        order.Confirm();

        Assert.Throws<OrderDomainException>(() => order.Confirm());
    }

    // Chứng minh Evidence Day 40: cancel lần hai trên order đã hủy phải ném domain exception để giữ tính nhất quán của trạng thái.
    [Fact]
    public void Cancel_AfterCancel_Throws()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.Cancel();

        Assert.Throws<OrderDomainException>(() => order.Cancel());
    }

    // Chứng minh Evidence Day 40: đơn hủy từ trạng thái Confirmed vẫn là hành vi hợp lệ và không giới hạn số lượng item đã xác nhận trước đó.
    [Fact]
    public void Cancel_AfterConfirm_Succeeds()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 3, new Money(40m, "USD")));
        order.Confirm();

        order.Cancel();

        Assert.Equal(OrderService.Domain.Enums.OrderStatus.Cancelled, order.Status);
    }

    // Chứng minh Evidence Day 41: khi confirm thành công, Order aggregate phải phát đúng một event OrderConfirmedEvent và không phát event lạ.
    [Fact]
    public void Confirm_EmitsSingleOrderConfirmedEvent()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(175m, "USD")));

        order.Confirm();

        var events = order.DomainEvents.OfType<OrderConfirmedEvent>().ToList();
        Assert.Single(events);
        Assert.Equal(order.Id, events[0].OrderId);
        Assert.Equal(order.Total, events[0].Total);
    }

    // Chứng minh Evidence Day 41: khi cancel thành công từ trạng thái Pending hoặc Confirmed, aggregate sẽ phát đúng một event OrderCancelledEvent duy nhất.
    [Fact]
    public void Cancel_EmitsSingleOrderCancelledEvent()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 2, new Money(60m, "USD")));
        order.Confirm();

        order.Cancel();

        var events = order.DomainEvents.OfType<OrderCancelledEvent>().ToList();
        Assert.Single(events);
        Assert.Equal(order.Id, events[0].OrderId);
    }

    // Chứng minh Evidence Day 41: sau khi xóa event đã xử lý, aggregate không còn giữ event chờ nào và sẵn sàng cho chu kỳ tiếp theo.
    [Fact]
    public void ClearDomainEvents_AfterMultipleEvents_EmptiesCollection()
    {
        var order = Order.Place(CustomerId.New(), new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(100m, "USD")));
        order.Confirm();

        Assert.NotEmpty(order.DomainEvents);

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
    }
}
