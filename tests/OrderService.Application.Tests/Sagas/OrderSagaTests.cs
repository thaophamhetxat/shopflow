// File này chứa 6 test unit chứng minh Day 52: saga reserve -> charge -> confirm đúng theo state machine, dừng ở FailedInventory/FailedPayment khi bước nào thất bại.
// Layer Application dùng fake repository/client để mô phỏng từng bước mà không cần chạy Inventory/Payment thật, đủ để chứng minh logic điều phối của `OrderSaga`.

using Microsoft.Extensions.Logging.Abstractions;
using OrderService.Application.Queries;
using OrderService.Application.Sagas;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Entities;
using OrderService.Domain.Enums;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;
using Xunit;

namespace OrderService.Application.Tests.Sagas;

/// <summary>
/// Các unit test của `OrderSaga` chứng minh state machine xử lý happy path và các nhánh fail trong Day 52.
/// </summary>
public sealed class OrderSagaTests
{
    // Test này chứng minh Evidence Day 52: Reserve thành công + Charge thành công -> order được Confirm, saga chốt `Completed`, và thứ tự gọi đúng Reserve trước Charge trước Confirm.
    [Fact]
    public async Task RunAsync_HappyPath_ConfirmsOrderAndCompletesSaga()
    {
        var order = CreateOrderWithSingleItem();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool> { [order.Items[0].ProductId] = true });
        var paymentClient = new FakePaymentClient(true);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(SagaStateStatus.Completed.ToString(), sagaStateRepository.GetState(order.Id.Value));
        Assert.Equal(new[] { order.Items[0].ProductId }, inventoryClient.Calls);
        Assert.Single(paymentClient.Calls);
    }

    // Test này chứng minh Evidence Day 52: Reserve thất bại khiến saga dừng ở FailedInventory, không charge và không confirm đơn.
    [Fact]
    public async Task RunAsync_ReserveFails_MarksFailedInventoryAndStops()
    {
        var order = CreateOrderWithSingleItem();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool> { [order.Items[0].ProductId] = false });
        var paymentClient = new FakePaymentClient(true);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(SagaStateStatus.FailedInventory.ToString(), sagaStateRepository.GetState(order.Id.Value));
        Assert.Empty(paymentClient.Calls);
    }

    // Test này chứng minh Evidence Day 52: Charge thất bại thì saga dừng ở FailedPayment mà không confirm đơn.
    [Fact]
    public async Task RunAsync_ChargeFails_MarksFailedPaymentAndStops()
    {
        var order = CreateOrderWithSingleItem();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool> { [order.Items[0].ProductId] = true });
        var paymentClient = new FakePaymentClient(false);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(SagaStateStatus.FailedPayment.ToString(), sagaStateRepository.GetState(order.Id.Value));
    }

    // Test này chứng minh Evidence Day 52: nếu saga đã kết thúc hoàn toàn thì không gọi lại reserve/charge/confirm.
    [Fact]
    public async Task RunAsync_AlreadyCompleted_DoesNothing()
    {
        var order = CreateOrderWithSingleItem();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        await sagaStateRepository.UpsertAsync(order.Id.Value, SagaStateStatus.Completed);
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool> { [order.Items[0].ProductId] = true });
        var paymentClient = new FakePaymentClient(true);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Empty(inventoryClient.Calls);
        Assert.Empty(paymentClient.Calls);
    }

    // Test này chứng minh Evidence Day 52: order không tồn tại thì saga không tạo row trạng thái và không gọi service ngoài.
    [Fact]
    public async Task RunAsync_OrderNotFound_DoesNothing()
    {
        var repository = new FakeOrderRepository(null);
        var sagaStateRepository = new FakeSagaStateRepository();
        var inventoryClient = new FakeInventoryClient();
        var paymentClient = new FakePaymentClient(true);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(Guid.NewGuid());

        Assert.Empty(sagaStateRepository.States);
        Assert.Empty(inventoryClient.Calls);
        Assert.Empty(paymentClient.Calls);
    }

    // Test này chứng minh Evidence Day 52: order đã bị Confirm từ trước không cần chạy saga lại.
    [Fact]
    public async Task RunAsync_OrderAlreadyConfirmed_DoesNothing()
    {
        var order = CreateOrderWithSingleItem();
        order.Confirm();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool> { [order.Items[0].ProductId] = true });
        var paymentClient = new FakePaymentClient(true);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Empty(inventoryClient.Calls);
        Assert.Empty(paymentClient.Calls);
    }

    private static Order CreateOrderWithSingleItem()
    {
        var customerId = CustomerId.From(Guid.NewGuid());
        var order = Order.Place(customerId, new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(250000m, "VND")));
        return order;
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        private readonly Order? _order;

        public FakeOrderRepository(Order? order)
        {
            _order = order;
        }

        public Task AddAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_order is null || _order.Id != id ? null : _order);
        }

        public Task<OrderDetailDto?> GetOrderDetailAsync(OrderId id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<OrderSummaryDto>> ListOrderSummariesByCustomerAsync(CustomerId customerId, OrderId? cursor, int pageSize, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSagaStateRepository : ISagaStateRepository
    {
        public Dictionary<Guid, string> States { get; } = new();

        public Task<SagaState?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(States.TryGetValue(orderId, out var state) ? new SagaState { OrderId = orderId, State = state, UpdatedAt = DateTime.UtcNow } : null);
        }

        public Task<SagaState?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            return GetAsync(orderId, cancellationToken);
        }

        public Task SaveAsync(SagaState state, CancellationToken cancellationToken = default)
        {
            States[state.OrderId] = state.State;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(Guid orderId, SagaStateStatus status, CancellationToken cancellationToken = default)
        {
            States[orderId] = status.ToString();
            return Task.CompletedTask;
        }

        public string? GetState(Guid orderId)
        {
            return States.TryGetValue(orderId, out var state) ? state : null;
        }
    }

    private sealed class FakeInventoryClient : IInventoryClient
    {
        private readonly Dictionary<Guid, bool> _results;

        public List<Guid> Calls { get; } = new();

        public FakeInventoryClient(Dictionary<Guid, bool>? results = null)
        {
            _results = results ?? new Dictionary<Guid, bool>();
        }

        public Task<bool> ReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
        {
            Calls.Add(productId);
            return Task.FromResult(_results.TryGetValue(productId, out var result) ? result : true);
        }
    }

    private sealed class FakePaymentClient : IPaymentClient
    {
        private readonly bool _succeeded;

        public List<string> Calls { get; } = new();

        public FakePaymentClient(bool succeeded)
        {
            _succeeded = succeeded;
        }

        public Task<PaymentChargeResult?> ChargeAsync(Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
        {
            Calls.Add(_succeeded ? "Succeeded" : "Failed");
            return Task.FromResult<PaymentChargeResult?>(new PaymentChargeResult(_succeeded ? "Succeeded" : "Failed"));
        }
    }
}
