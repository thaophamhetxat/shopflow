// File này đăng ký MediatR handler và validator của PaymentService Application.
// Tạo ở Day 48 để API nối command/query với use case mà không reference chéo sang OrderService.
// Gateway Day 49 và Saga Day 52 sẽ gửi request qua MediatR đã đăng ký tại đây.

using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentService.Application;

/// <summary>
/// Đăng ký handler và validator thuộc PaymentService Application.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký MediatR và validator trong assembly, không thêm pipeline behavior hay repository giả.
    /// </summary>
    /// <param name="services">Container dịch vụ cần bổ sung đăng ký.</param>
    /// <returns>Container sau khi đã đăng ký Application.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assembly = typeof(DependencyInjection).Assembly;
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(assembly));

        // FluentValidation DI là gói riêng; quét assembly trực tiếp để giữ đúng danh sách package đã chốt Day 48.
        foreach (var validatorType in assembly.GetTypes()
                     .Where(type => type.IsClass && !type.IsAbstract)
                     .Where(type => type.GetInterfaces().Any(
                         implemented => implemented.IsGenericType &&
                                        implemented.GetGenericTypeDefinition() == typeof(IValidator<>))))
        {
            foreach (var validatorInterface in validatorType.GetInterfaces()
                         .Where(implemented => implemented.IsGenericType &&
                                               implemented.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddTransient(validatorInterface, validatorType);
            }
        }

        return services;
    }
}
