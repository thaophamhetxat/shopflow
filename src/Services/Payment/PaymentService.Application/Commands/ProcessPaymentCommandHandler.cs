// File này thực thi use case ProcessPayment của PaymentService Application.
// Tạo ở Day 48 để lưu khoản thanh toán, gọi gateway và lưu trạng thái Succeeded/Failed.
// Gateway Day 49 và Saga Day 52 sẽ gọi handler này; gateway từ chối vẫn là kết quả nghiệp vụ.

using FluentValidation;
using MediatR;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Queries;
using PaymentService.Domain.Aggregates;
using PaymentService.Domain.Repositories;
using PaymentService.Domain.ValueObjects;
using ShopFlow.BuildingBlocks;

namespace PaymentService.Application.Commands;

/// <summary>
/// Xử lý thanh toán mới; kết quả gateway thất bại được trả về dưới dạng PaymentDto Failed.
/// </summary>
public sealed class ProcessPaymentCommandHandler : IRequestHandler<ProcessPaymentCommand, PaymentDto>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IValidator<ProcessPaymentCommand> _validator;

    /// <summary>
    /// Tạo handler với repository, gateway và validator của ProcessPayment.
    /// </summary>
    /// <param name="repository">Repository lưu trạng thái Payment.</param>
    /// <param name="gateway">Cổng thực hiện thao tác Charge.</param>
    /// <param name="validator">Validator của command.</param>
    public ProcessPaymentCommandHandler(
        IPaymentRepository repository,
        IPaymentGateway gateway,
        IValidator<ProcessPaymentCommand> validator)
    {
        _repository = repository;
        _gateway = gateway;
        _validator = validator;
    }

    /// <summary>
    /// Validate, tạo Payment, gọi gateway, cập nhật trạng thái và trả DTO.
    /// </summary>
    /// <param name="request">Yêu cầu OrderId, số tiền và tiền tệ cần thu.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns>PaymentDto ở trạng thái Succeeded hoặc Failed.</returns>
    /// <exception cref="ValidationException">Ném khi dữ liệu đầu vào không hợp lệ.</exception>
    public async Task<PaymentDto> Handle(ProcessPaymentCommand request, CancellationToken cancellationToken)
    {
        using var activity = ShopFlowActivitySource.Source.StartActivity(ShopFlowActivitySource.PaymentProcess);
        activity?.SetTag("payment.amount", request.Amount.ToString());
        activity?.SetTag("payment.currency", request.Currency);
        activity?.SetTag("order.id", request.OrderId.ToString());

        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        var payment = Payment.Create(
            PaymentId.New(),
            request.OrderId,
            request.Amount,
            request.Currency);
        await _repository.AddAsync(payment, cancellationToken);

        var result = await _gateway.ChargeAsync(
            request.OrderId,
            request.Amount,
            request.Currency,
            cancellationToken);

        if (result.Succeeded)
        {
            payment.MarkSucceeded();
        }
        else
        {
            payment.MarkFailed(result.FailureReason ?? "Thanh toán thất bại.");
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return ToDto(payment);
    }

    /// <summary>Chuyển aggregate Payment thành DTO cho API.</summary>
    /// <param name="payment">Aggregate cần ánh xạ.</param>
    /// <returns>DTO chứa ID, OrderId, tiền, trạng thái và lý do thất bại.</returns>
    private static PaymentDto ToDto(Payment payment)
    {
        return new PaymentDto(
            payment.Id.Value,
            payment.OrderId,
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.FailureReason);
    }
}
