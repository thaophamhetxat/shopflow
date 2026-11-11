// File này chứa mapper thuần cho exception sang RFC 9457 Problem Details, dễ unit test gọi trực tiếp mà không cần khởi động API.
// Tạo ở Day 47 để API v1/v2 có cùng phản hồi lỗi chuẩn, đồng thời chuẩn hóa title/status cho 404, 409, 422 và 500.
using FluentValidation;
using OrderService.Application;
using OrderService.Domain.Exceptions;

namespace OrderService.API.Endpoints;

/// <summary>
/// Mapper riêng để chuyển exception thành tuple status/title/detail chuẩn cho Problem Details.
/// Method này thuần, không phụ thuộc HttpContext hay builder, nên test unit có thể gọi dễ dàng.
/// </summary>
public static class ProblemDetailsMapping
{
    /// <summary>
    /// Map exception sang mã trạng thái, tiêu đề và chi tiết phù hợp với RFC 9457, dùng cho endpoint API và unit test.
    /// </summary>
    /// <param name="exception">Ngoại lệ cần chuyển thành phản hồi lỗi.</param>
    /// <param name="instancePath">Đường dẫn request đang gặp lỗi để đặt vào field instance của Problem Details.</param>
    /// <returns>Tuple gồm statusCode, title và detail để dùng trong Results.Problem.</returns>
    public static (int StatusCode, string Title, string Detail) Map(Exception exception, string instancePath)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // OrderNotFoundException → 404: tài nguyên client hỏi không tồn tại, không phải lỗi server.
        if (exception is OrderNotFoundException)
        {
            return (404, "Not Found", exception.Message);
        }

        // OrderDomainException → 409: đơn đã ở trạng thái không cho phép thao tác này, nên client gửi lại y hệt vẫn là conflict, không phải bad request.
        if (exception is OrderDomainException)
        {
            return (409, "Conflict", exception.Message);
        }

        // ValidationException → 422: input JSON đúng hình thức nhưng rule nghiệp vụ không đạt, nên không coi là bad request hay server error.
        if (exception is ValidationException validationException)
        {
            var detail = string.Join("; ", validationException.Errors.Select(error => error.ErrorMessage));
            return (422, "Unprocessable Entity", detail);
        }

        // Exception khác → 500: lộ stack trace hoặc connection string là bảo mật, nên API chỉ trả thông báo tổng quát đã chốt ở Day 47.
        return (500, "Internal Server Error", "Đã xảy ra lỗi không mong muốn.");
    }
}
