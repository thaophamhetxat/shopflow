// File này chứa extension method đăng ký DbContext và repository cho OrderService.Infrastructure.
// Tạo ở Day 42 để layer Infrastructure có thể được thêm vào DI container bằng một bước duy nhất khi service khởi động.
// Infrastructure này chịu trách nhiệm cung cấp dependency cho persistence, không chứa business logic hoặc API endpoint.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Idempotency;
using OrderService.Domain.Repositories;
using OrderService.Infrastructure.Idempotency;
using OrderService.Infrastructure.Outbox;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Repositories;

namespace OrderService.Infrastructure;

/// <summary>
/// File này chứa extension method đăng ký DbContext, repository, Redis cache và store idempotency cho OrderService.Infrastructure.
/// Layer Infrastructure dùng nó để gắn PostgreSQL + Redis thật qua DI, phục vụ handler Day 45.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký PostgreSQL, repository và Redis idempotency bằng connection string rõ ràng.
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
        services.AddStackExchangeRedisCache(options => options.Configuration = "localhost:6379");
        services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }

    /// <summary>
    /// Đăng ký infrastructure bằng cấu hình ứng dụng, đọc connection string OrderDb và Redis từ section ConnectionStrings.
    /// </summary>
    /// <param name="services">Container DI hiện tại.</param>
    /// <param name="configuration">Cấu hình đang chạy của ứng dụng.</param>
    /// <returns>Container DI sau khi đăng ký OrderDbContext, Redis cache và repository.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("OrderDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Thiếu connection string 'OrderDb' trong cấu hình application.");
        }

        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";

        services.AddDbContext<OrderDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
