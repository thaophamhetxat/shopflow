// File này chứa 6 test unit chứng minh Day 52: saga reserve -> charge -> confirm đúng theo state machine, dừng ở FailedInventory/FailedPayment khi bước nào thất bại.
// Layer Application dùng fake repository/client để mô phỏng từng bước mà không cần chạy Inventory/Payment thật, đủ để chứng minh logic điều phối của `OrderSaga`.
// Day 61 cập nhật fake read-model repository theo signature cursor `(CreatedAt, OrderId)`.

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

    // Test này chứng minh Evidence Day 53: Charge thất bại phải release toàn bộ inventory đã reserve rồi cancel order và lưu FailedPayment.
    [Fact]
    public async Task RunAsync_ChargeFails_ReleasesInventoryAndCancelsOrder()
    {
        var order = CreateOrderWithMultipleItems();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var reservationRepository = new FakeSagaReservationRepository();
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool>
        {
            [order.Items[0].ProductId] = true,
            [order.Items[1].ProductId] = true
        });
        var paymentClient = new FakePaymentClient(false);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, reservationRepository, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(SagaStateStatus.FailedPayment.ToString(), sagaStateRepository.GetState(order.Id.Value));
        Assert.NotNull(sagaStateRepository.GetState(order.Id.Value));
        Assert.Equal(2, inventoryClient.ReleaseCalls.Count);
        Assert.Contains(order.Items[0].ProductId, inventoryClient.ReleaseCalls);
        Assert.Contains(order.Items[1].ProductId, inventoryClient.ReleaseCalls);
    }

    // Test này chứng minh Evidence Day 53: Reserve fail ở item thứ hai chỉ bù trừ item đã reserve trước đó, không touch item chưa reserve.
    [Fact]
    public async Task RunAsync_ReserveFails_ReleasesPartialReservationsAndCancelsOrder()
    {
        var order = CreateOrderWithMultipleItems();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var reservationRepository = new FakeSagaReservationRepository();
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool>
        {
            [order.Items[0].ProductId] = true,
            [order.Items[1].ProductId] = false
        });
        var paymentClient = new FakePaymentClient(true);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, reservationRepository, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(SagaStateStatus.FailedInventory.ToString(), sagaStateRepository.GetState(order.Id.Value));
        Assert.Equal(new[] { order.Items[0].ProductId }, inventoryClient.ReleaseCalls.Distinct().ToArray());
    }

    // Test này chứng minh Evidence Day 53: compensation gọi 2 lần không làm double-release và không error khi cancel lần 2.
    [Fact]
    public async Task RunAsync_CompensationRunsTwice_DoesNotDoubleRelease()
    {
        var order = CreateOrderWithSingleItem();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var reservationRepository = new FakeSagaReservationRepository();
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool> { [order.Items[0].ProductId] = true });
        var paymentClient = new FakePaymentClient(false);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, reservationRepository, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(order.Id.Value);
        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(1, inventoryClient.ReleaseCalls.Count(r => r == order.Items[0].ProductId));
    }

    // Test này chứng minh Evidence Day 53: resume sau crash phải bỏ qua product đã reserve từ lần chạy trước.
    [Fact]
    public async Task RunAsync_ResumeAfterCrash_SkipsAlreadyReservedItems()
    {
        var order = CreateOrderWithMultipleItems();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var reservationRepository = new FakeSagaReservationRepository();
        await reservationRepository.UpsertAsync(order.Id.Value, order.Items[0].ProductId, order.Items[0].Quantity);
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool>
        {
            [order.Items[1].ProductId] = true
        });
        var paymentClient = new FakePaymentClient(true);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, reservationRepository, NullLogger<OrderSaga>.Instance);
        await sagaStateRepository.UpsertAsync(order.Id.Value, SagaStateStatus.PendingInventory);

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(SagaStateStatus.Completed.ToString(), sagaStateRepository.GetState(order.Id.Value));
        Assert.Equal(new[] { order.Items[1].ProductId }, inventoryClient.ReserveCalls);
    }

    // Test này chứng minh Evidence Day 53: timeout 30s phải compensation và đánh dấu FailedPayment với errorMessage chứa timeout.
    [Fact]
    public async Task RunAsync_SagaTimeout_CompensatesAndMarksFailed()
    {
        var order = CreateOrderWithSingleItem();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var reservationRepository = new FakeSagaReservationRepository();
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool> { [order.Items[0].ProductId] = true });
        var paymentClient = new FakePaymentClient(true, hang: true);
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, reservationRepository, NullLogger<OrderSaga>.Instance, TimeSpan.FromMilliseconds(100));

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(SagaStateStatus.FailedPayment.ToString(), sagaStateRepository.GetState(order.Id.Value));
        Assert.Contains("timeout", sagaStateRepository.GetErrorMessage(order.Id.Value), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, inventoryClient.ReleaseCalls.Count(item => item == order.Items[0].ProductId));
    }

    // Test này chứng minh Evidence Day 53: leftover FailedPayment không có row reservation sẽ release toàn bộ item và cancel đơn khi chạy lại.
    [Fact]
    public async Task RunAsync_FailedPaymentLeftover_CompensatesOnReRun()
    {
        var order = CreateOrderWithSingleItem();
        var repository = new FakeOrderRepository(order);
        var sagaStateRepository = new FakeSagaStateRepository();
        var reservationRepository = new FakeSagaReservationRepository();
        var inventoryClient = new FakeInventoryClient(new Dictionary<Guid, bool> { [order.Items[0].ProductId] = true });
        var paymentClient = new FakePaymentClient(true);
        await sagaStateRepository.UpsertAsync(order.Id.Value, SagaStateStatus.FailedPayment, "leftover");
        var saga = new OrderSaga(repository, sagaStateRepository, inventoryClient, paymentClient, reservationRepository, NullLogger<OrderSaga>.Instance);

        await saga.RunAsync(order.Id.Value);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(1, inventoryClient.ReleaseCalls.Count(item => item == order.Items[0].ProductId));
        Assert.Empty(paymentClient.Calls);
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

        Assert.Equal(OrderStatus.Cancelled, order.Status);
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

        Assert.Equal(OrderStatus.Cancelled, order.Status);
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

    private static Order CreateOrderWithMultipleItems()
    {
        var customerId = CustomerId.From(Guid.NewGuid());
        var order = Order.Place(customerId, new Address("123 Lê Lợi", "Hà Nội", "Việt Nam", "100000"));
        order.AddItem(new OrderItem(Guid.NewGuid(), 1, new Money(250000m, "VND")));
        order.AddItem(new OrderItem(Guid.NewGuid(), 2, new Money(180000m, "VND")));
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

        public Task<IReadOnlyList<OrderSummaryDto>> ListOrderSummariesByCustomerAsync(CustomerId customerId, DateTime? cursorCreatedAt, OrderId? cursorOrderId, int pageSize, CancellationToken cancellationToken = default)
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
        public Dictionary<Guid, SagaState> States { get; } = new();

        public Task<SagaState?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(States.TryGetValue(orderId, out var state) ? state : null);
        }

        public Task<SagaState?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            return GetAsync(orderId, cancellationToken);
        }

        public Task SaveAsync(SagaState state, CancellationToken cancellationToken = default)
        {
            States[state.OrderId] = state;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(Guid orderId, SagaStateStatus status, string? errorMessage = null, CancellationToken cancellationToken = default)
        {
            if (States.TryGetValue(orderId, out var existing))
            {
                existing.State = status.ToString();
                existing.ErrorMessage = errorMessage;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                States[orderId] = new SagaState { OrderId = orderId, State = status.ToString(), ErrorMessage = errorMessage, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            }

            return Task.CompletedTask;
        }

        public string? GetState(Guid orderId)
        {
            return States.TryGetValue(orderId, out var state) ? state.State : null;
        }

        public string? GetErrorMessage(Guid orderId)
        {
            return States.TryGetValue(orderId, out var state) ? state.ErrorMessage : null;
        }
    }

    private sealed class FakeInventoryClient : IInventoryClient
    {
        private readonly Dictionary<Guid, bool> _reserveResults;

        public List<Guid> Calls { get; } = new();
        public List<Guid> ReleaseCalls { get; } = new();
        public List<Guid> ReserveCalls { get; } = new();

        public FakeInventoryClient(Dictionary<Guid, bool>? results = null)
        {
            _reserveResults = results ?? new Dictionary<Guid, bool>();
        }

        public Task<bool> ReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
        {
            Calls.Add(productId);
            ReserveCalls.Add(productId);
            return Task.FromResult(_reserveResults.TryGetValue(productId, out var result) ? result : true);
        }

        public Task<bool> ReleaseAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
        {
            ReleaseCalls.Add(productId);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSagaReservationRepository : ISagaReservationRepository
    {
        private readonly Dictionary<(Guid OrderId, Guid ProductId), SagaReservation> _items = new();

        public Task<IReadOnlyList<SagaReservation>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SagaReservation>>(_items
                .Where(item => item.Key.OrderId == orderId)
                .Select(item => item.Value)
                .ToList());
        }

        public Task<SagaReservation?> GetByOrderIdAndProductIdAsync(Guid orderId, Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.TryGetValue((orderId, productId), out var item) ? item : null);
        }

        public Task SaveAsync(SagaReservation reservation, CancellationToken cancellationToken = default)
        {
            _items[(reservation.OrderId, reservation.ProductId)] = reservation;
            return Task.CompletedTask;
        }

        public Task MarkReleasedAsync(Guid orderId, Guid productId, CancellationToken cancellationToken = default)
        {
            if (_items.TryGetValue((orderId, productId), out var item))
            {
                item.MarkReleased();
            }

            return Task.CompletedTask;
        }

        public Task UpsertAsync(Guid orderId, Guid productId, int quantity, CancellationToken cancellationToken = default)
        {
            if (_items.TryGetValue((orderId, productId), out var item))
            {
                item.Quantity = quantity;
                item.Status = SagaReservationStatus.Reserved;
                item.UpdatedAt = DateTime.UtcNow;
                return Task.CompletedTask;
            }

            _items[(orderId, productId)] = SagaReservation.Create(orderId, productId, quantity);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentClient : IPaymentClient
    {
        private readonly bool _succeeded;
        private readonly bool _hang;

        public List<string> Calls { get; } = new();

        public FakePaymentClient(bool succeeded, bool hang = false)
        {
            _succeeded = succeeded;
            _hang = hang;
        }

        public async Task<PaymentChargeResult?> ChargeAsync(Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
        {
            if (_hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            Calls.Add(_succeeded ? "Succeeded" : "Failed");
            return new PaymentChargeResult(_succeeded ? "Succeeded" : "Failed");
        }
    }
}
