// File này thực thi query đọc Payment trong PaymentService Application.
// Tạo ở Day 48 để query không thay đổi aggregate và trả null nếu ID chưa tồn tại.
// Gateway Day 49 và Saga Day 52 sẽ nhận DTO thay vì tham chiếu entity EF.

using MediatR;
using PaymentService.Domain.Repositories;
using PaymentService.Domain.ValueObjects;

namespace PaymentService.Application.Queries;

/// <summary>
/// Đọc một Payment theo ID và chuyển thành DTO; trả null khi không tìm thấy.
/// </summary>
public sealed class GetPaymentQueryHandler : IRequestHandler<GetPaymentQuery, PaymentDto?>
{
    private readonly IPaymentRepository _repository;

    /// <summary>
    /// Tạo handler với repository Payment.
    /// </summary>
    /// <param name="repository">Repository dùng để đọc thanh toán.</param>
    public GetPaymentQueryHandler(IPaymentRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Tải Payment theo ID và trả DTO hoặc null nếu ID chưa tồn tại.
    /// </summary>
    /// <param name="request">Query chứa PaymentId cần đọc.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns>DTO thanh toán nếu tìm thấy; ngược lại null.</returns>
    public async Task<PaymentDto?> Handle(GetPaymentQuery request, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdReadOnlyAsync(PaymentId.From(request.PaymentId), cancellationToken);
        return payment is null
            ? null
            : new PaymentDto(
                payment.Id.Value,
                payment.OrderId,
                payment.Amount,
                payment.Currency,
                payment.Status.ToString(),
                payment.FailureReason);
    }
}
