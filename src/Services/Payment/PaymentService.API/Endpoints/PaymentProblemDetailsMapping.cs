// File này ánh xạ exception Payment sang RFC 9457 Problem Details.
// Tạo ở Day 48 để các endpoint Charge/Refund/GET thống nhất 404, 409, 422 và 500.
// Gateway Day 49 và Saga Day 52 nhận cùng định dạng lỗi từ Payment API.

using FluentValidation;
using PaymentService.Application;
using PaymentService.Domain.Exceptions;

namespace PaymentService.API.Endpoints;

/// <summary>
/// Chuyển exception của PaymentService thành trạng thái và nội dung Problem Details.
/// </summary>
public static class PaymentProblemDetailsMapping
{
    /// <summary>
    /// Ánh xạ lỗi không tìm thấy, xung đột nghiệp vụ, validation hoặc lỗi hệ thống.
    /// </summary>
    /// <param name="exception">Ngoại lệ cần ánh xạ.</param>
    /// <returns>Tuple gồm status code, title và detail cho phản hồi HTTP.</returns>
    public static (int StatusCode, string Title, string Detail) Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is PaymentNotFoundException)
        {
            return (404, "Not Found", exception.Message);
        }

        if (exception is PaymentDomainException)
        {
            return (409, "Conflict", exception.Message);
        }

        if (exception is ValidationException validationException)
        {
            return (422, "Unprocessable Entity", string.Join("; ", validationException.Errors.Select(error => error.ErrorMessage)));
        }

        return (500, "Internal Server Error", "Đã xảy ra lỗi không mong muốn.");
    }
}
