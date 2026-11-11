// File này chứa orchestrator Saga của OrderService, có nhiệm vụ reserve kho, charge tiền, confirm đơn và bù trừ khi fail/timeout.
// Layer Application giữ logic điều phối này để Outbox worker chỉ phải trigger method `RunAsync` mà không tự xử lý nghiệp vụ của từng service.
// Day 53 sửa file này để thêm compensation idempotent, timeout 30 giây và guard resume bỏ qua item đã reserve từ lần chạy trước.
// Day sau có thể đọc lại `OrderSaga` để thêm resilience nhưng logic Day 53 ở đây phải giữ nguyên và đúng nghiệp vụ hiện tại.

using Microsoft.Extensions.Logging;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Enums;
using OrderService.Domain.Exceptions;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Sagas;

/// <summary>
/// `OrderSaga` điều phối luồng đặt hàng từ khi order phát `OrderPlacedEvent` cho tới khi đơn Confirm hoặc dừng ở Failed* sau compensation.
/// </summary>
public sealed class OrderSaga : IOrderSaga
{
    private readonly IOrderRepository _orderRepository;
    private readonly ISagaStateRepository _sagaStateRepository;
    private readonly IInventoryClient _inventoryClient;
    private readonly IPaymentClient _paymentClient;
    private readonly ISagaReservationRepository _sagaReservationRepository;
    private readonly ILogger<OrderSaga> _logger;
    private readonly TimeSpan _sagaTimeout;

    /// <summary>
    /// Khởi tạo saga mặc định với timeout 30 giây và repository reservation in-memory để tránh phá 6 test cũ kế thừa Day 52.
    /// </summary>
    /// <param name="orderRepository">Repository dùng để đọc aggregate Order.</param>
    /// <param name="sagaStateRepository">Repository lưu tiến độ saga.</param>
    /// <param name="inventoryClient">Client reserve/release stock trong InventoryService.</param>
    /// <param name="paymentClient">Client charge tiền trong PaymentService.</param>
    /// <param name="logger">Logger dùng để trace từng bước của saga.</param>
    public OrderSaga(
        IOrderRepository orderRepository,
        ISagaStateRepository sagaStateRepository,
        IInventoryClient inventoryClient,
        IPaymentClient paymentClient,
        ILogger<OrderSaga> logger)
        : this(orderRepository, sagaStateRepository, inventoryClient, paymentClient, new InMemorySagaReservationRepository(), logger, TimeSpan.FromSeconds(30))
    {
    }

