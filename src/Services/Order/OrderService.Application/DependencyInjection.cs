// File này chứa extension method đăng ký MediatR cho OrderService.Application.
// Tạo ở Day 41 để layer Application có thể tự đăng ký handler và query/command khi service bắt đầu chạy.
// Application layer này giữ các use case và DI, không chứa logic persistence.

using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace OrderService.Application;

/// <summary>
/// Đăng ký toàn bộ handler, command và query của OrderService.Application theo assembly hiện tại.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký MediatR để toàn bộ IRequestHandler trong assembly OrderService.Application có thể được resolve khi cần xử lý use case.
    /// </summary>
    /// <param name="services">Container DI hiện tại.</param>
    /// <returns>Container DI đã được đăng ký ứng dụng Order.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        return services;
    }
}
