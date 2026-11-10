// File này chứa behavior ghi log cho MediatR của OrderService.Application.
// Tạo ở Day 45 để mỗi command/query đều có nhật ký trước và sau khi thực thi, giúp theo dõi luồng xử lý và tìm lỗi khi validate hoặc repository fail.
// Application layer dùng behavior này để thu thập thông tin tiêu chuẩn cho mọi use case mà không làm bẩn code handler.

using MediatR;
using Microsoft.Extensions.Logging;

namespace OrderService.Application.Behaviors;

/// <summary>
/// Behavior ghi log cho tất cả request khi MediatR bắt đầu xử lý và hoàn tất xử lý, bao gồm lỗi nếu handler ném exception.
/// </summary>
/// <typeparam name="TRequest">Loại request đang được MediatR xử lý.</typeparam>
/// <typeparam name="TResponse">Loại trả về của request.</typeparam>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    /// Khởi tạo behavior với logger để ghi nhật ký cho từng request/response.
    /// </summary>
    /// <param name="logger">Logger của pipeline MediatR.</param>
    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Ghi thông tin bắt đầu xử lý, gọi handler tiếp theo và log lỗi nếu nó thất bại.
    /// </summary>
    /// <param name="request">Request đang được pipeline xử lý.</param>
    /// <param name="next">Delegate gọi tới handler tiếp theo trong pipeline.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị cancel.</param>
    /// <returns>Kết quả của request sau khi pipeline xử lý xong.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Bắt đầu xử lý request {RequestType}", typeof(TRequest).Name);

        try
        {
            var response = await next();
            _logger.LogInformation("Hoàn tất xử lý request {RequestType} với response {ResponseType}", typeof(TRequest).Name, typeof(TResponse).Name);
            return response;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Xử lý request {RequestType} thất bại", typeof(TRequest).Name);
            throw;
        }
    }
}