    /// <summary>
    /// Khởi tạo saga với repository reservation thực tế và timeout tùy chọn, dùng cho app runtime và test đầu vào Day 53.
    /// </summary>
    /// <param name="orderRepository">Repository dùng để đọc aggregate Order.</param>
    /// <param name="sagaStateRepository">Repository lưu tiến độ saga.</param>
    /// <param name="inventoryClient">Client reserve/release stock trong InventoryService.</param>
    /// <param name="paymentClient">Client charge tiền trong PaymentService.</param>
    /// <param name="sagaReservationRepository">Repository lưu row `saga_reservations` cho compensation idempotent.</param>
    /// <param name="logger">Logger dùng để trace từng bước của saga.</param>
    /// <param name="sagaTimeout">Thời gian tối đa chờ saga trước khi timeout; mặc định là 30 giây.</param>
    public OrderSaga(
        IOrderRepository orderRepository,
        ISagaStateRepository sagaStateRepository,
        IInventoryClient inventoryClient,
        IPaymentClient paymentClient,
        ISagaReservationRepository sagaReservationRepository,
        ILogger<OrderSaga> logger,
        TimeSpan? sagaTimeout = null)
    {
        _orderRepository = orderRepository;
        _sagaStateRepository = sagaStateRepository;
        _inventoryClient = inventoryClient;
        _paymentClient = paymentClient;
        _sagaReservationRepository = sagaReservationRepository ?? throw new ArgumentNullException(nameof(sagaReservationRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sagaTimeout = sagaTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Chạy saga một lần cho orderId và tự xử lý compensation nếu Reserve/Charge fail hoặc timeout.
    /// </summary>
    /// <param name="orderId">Mã order cần điều phối.</param>
    /// <param name="cancellationToken">Token hủy nếu host đang shutdown.</param>
    /// <returns>Task hoàn tất sau khi order đạt trạng thái đã xử lý hoặc dừng ở failed state.</returns>
    public async Task RunAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var persistedOrder = await _orderRepository.GetByIdAsync(OrderId.From(orderId), cancellationToken);
        if (persistedOrder is null)
        {
            _logger.LogInformation("Saga bỏ qua vì order {OrderId} không tồn tại.", orderId);
            return;
        }

        if (persistedOrder.Status == OrderStatus.Confirmed)
        {
            _logger.LogInformation("Saga bỏ qua vì order {OrderId} đã ở trạng thái Confirmed.", orderId);
            return;
        }

        var sagaState = await _sagaStateRepository.GetAsync(orderId, cancellationToken) ?? SagaState.Create(orderId, SagaStateStatus.PendingInventory);
        var stateName = sagaState.State;

        if (string.Equals(stateName, SagaStateStatus.Completed.ToString(), StringComparison.Ordinal))
        {
            _logger.LogInformation("Saga {OrderId} đã ở trạng thái Completed; không gọi lại Reserve/Charge/Confirm.", orderId);
            return;
        }

        if (string.Equals(stateName, SagaStateStatus.FailedInventory.ToString(), StringComparison.Ordinal) ||
            string.Equals(stateName, SagaStateStatus.FailedPayment.ToString(), StringComparison.Ordinal))
        {
            if (persistedOrder.Status == OrderStatus.Cancelled)
            {
                _logger.LogInformation("Saga {OrderId} đã ở trạng thái {State} và đơn đã Cancelled; bỏ qua retry thêm.", orderId, stateName);
                return;
            }

            if (persistedOrder.Status == OrderStatus.Pending)
            {
                _logger.LogWarning("Saga {OrderId} đang ở trạng thái {State} nhưng đơn vẫn Pending; Day 53 bù trừ một lần trước khi return.", orderId, stateName);

                var existingReservations = await _sagaReservationRepository.GetByOrderIdAsync(orderId, cancellationToken);
                if (existingReservations.Count > 0)
                {
                    await CompensateAsync(persistedOrder, cancellationToken);
                    return;
                }

                if (string.Equals(stateName, SagaStateStatus.FailedPayment.ToString(), StringComparison.Ordinal))
                {
                    foreach (var item in persistedOrder.Items)
                    {
                        try
                        {
                            await _inventoryClient.ReleaseAsync(item.ProductId, item.Quantity, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Release inventory fallback thất bại cho order {OrderId}, product {ProductId} khi chữa leftover FailedPayment.", orderId, item.ProductId);
                        }
                    }

                    if (persistedOrder.Status == OrderStatus.Pending)
                    {
                        persistedOrder.Cancel();
                        await _orderRepository.SaveChangesAsync(cancellationToken);
                    }

                    return;
                }

                if (persistedOrder.Status == OrderStatus.Pending)
                {
                    _logger.LogWarning("Saga {OrderId} ở FailedInventory nhưng không có row reservation nào; không auto release vì sẽ cộng thừa stock, chỉ cancel đơn và yêu cầu xử lý tay.", orderId);
                    persistedOrder.Cancel();
                    await _orderRepository.SaveChangesAsync(cancellationToken);
                }

                return;
            }
        }

        var phase = SagaStateStatus.PendingInventory;

        try
        {
            if (string.Equals(stateName, SagaStateStatus.PendingInventory.ToString(), StringComparison.Ordinal))
            {
                _logger.LogInformation("Saga {OrderId} bắt đầu Reserve kho cho {ItemCount} item.", orderId, persistedOrder.Items.Count);

                foreach (var item in persistedOrder.Items)
                {
                    var existingReservation = await _sagaReservationRepository.GetByOrderIdAndProductIdAsync(orderId, item.ProductId, cancellationToken);
                    if (existingReservation is not null && existingReservation.Status == SagaReservationStatus.Reserved)
                    {
                        _logger.LogInformation("Saga {OrderId} bỏ qua product {ProductId} vì item này đã reserve từ lần chạy trước.", orderId, item.ProductId);
                        continue;
                    }

                    if (existingReservation is not null && existingReservation.Status == SagaReservationStatus.Released)
                    {
                        _logger.LogInformation("Saga {OrderId} bỏ qua product {ProductId} vì item này đã release trong compensation trước đó.", orderId, item.ProductId);
                        continue;
                    }

                    var reserved = await _inventoryClient.ReserveAsync(item.ProductId, item.Quantity, cancellationToken);
                    if (!reserved)
                    {
                        phase = SagaStateStatus.FailedInventory;
                        var errorMessage = $"Reserve failed for product {item.ProductId}.";
                        await CompensateAsync(persistedOrder, cancellationToken);
                        await _sagaStateRepository.UpsertAsync(orderId, SagaStateStatus.FailedInventory, errorMessage: errorMessage, cancellationToken: cancellationToken);
                        _logger.LogWarning("Saga {OrderId} dừng ở FailedInventory do reserve sản phẩm {ProductId} thất bại: {ErrorMessage}.", orderId, item.ProductId, errorMessage);
                        return;
                    }

                    await _sagaReservationRepository.UpsertAsync(orderId, item.ProductId, item.Quantity, cancellationToken);
                }

                await _sagaStateRepository.UpsertAsync(orderId, SagaStateStatus.PendingPayment, cancellationToken: cancellationToken);
                _logger.LogInformation("Saga {OrderId} đã reserve kho thành công, chuyển sang PendingPayment.", orderId);
            }

            if (string.Equals(stateName, SagaStateStatus.PendingPayment.ToString(), StringComparison.Ordinal) ||
                string.Equals(stateName, SagaStateStatus.PendingInventory.ToString(), StringComparison.Ordinal))
            {
                phase = SagaStateStatus.PendingPayment;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_sagaTimeout);

                var chargeResult = await _paymentClient.ChargeAsync(
                    persistedOrder.Id.Value,
                    persistedOrder.Total.Amount,
                    persistedOrder.Total.Currency,
                    timeoutCts.Token);

                if (chargeResult is null || !string.Equals(chargeResult.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    var errorMessage = $"Payment status was {chargeResult?.Status ?? "Unknown"}.";
                    await CompensateAsync(persistedOrder, cancellationToken);
                    await _sagaStateRepository.UpsertAsync(orderId, SagaStateStatus.FailedPayment, errorMessage: errorMessage, cancellationToken: cancellationToken);
                    _logger.LogWarning("Saga {OrderId} dừng ở FailedPayment vì PaymentStatus={Status}.", orderId, chargeResult?.Status ?? "Unknown");
                    return;
                }

                persistedOrder.Confirm();
                await _orderRepository.SaveChangesAsync(cancellationToken);
                await _sagaStateRepository.UpsertAsync(orderId, SagaStateStatus.Completed, cancellationToken: cancellationToken);
                _logger.LogInformation("Saga {OrderId} hoàn tất: Payment succeeded, Order đã Confirmed và state = Completed.", orderId);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutPhase = phase == SagaStateStatus.PendingPayment ? SagaStateStatus.FailedPayment : SagaStateStatus.FailedInventory;
            var errorMessage = $"Saga timeout after {_sagaTimeout.TotalSeconds:0}s during {phase}.";
            await CompensateAsync(persistedOrder, CancellationToken.None);
            await _sagaStateRepository.UpsertAsync(orderId, timeoutPhase, errorMessage: errorMessage, cancellationToken: CancellationToken.None);
            _logger.LogWarning("Saga {OrderId} timeout sau {Timeout} giây ở phase {Phase}; chuẩn bị compensation và ghi Failed.*.", orderId, _sagaTimeout.TotalSeconds, phase);
        }
        catch (Exception ex)
        {
            var failedPhase = phase == SagaStateStatus.PendingPayment ? SagaStateStatus.FailedPayment : SagaStateStatus.FailedInventory;
            var errorMessage = ex.Message;
            await CompensateAsync(persistedOrder, CancellationToken.None);
            await _sagaStateRepository.UpsertAsync(orderId, failedPhase, errorMessage: errorMessage, cancellationToken: CancellationToken.None);
            _logger.LogError(ex, "Saga {OrderId} gặp lỗi ở phase {Phase}; compensation đã chạy và state đã chuyển sang {FailedPhase}.", orderId, phase, failedPhase);
        }
    }

    private async Task CompensateAsync(Order order, CancellationToken cancellationToken)
    {
        var reservations = await _sagaReservationRepository.GetByOrderIdAsync(order.Id.Value, cancellationToken);
        var reservedReservations = reservations
            .Where(reservation => reservation.Status == SagaReservationStatus.Reserved)
            .ToList();

        foreach (var reservation in reservedReservations)
        {
            try
            {
                var released = await _inventoryClient.ReleaseAsync(reservation.ProductId, reservation.Quantity, cancellationToken);
                if (released)
                {
                    await _sagaReservationRepository.MarkReleasedAsync(order.Id.Value, reservation.ProductId, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Compensation order {OrderId} không release được product {ProductId} với quantity {Quantity}; row giữ nguyên Reserved để retry sau.", order.Id.Value, reservation.ProductId, reservation.Quantity);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Compensation order {OrderId} gặp lỗi khi release product {ProductId}; row giữ nguyên Reserved để retry sau.", order.Id.Value, reservation.ProductId);
            }
        }

        if (order.Status == OrderStatus.Pending)
        {
            try
            {
                order.Cancel();
                await _orderRepository.SaveChangesAsync(cancellationToken);
            }
            catch (OrderDomainException ex)
            {
                _logger.LogInformation(ex, "Compensation order {OrderId} bỏ qua cancel vì đơn không còn ở trạng thái Pending.", order.Id.Value);
            }
        }
    }

    private sealed class InMemorySagaReservationRepository : ISagaReservationRepository
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
}

