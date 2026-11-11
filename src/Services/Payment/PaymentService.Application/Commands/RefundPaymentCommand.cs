// File này định nghĩa command hoàn tiền trong PaymentService Application.
// Tạo ở Day 48 để thao tác Refund đi qua quy tắc chuyển trạng thái của aggregate.
// Gateway Day 49 và Saga Day 52 sẽ gọi command này khi cần bù trừ thanh toán.

using MediatR;
using PaymentService.Application.Queries;

namespace PaymentService.Application.Commands;

/// <summary>
/// Yêu cầu hoàn một Payment đã thu thành công.
/// </summary>
/// <param name="PaymentId">ID thanh toán cần hoàn.</param>
public sealed record RefundPaymentCommand(Guid PaymentId) : IRequest<PaymentDto>;
