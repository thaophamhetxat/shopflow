// File này ánh xạ exception Inventory sang RFC 9457 Problem Details.
// Tạo ở Day 48 để các endpoint Reserve/Release/GET thống nhất 404, 409, 422 và 500.
// Gateway Day 49 và Saga Day 52 nhận cùng định dạng lỗi từ Inventory API.

using FluentValidation;
using InventoryService.Application;
using InventoryService.Domain.Exceptions;

namespace InventoryService.API.Endpoints;

/// <summary>
/// Chuyển exception của InventoryService thành trạng thái và nội dung Problem Details.
/// </summary>
public static class InventoryProblemDetailsMapping
{
    /// <summary>
    /// Ánh xạ lỗi không tìm thấy, xung đột nghiệp vụ, validation hoặc lỗi hệ thống.
    /// </summary>
    /// <param name="exception">Ngoại lệ cần ánh xạ.</param>
    /// <returns>Tuple gồm status code, title và detail cho phản hồi HTTP.</returns>
    public static (int StatusCode, string Title, string Detail) Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is ProductNotFoundException)
        {
            return (404, "Not Found", exception.Message);
        }

        if (exception is InventoryDomainException)
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
