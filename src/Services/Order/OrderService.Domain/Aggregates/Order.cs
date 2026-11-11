// File này chứa Aggregate Root cho Order trong Domain của OrderService.
// Tạo ở Day 40 để chuẩn hóa vòng đời đơn hàng và bảo vệ invariant nghiệp vụ trước khi làm Application/Infrastructure ở các day sau.
// Domain này giữ Order làm boundary nhất quán với toàn bộ OrderItem bên trong, vì vậy mọi thay đổi phải đi qua AddItem, Confirm và Cancel.

using OrderService.Domain.Entities;
using OrderService.Domain.Enums;
using OrderService.Domain.Events;
using OrderService.Domain.Exceptions;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Aggregates;

public sealed record OrderSnapshotData(
    Guid OrderId,
    Guid CustomerId,
    IReadOnlyList<OrderItem> Items,
    Address ShippingAddress,
    OrderStatus Status,
    decimal TotalAmount,
    string TotalCurrency,
    int Version,
    DateTime CreatedAt)
{
    public static OrderSnapshotData FromOrder(Order order)
    {
        return new OrderSnapshotData(
            order.Id.Value,
            order.CustomerId.Value,
            order.Items.ToList(),
            order.ShippingAddress,
            order.Status,
            order.Total.Amount,
            order.Total.Currency,
            order.Version,
            order.CreatedAt);
    }
}

/// <summary>
/// Aggregate Root đại diện cho một đơn hàng trong ShopFlow, bao gồm trạng thái, địa chỉ giao hàng, khách hàng và toàn bộ dòng hàng của đơn.
/// Ranh giới nhất quán của nó là Order + các OrderItem bên trong; không ai được sửa OrderItem trực tiếp bên ngoài, mọi thay đổi đều phải đi qua AddItem để giữ tính toàn vẹn của tổng tiền và trạng thái đơn.
/// Hành vi: Place để khởi tạo đơn ở Pending, AddItem để thêm item trước khi chốt, Confirm để xác nhận và chốt tiền, Cancel để hủy trước hoặc sau khi đã xác nhận nếu còn hợp lệ.
/// </summary>
public sealed class Order
{
    private readonly List<OrderItem> _items;
    private readonly List<IDomainEvent> _domainEvents;
    private OrderStatus _status;
    private DateTime _createdAt;

    /// <summary>
    /// Mã định danh duy nhất của đơn hàng, được sinh khi đặt đơn và không bao giờ thay đổi.
    /// </summary>
    public OrderId Id { get; private set; }

    /// <summary>
    /// Mã khách hàng sở hữu đơn hàng, được gán ngay khi Order được tạo.
    /// </summary>
    public CustomerId CustomerId { get; private set; }

    /// <summary>
    /// Địa chỉ giao hàng được lưu cùng đơn hàng và không cho phép thay đổi trực tiếp từ bên ngoài.
    /// </summary>
    public Address ShippingAddress { get; private set; }

    /// <summary>
    /// Trạng thái hiện tại của đơn hàng, chỉ có thể thay đổi qua Confirm hoặc Cancel.
    /// </summary>
    public OrderStatus Status => _status;

    /// <summary>
    /// Thời điểm tạo đơn, được ghi khi Place được gọi và không bao giờ đổi sau đó.
    /// </summary>
    public DateTime CreatedAt
    {
        get => _createdAt;
        private set => _createdAt = value;
    }

    /// <summary>
    /// Version của stream event tương ứng với số lượng domain event đã phát sinh và replay xong trên aggregate.
    /// Mỗi event mới đều tăng lên 1, và mỗi lần Apply replay 1 event cũng tăng lên 1 để Version phản ánh đúng stream state.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Danh sách dòng hàng của đơn hàng, được trả về dạng chỉ đọc để ngăn sửa trực tiếp bên ngoài aggregate.
    /// </summary>
    public IReadOnlyList<OrderItem> Items => _items;

    /// <summary>
    /// Tổng tiền của đơn hàng, tính bằng tổng LineTotal các item. Khi chưa có item nào, vẫn trả về 0 USD như giá trị rỗng trong domain.
    /// </summary>
    public Money Total => CalculateTotal(_items);

    /// <summary>
    /// Danh sách sự kiện nghiệp vụ đã phát ra từ aggregate Order nhưng chưa được xử lý bởi Outbox hoặc Saga.
    /// Dùng để ghi lại mọi thay đổi quan trọng như đơn được đặt, xác nhận hoặc hủy.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Order(OrderId id, CustomerId customerId, Address shippingAddress, DateTime createdAt)
    {
        Id = id;
        CustomerId = customerId;
        ShippingAddress = shippingAddress;
        _createdAt = createdAt;
        _status = OrderStatus.Pending;
        _items = new List<OrderItem>();
        _domainEvents = new List<IDomainEvent>();
        Version = 0;
    }

