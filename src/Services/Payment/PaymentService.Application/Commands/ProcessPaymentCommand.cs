// File này định nghĩa command thu tiền cho PaymentService Application.
// Tạo ở Day 48 để API chuyển yêu cầu thanh toán thành một use case MediatR có kiểu rõ ràng.
// Gateway Day 49 và Saga Day 52 sẽ gọi command này khi xử lý đơn hàng.

using MediatR;
using PaymentService.Application.Queries;

namespace PaymentService.Application.Commands;

/// <summary>
/// Yêu cầu tạo một khoản thanh toán và gửi thông tin cho gateway.
/// </summary>
/// <param name="OrderId">ID đơn hàng cần thanh toán.</param>
/// <param name="Amount">Số tiền cần thu.</param>
/// <param name="Currency">Mã tiền tệ gồm ba chữ cái in hoa.</param>
public sealed record ProcessPaymentCommand(Guid OrderId, decimal Amount, string Currency) : IRequest<PaymentDto>;
