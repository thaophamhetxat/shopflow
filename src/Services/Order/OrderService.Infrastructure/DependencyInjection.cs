// File này chứa extension method đăng ký DbContext và repository cho OrderService.Infrastructure.
// Tạo ở Day 42 để layer Infrastructure có thể được thêm vào DI container bằng một bước duy nhất khi service khởi động.
// Infrastructure này chịu trách nhiệm cung cấp dependency cho persistence, không chứa business logic hoặc API endpoint.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Domain.Repositories;
using OrderService.Infrastructure.Outbox;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Repositories;

namespace OrderService.Infrastructure;

/// <summary>
/// Đăng ký DbContext và IOrderRepository để ứng dụng Order có thể truy cập database PostgreSQL local qua EF Core.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký context và repository bằng connection string rõ ràng và ở dạng string đã có sẵn.
    /// </summary>
    /// <param name="services">Container DI hiện tại.</param>
    /// <param name="connectionString">Chuỗi kết nối PostgreSQL local dùng cho OrderDbContext.</param>
    /// <returns>Container DI sau khi đã đăng ký infrastructure cho Order.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string không được rỗng.", nameof(connectionString));
        }

        services.AddDbContext<OrderDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }

    /// <summary>
    /// Đăng ký infrastructure bằng cấu hình ứng dụng, đọc connection string OrderDb từ section ConnectionStrings.
    /// </summary>
    /// <param name="services">Container DI hiện tại.</param>
    /// <param name="configuration">Cấu hình đang chạy của ứng dụng.</param>
    /// <returns>Container DI sau khi đăng ký OrderDbContext và repository.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("OrderDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Thiếu connection string 'OrderDb' trong cấu hình application.");
        }

        return services.AddInfrastructure(connectionString);
    }
}
