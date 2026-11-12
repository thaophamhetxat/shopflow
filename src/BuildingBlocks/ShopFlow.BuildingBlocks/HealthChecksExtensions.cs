// File này đăng ký health check phụ thuộc cho từng bounded context.
// Layer BuildingBlocks chỉ thêm check khi cấu hình tương ứng tồn tại và không ghi URI hoặc connection string.
// Day 65 gắn tag ready để các API phân biệt readiness với liveness.

using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace ShopFlow.BuildingBlocks;

/// <summary>
/// Extension đăng ký dependency health checks dùng chung cho các service ShopFlow.
/// </summary>
public static class HealthChecksExtensions
{
    /// <summary>
    /// Đăng ký PostgreSQL, Redis và RabbitMQ checks có cấu hình, tất cả gắn tag ready.
    /// </summary>
    /// <param name="services">Container dịch vụ của API.</param>
    /// <param name="dbConnectionStringFactory">Factory connection string PostgreSQL hoặc null nếu service không dùng database.</param>
    /// <param name="redisConnectionString">Connection string Redis hoặc null nếu service không dùng Redis.</param>
    /// <param name="rabbitMqUri">AMQP URI hoặc null nếu service không dùng RabbitMQ.</param>
    /// <returns>Container dịch vụ sau khi thêm các check đã cấu hình.</returns>
    public static IServiceCollection AddShopFlowHealthChecks(
        this IServiceCollection services,
        Func<IServiceProvider, string?>? dbConnectionStringFactory,
        string? redisConnectionString,
        string? rabbitMqUri)
    {
        ArgumentNullException.ThrowIfNull(services);

        var checks = services.AddHealthChecks();
        var readyTag = new[] { "ready" };

        // Database thuộc readiness vì API chỉ sẵn sàng xử lý nghiệp vụ khi PostgreSQL có thể truy cập.
        if (dbConnectionStringFactory is not null)
        {
            checks.AddNpgSql(
                serviceProvider => dbConnectionStringFactory(serviceProvider)
                    ?? throw new InvalidOperationException("PostgreSQL connection string is not configured."),
                name: "postgres",
                tags: readyTag);
        }

        // Redis chỉ được kiểm tra ở service có cấu hình sử dụng nó.
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            checks.AddRedis(redisConnectionString, name: "redis", tags: readyTag);
        }

        // RabbitMQ chỉ được kiểm tra ở service có cấu hình sử dụng broker.
        if (!string.IsNullOrWhiteSpace(rabbitMqUri))
        {
            services.AddSingleton<Task<IConnection>>(_ =>
            {
                var factory = new ConnectionFactory { Uri = new Uri(rabbitMqUri) };
                return factory.CreateConnectionAsync();
            });
            checks.AddRabbitMQ(
                serviceProvider => serviceProvider.GetRequiredService<Task<IConnection>>(),
                name: "rabbitmq",
                tags: readyTag);
        }

        return services;
    }
}
