// File này chứa orchestrator Saga của OrderService, có nhiệm vụ reserve kho, charge tiền và confirm đơn theo một state machine đơn giản.
// Layer Application giữ logic điều phối này để Outbox worker chỉ phải trigger method `RunAsync` mà không tự xử lý nghiệp vụ của từng service.

using Microsoft.Extensions.Logging;
using OrderService.Domain.Aggregates;
using OrderService.Domain.Enums;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Sagas;

/// <summary>
/// `OrderSaga` điều phối luồng đặt hàng từ khi order được phát `OrderPlacedEvent` đến khi đơn được Confirm và lưu `saga_state` là Completed.
/// </summary>
public sealed class OrderSaga : IOrderSaga
{
    private readonly IOrderRepository _orderRepository;
    private readonly ISagaStateRepository _sagaStateRepository;
    private readonly IInventoryClient _inventoryClient;
    private readonly IPaymentClient _paymentClient;
    private readonly ILogger<OrderSaga> _logger;

    /// <summary>
    /// Khởi tạo saga với repository order, repository state và client cho Inventory/Payment.
    /// </summary>
    /// <param name="orderRepository">Repository dùng để đọc aggregate Order.</param>
    /// <param name="sagaStateRepository">Repository lưu tiến độ saga.</param>
    /// <param name="inventoryClient">Client reserve stock trong InventoryService.</param>
    /// <param name="paymentClient">Client charge tiền trong PaymentService.</param>
    /// <param name="logger">Logger dùng để trace từng bước của saga.</param>
    public OrderSaga(
        IOrderRepository orderRepository,
        ISagaStateRepository sagaStateRepository,
        IInventoryClient inventoryClient,
        IPaymentClient paymentClient,
        ILogger<OrderSaga> logger)
    {
        _orderRepository = orderRepository;
        _sagaStateRepository = sagaStateRepository;
        _inventoryClient = inventoryClient;
        _paymentClient = paymentClient;
        _logger = logger;
    }

    /// <summary>
    /// Chạy saga một lần cho orderId. Nếu đang ở `Completed`, `FailedInventory`, hoặc `FailedPayment`, method không làm thêm gì để tránh gọi lại trùng.
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
            _logger.LogWarning("Saga {OrderId} dừng ở trạng thái {State} và không retry thêm trong Day 52.", orderId, stateName);
            return;
        }

        if (string.Equals(stateName, SagaStateStatus.PendingInventory.ToString(), StringComparison.Ordinal))
        {
            _logger.LogInformation("Saga {OrderId} bắt đầu Reserve kho cho {ItemCount} item.", orderId, persistedOrder.Items.Count);

            foreach (var item in persistedOrder.Items)
            {
                var reserved = await _inventoryClient.ReserveAsync(item.ProductId, item.Quantity, cancellationToken);
                if (!reserved)
                {
                    var errorMessage = $"Reserve failed for product {item.ProductId}.";
                    await _sagaStateRepository.UpsertAsync(orderId, SagaStateStatus.FailedInventory, errorMessage, cancellationToken);
                    _logger.LogWarning("Saga {OrderId} dừng ở FailedInventory do reserve sản phẩm {ProductId} thất bại: {ErrorMessage}.", orderId, item.ProductId, errorMessage);
                    return;
                }
            }

            await _sagaStateRepository.UpsertAsync(orderId, SagaStateStatus.PendingPayment, cancellationToken);
            _logger.LogInformation("Saga {OrderId} đã reserve kho thành công, chuyển sang PendingPayment.", orderId);
        }

        if (string.Equals(stateName, SagaStateStatus.PendingPayment.ToString(), StringComparison.Ordinal) ||
            string.Equals(stateName, SagaStateStatus.PendingInventory.ToString(), StringComparison.Ordinal))
        {
            var chargeResult = await _paymentClient.ChargeAsync(
                persistedOrder.Id.Value,
                persistedOrder.Total.Amount,
                persistedOrder.Total.Currency,
                cancellationToken);

            if (chargeResult is null || !string.Equals(chargeResult.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var errorMessage = $"Payment status was {chargeResult?.Status ?? "Unknown"}.";
                await _sagaStateRepository.UpsertAsync(orderId, SagaStateStatus.FailedPayment, errorMessage, cancellationToken);
                _logger.LogWarning("Saga {OrderId} dừng ở FailedPayment vì PaymentStatus={Status}.", orderId, chargeResult?.Status ?? "Unknown");
                return;
            }

            persistedOrder.Confirm();
            await _orderRepository.SaveChangesAsync(cancellationToken);
            await _sagaStateRepository.UpsertAsync(orderId, SagaStateStatus.Completed, cancellationToken);
            _logger.LogInformation("Saga {OrderId} hoàn tất: Payment succeeded, Order đã Confirmed và state = Completed.", orderId);
        }
    }
}
