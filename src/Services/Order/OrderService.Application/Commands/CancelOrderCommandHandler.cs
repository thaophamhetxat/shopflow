// File này chứa handler khung cho command hủy đơn hàng.
// Tạo ở Day 41 để đặt sẵn contract cho use case hủy đơn, còn repository/service thực tế sẽ được đưa vào các ngày sau.
// Application layer này giữ thứ tự xử lý nghiệp vụ rõ ràng để sau này thêm repository mà không phá vỡ interface.

using MediatR;

namespace OrderService.Application.Commands;

/// <summary>
/// Handler khung cho CancelOrderCommand: bước đầu là lấy order theo OrderId, bước tiếp theo là gọi Aggregate.Cancel, cuối cùng mới trả về trạng thái thành công.
/// Hiện tại chưa có repository nên handler trả về giá trị mặc định và chờ triển khai thực tế ở ngày sau.
/// </summary>
public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, bool>
{
    public Task<bool> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
