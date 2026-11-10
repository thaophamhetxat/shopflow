// File này chứa handler hủy đơn hàng của OrderService.Application.
// Tạo ở Day 45 để thực thi hành vi hủy trên aggregate đã được lưu trong PostgreSQL.
// Application layer này kiểm tra tồn tại Order trước khi gọi Cancel để tránh hủy đơn không có thật.

using MediatR;
using OrderService.Domain.Repositories;
using OrderService.Domain.ValueObjects;

namespace OrderService.Application.Commands;

/// <summary>
/// Handler cho CancelOrderCommand, nạp Order theo OrderId, gọi domain Cancel khi order tồn tại và lưu lại trạng thái mới.
/// Nếu không tìm thấy đơn tương ứng thì ném OrderNotFoundException để caller biết đây là lỗi nghiệp vụ rõ ràng.
/// </summary>
public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
{
    private readonly IOrderRepository _orderRepository;

    /// <summary>
    /// Khởi tạo handler với repository để đọc và lưu trạng thái Order trong PostgreSQL.
    /// </summary>
    /// <param name="orderRepository">Repository thực thi đọc và save aggregate Order.</param>
    public CancelOrderCommandHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    /// <summary>
    /// Tải Order theo OrderId và gọi Order.Cancel(); nếu không có đơn thì ném lỗi không tìm thấy.
    /// </summary>
    /// <param name="request">Command yêu cầu hủy order bằng Id.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị hủy giữa chừng.</param>
    public async Task Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(request.OrderId);
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            throw new OrderNotFoundException(request.OrderId);
        }

        // Gọi domain Cancel trên aggregate đã có: trạng thái chỉ được chuyển từ Pending/Confirmed sang Cancelled, không có state sai/phải trả về null.
        order.Cancel();
        await _orderRepository.SaveChangesAsync(cancellationToken);
    }
}
