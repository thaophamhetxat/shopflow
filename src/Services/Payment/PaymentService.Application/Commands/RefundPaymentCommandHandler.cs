// File này thực thi use case RefundPayment của PaymentService Application.
// Tạo ở Day 48 để chỉ hoàn khoản đã Succeeded và lưu trạng thái Refunded.
// Gateway Day 49 và Saga Day 52 sẽ gọi handler này trong bước compensation.

using MediatR;
using PaymentService.Application.Queries;
using PaymentService.Domain.Repositories;
using PaymentService.Domain.ValueObjects;

namespace PaymentService.Application.Commands;

/// <summary>
/// Tải Payment, yêu cầu aggregate hoàn tiền rồi lưu trạng thái mới.
/// </summary>
public sealed class RefundPaymentCommandHandler : IRequestHandler<RefundPaymentCommand, PaymentDto>
{
    private readonly IPaymentRepository _repository;

    /// <summary>
    /// Tạo handler với repository Payment.
    /// </summary>
    /// <param name="repository">Repository dùng để đọc và lưu thanh toán.</param>
    public RefundPaymentCommandHandler(IPaymentRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Hoàn một khoản thanh toán thành công và trả trạng thái Refunded.
    /// </summary>
    /// <param name="request">Command chứa PaymentId cần hoàn.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns>PaymentDto sau khi Refund thành công.</returns>
    /// <exception cref="PaymentService.Application.PaymentNotFoundException">Ném khi Payment không tồn tại.</exception>
    /// <exception cref="PaymentService.Domain.Exceptions.PaymentDomainException">Ném khi trạng thái không phải Succeeded.</exception>
    public async Task<PaymentDto> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(PaymentId.From(request.PaymentId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentNotFoundException(request.PaymentId);
        }

        payment.Refund();
        await _repository.SaveChangesAsync(cancellationToken);

        return new PaymentDto(
            payment.Id.Value,
            payment.OrderId,
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.FailureReason);
    }
}
