// File này chứa command hủy đơn hàng trong OrderService.Application.
// Tạo ở Day 41 để chuẩn hóa yêu cầu hủy đơn, dù logic lưu trữ chưa có repository nên handler vẫn là khung chạy từng bước.
// Application layer định nghĩa yêu cầu mà người gửi sẽ đưa ra, không triển khai persistence ở đây.

using MediatR;

namespace OrderService.Application.Commands;

/// <summary>
/// Command yêu cầu hủy đơn hàng bằng mã order được chỉ định.
/// API hoặc workflow gửi yêu cầu này; handler sẽ điều hướng việc hủy và không trả về dữ liệu.
/// </summary>
public sealed record CancelOrderCommand(Guid OrderId) : IRequest;
