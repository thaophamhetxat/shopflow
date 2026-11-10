// File này chứa behavior validate request trước khi handler chạy thực tế.
// Tạo ở Day 45 để mọi command đi qua FluentValidation trước khi chạm vào domain hoặc repository, giữ pipeline rõ ràng và một nhất.
// Application layer dùng behavior này để đảm bảo data sai không lọt xuống aggregate Order.

using FluentValidation;
using MediatR;

namespace OrderService.Application.Behaviors;

/// <summary>
/// Behavior chạy FluentValidation cho mỗi request trước khi handler được gọi.
/// Nếu request sai rule thì ném ValidationException và không cho handler chạm tới domain hoặc repository.
/// </summary>
/// <typeparam name="TRequest">Loại request cần validate.</typeparam>
/// <typeparam name="TResponse">Loại response của request.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>
    /// Khởi tạo behavior với tập validator đã đăng ký trong DI.
    /// </summary>
    /// <param name="validators">Danh sách validator tương ứng với request.</param>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    /// <summary>
    /// Validate request trước khi gọi next(); nếu có lỗi thì ném ValidationException mà không thực thi handler.
    /// </summary>
    /// <param name="request">Request cần kiểm tra hợp lệ.</param>
    /// <param name="next">Delegate gọi handler sau khi validation pass.</param>
    /// <param name="cancellationToken">Token hủy nếu request bị cancel.</param>
    /// <returns>Kết quả từ handler khi request hợp lệ.</returns>
    /// <exception cref="ValidationException">Ném khi có ít nhất một rule không đạt.</exception>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (_validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var failures = _validators
                .Select(validator => validator.Validate(context))
                .SelectMany(result => result.Errors)
                .Where(error => error is not null)
                .ToList();

            // ValidationBehavior chạy TRƯỚC handler: lệnh sai rule thì ném ValidationException, không bao giờ chạm tới Domain.
            if (failures.Count != 0)
            {
                throw new ValidationException(failures);
            }
        }

        return await next();
    }
}