    /// <summary>
    /// Tạo một đơn hàng mới ở trạng thái Pending, dùng làm factory duy nhất cho aggregate Order.
    /// </summary>
    /// <param name="customerId">Mã khách hàng đặt hàng, không được rỗng.</param>
    /// <param name="shippingAddress">Địa chỉ giao hàng hợp lệ theo domain.</param>
    /// <returns>Order mới đã ở trạng thái Pending và chưa có item nào.</returns>
    public static Order Place(CustomerId customerId, Address shippingAddress)
    {
        var order = new Order(OrderId.New(), customerId, shippingAddress, DateTime.UtcNow);

        // Phát OrderPlacedEvent: đơn vừa được tạo là sự kiện nghiệp vụ mà Outbox (Day 43) sẽ ghi lại để các service khác biết về việc đặt hàng mới.
        // Event này cần mang cả Items và ShippingAddress vì event stream là source of truth; nếu không có dữ liệu này, replay không thể tái dựng lại order đã đặt một cách đúng đắn.
        order.AddDomainEvent(new OrderPlacedEvent(order.Id, order.CustomerId, order.Items, order.ShippingAddress, order.Total, DateTime.UtcNow));

        return order;
    }

    public static Order FromSnapshot(OrderSnapshotData snapshot)
    {
        var order = new Order(
            OrderId.From(snapshot.OrderId),
            CustomerId.From(snapshot.CustomerId),
            snapshot.ShippingAddress,
            snapshot.CreatedAt);

        order.Id = OrderId.From(snapshot.OrderId);
        order.CustomerId = CustomerId.From(snapshot.CustomerId);
        order.ShippingAddress = snapshot.ShippingAddress;
        order.CreatedAt = snapshot.CreatedAt;
        order._status = snapshot.Status;
        order.Version = snapshot.Version;

        foreach (var item in snapshot.Items)
        {
            order._items.Add(item);
        }

        return order;
    }

    /// <summary>
    /// Thêm một dòng hàng vào đơn hàng khi đơn đang ở trạng thái Pending.
    /// Từ Pending -> Pending, không được thêm khi đã Confirmed hoặc Cancelled.
    /// </summary>
    /// <param name="item">Dòng hàng cần thêm vào order.</param>
    /// <exception cref="OrderDomainException">Ném khi đơn đã xác nhận, đã hủy, hoặc thêm item với tiền tệ khác với những item đã có trong đơn.</exception>
    public void AddItem(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Không thêm item sau khi Confirmed: đơn đã xác nhận là đã chốt với kho/thanh toán, thêm item nữa sẽ làm tổng tiền lệch khỏi đơn đã chốt.
        if (_status == OrderStatus.Confirmed)
        {
            throw new OrderDomainException("Không thể thêm sản phẩm sau khi đơn đã được xác nhận.");
        }

        // Không thêm item sau khi Cancelled: đơn đã hủy thì không còn giá trị nghiệp vụ để bổ sung hàng nữa, việc đó là dấu hiệu sai trạng thái.
        if (_status == OrderStatus.Cancelled)
        {
            throw new OrderDomainException("Không thể thêm sản phẩm khi đơn đã bị hủy.");
        }

        // Cùng đơn phải dùng một đơn vị tiền: nếu đơn đã có item theo USD, thêm VND sẽ làm tổng tiền không thể hợp thức trong một order duy nhất.
        if (_items.Count > 0 && !string.Equals(_items[0].LineTotal.Currency, item.LineTotal.Currency, StringComparison.Ordinal))
        {
            throw new OrderDomainException("Không thể thêm dòng hàng với đơn vị tiền khác với các dòng hiện có trong đơn.");
        }

        _items.Add(item);
    }

