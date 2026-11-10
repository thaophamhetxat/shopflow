// File này chứa handler khung cho command hủy đơn hàng.
// Tạo ở Day 41 để đặt sẵn contract cho use case hủy đơn, còn repository/service thực tế sẽ được đưa vào các ngày sau.
// Application layer này giữ thứ tự xử lý nghiệp vụ rõ ràng để sau này thêm repository mà không phá vỡ interface.

using MediatR;

namespace OrderService.Application.Commands;

/// <summary>
/// Handler khung cho CancelOrderCommand, sẽ tải Order theo OrderId rồi gọi hành vi Cancel khi repository có mặt ở Day 42.
/// </summary>
public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
{
    public Task Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        // Chưa thể tải aggregate khi Day 41 chưa có repository; ném lỗi rõ ràng thay vì giả vờ hủy thành công hoặc thất bại.
        throw new NotImplementedException("Chưa có repository để tải Order — Day 42 sẽ hoàn thiện handler này.");
    }
}
