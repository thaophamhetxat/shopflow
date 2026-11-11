// File này chứa validator cho command ProcessPayment của PaymentService Application.
// Tạo ở Day 48 để dữ liệu không hợp lệ bị từ chối trước khi tạo Payment hoặc gọi gateway.
// API Day 48 ánh xạ các lỗi này thành 422; Saga Day 52 dùng cùng quy tắc đầu vào.

using FluentValidation;

namespace PaymentService.Application.Commands;

/// <summary>
/// Kiểm tra OrderId, số tiền và mã tiền tệ của command ProcessPayment.
/// </summary>
public sealed class ProcessPaymentCommandValidator : AbstractValidator<ProcessPaymentCommand>
{
    /// <summary>
    /// Khai báo quy tắc OrderId hợp lệ, Amount dương và Currency in hoa gồm ba ký tự.
    /// </summary>
    public ProcessPaymentCommandValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEqual(Guid.Empty)
            .WithMessage("OrderId không được là Guid.Empty.");

        RuleFor(command => command.Amount)
            .GreaterThan(0)
            .WithMessage("Số tiền thanh toán phải lớn hơn 0.");

        RuleFor(command => command.Currency)
            .Must(currency => currency is { Length: 3 } &&
                              currency.All(character => character is >= 'A' and <= 'Z'))
            .WithMessage("Mã tiền tệ phải gồm đúng ba chữ cái in hoa.");
    }
}
