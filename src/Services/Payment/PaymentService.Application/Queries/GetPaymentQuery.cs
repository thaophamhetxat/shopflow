// File này định nghĩa query đọc Payment trong PaymentService Application.
// Tạo ở Day 48 để API tra cứu thanh toán qua MediatR thay vì truy cập Infrastructure trực tiếp.
// Gateway Day 49 và Saga Day 52 có thể dùng query này để kiểm tra kết quả Charge.

using MediatR;

namespace PaymentService.Application.Queries;

/// <summary>
/// Yêu cầu đọc một Payment theo ID; kết quả null nếu không có khoản thanh toán tương ứng.
/// </summary>
/// <param name="PaymentId">ID thanh toán cần đọc.</param>
public sealed record GetPaymentQuery(Guid PaymentId) : IRequest<PaymentDto?>;