    /// <summary>
    /// Xác nhận đơn hàng khi đơn đang ở trạng thái Pending và đã có ít nhất một item với tổng tiền dương.
    /// Từ Pending -> Confirmed.
    /// </summary>
    /// <exception cref="OrderDomainException">Ném khi đơn không ở Pending, chưa có item, hoặc tổng tiền bằng 0.</exception>
    public void Confirm()
    {
        // Chỉ cho xác nhận từ Pending: đơn đã hủy hoặc đã xác nhận rồi không phải là lần chốt hợp lệ tiếp theo, nên phải cấm để tránh trạng thái không rõ ràng.
        if (_status != OrderStatus.Pending)
        {
            throw new OrderDomainException("Chỉ có thể xác nhận đơn ở trạng thái Pending.");
        }

        // Đơn rỗng không có giá trị kinh doanh: xác nhận đơn không có hàng chỉ tạo ra một bản ghi trống trong hệ thống, không hợp lệ.
        if (_items.Count == 0)
        {
            throw new OrderDomainException("Không thể xác nhận đơn khi chưa có sản phẩm nào.");
        }

        // Confirm đòi tổng tiền > 0: đơn 0 đồng không có giá trị kinh doanh, xác nhận nó chỉ tạo đơn rỗng trong hệ thống.
        if (Total.Amount <= 0)
        {
            throw new OrderDomainException("Không thể xác nhận đơn khi tổng tiền bằng 0 hoặc âm.");
        }

        _status = OrderStatus.Confirmed;

        // Phát OrderConfirmedEvent: đơn vừa được xác nhận là trạng thái nghiệp vụ đã chốt, và Saga/Outbox sau này sẽ dùng nó để kích hoạt xử lý kho, thanh toán và thông báo.
        AddDomainEvent(new OrderConfirmedEvent(Id, Total, DateTime.UtcNow));
    }

    /// <summary>
    /// Hủy đơn hàng khi đơn đang ở trạng thái Pending hoặc Confirmed.
    /// Từ Pending/Confirmed -> Cancelled.
    /// </summary>
    /// <exception cref="OrderDomainException">Ném khi trạng thái hiện tại không cho phép hủy hoặc đơn đã hủy rồi.</exception>
    public void Cancel()
    {
        // Chỉ cho hủy từ Pending hoặc Confirmed: hủy là hành vi nghiệp vụ chốt trạng thái, không thể gọi khi đơn đã hoàn tất hoặc chưa tồn tại trạng thái hợp lệ.
        if (_status == OrderStatus.Cancelled)
        {
            throw new OrderDomainException("Không thể hủy đơn đã hủy trước đó.");
        }

        if (_status != OrderStatus.Pending && _status != OrderStatus.Confirmed)
        {
            throw new OrderDomainException("Chỉ có thể hủy đơn ở trạng thái Pending hoặc Confirmed.");
        }

        _status = OrderStatus.Cancelled;

        // Phát OrderCancelledEvent: đơn vừa bị hủy là sự kiện nghiệp vụ có thể kích hoạt hoàn tiền, giải phóng tồn kho hoặc cập nhật trạng thái bên ngoài.
        AddDomainEvent(new OrderCancelledEvent(Id, DateTime.UtcNow));
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    public void Apply(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        switch (domainEvent)
        {
            case OrderPlacedEvent placedEvent:
                Id = placedEvent.OrderId;
                CustomerId = placedEvent.CustomerId;
                ShippingAddress = placedEvent.ShippingAddress;
                CreatedAt = placedEvent.OccurredOn;
                _items.Clear();
                foreach (var item in placedEvent.Items)
                {
                    _items.Add(item);
                }

                _status = OrderStatus.Pending;
                Version++;
                return;
            case OrderConfirmedEvent confirmedEvent:
                if (confirmedEvent.Total != Total)
                {
                    throw new OrderDomainException("Tổng tiền của OrderConfirmedEvent không khớp với trạng thái hiện tại của aggregate.");
                }

                _status = OrderStatus.Confirmed;
                Version++;
                return;
            case OrderCancelledEvent:
                _status = OrderStatus.Cancelled;
                Version++;
                return;
            default:
                throw new OrderDomainException($"Không hỗ trợ replay event kiểu '{domainEvent.GetType().FullName}'.");
        }
    }

    private void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // Mỗi event phát sinh phải tăng version đúng 1 lần để stream event luôn phản ánh số lượng event đã xảy ra.
        // Khi aggregate mới tạo hoặc pending/confirm/cancel gọi AddDomainEvent, số version hiện tại là số event đã phát sinh nhưng chưa được persist.
        // Khi replay một event bằng Apply, chúng ta tăng version thêm 1 lần nữa để Version sau replay bằng chính số lượng event đã lưu trong stream.
        Version++;
        _domainEvents.Add(domainEvent);
    }

    private static Money CalculateTotal(IReadOnlyCollection<OrderItem> items)
    {
        if (items.Count == 0)
        {
            return new Money(0m, "USD");
        }

        var total = new Money(0m, items.First().LineTotal.Currency);
        foreach (var item in items)
        {
            total = total.Add(item.LineTotal);
        }

        return total;
    }
}
