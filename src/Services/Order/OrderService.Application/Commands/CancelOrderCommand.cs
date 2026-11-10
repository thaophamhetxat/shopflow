// File này chứa command hủy đơn hàng trong OrderService.Application.
// Tạo ở Day 41 để chuẩn hóa yêu cầu hủy đơn, dù logic lưu trữ chưa có repository nên handler vẫn là khung chạy từng bước.
// Application layer định nghĩa yêu cầu mà người gửi sẽ đưa ra, không triển khai persistence ở đây.

using MediatR;

namespace OrderService.Application.Commands;

/// <summary>
/// Command yêu cầu hủy đơn hàng bằng mã order được chỉ định.
/// Command này là request nghiệp vụ từ API hoặc workflow, và handler phụ trách lần lượt điều hướng tới aggregate hoặc service tương ứng.
/// </summary>
public sealed record CancelOrderCommand(Guid OrderId) : IRequest<bool>;
