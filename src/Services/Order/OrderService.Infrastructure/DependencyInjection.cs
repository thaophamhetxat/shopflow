// File này chứa extension method đăng ký DbContext và repository cho OrderService.Infrastructure.
// Tạo ở Day 42 để layer Infrastructure có thể được thêm vào DI container bằng một bước duy nhất khi service khởi động.
// Infrastructure này chịu trách nhiệm cung cấp dependency cho persistence, không chứa business logic hoặc API endpoint.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderService.Application.Idempotency;
using OrderService.Application.Queries;
using OrderService.Application.Sagas;
using OrderService.Domain.Repositories;
using OrderService.Infrastructure.Idempotency;
using OrderService.Infrastructure.Outbox;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Repositories;
using OrderService.Infrastructure.Sagas;

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

        services.RemoveAll<IOrderRepository>();
        services.RemoveAll<IIdempotencyStore>();
        services.RemoveAll<IOrderSaga>();
        services.RemoveAll<ISagaStateRepository>();
        services.RemoveAll<ISagaReservationRepository>();
        services.RemoveAll<IInventoryClient>();
        services.RemoveAll<IPaymentClient>();

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderReadModelRepository, OrderRepository>();
        services.AddScoped<IOrderSaga, OrderSaga>();
        services.AddScoped<ISagaStateRepository, SagaStateRepository>();
        // Day 53 cần lưu row `saga_reservations` qua repository thực tế để resume lại sau crash không ghi trùng reserve và compensation đọc đúng dữ liệu DB.
        // Nếu thiếu đăng ký này, DI sẽ rơi về constructor 5 tham số dùng repo in-memory, nên hành vi chạy ngoài app khác với unit test và sẽ sai ở retry/crash.
        services.AddScoped<ISagaReservationRepository, SagaReservationRepository>();
        services.AddScoped<IInventoryClient, InventoryHttpClient>();
        services.AddScoped<IPaymentClient, PaymentHttpClient>();

        services.AddHttpClient("InventoryService", client =>
        {
            client.BaseAddress = new Uri("http://localhost:5140");
        });

        services.AddHttpClient("PaymentService", client =>
        {
            client.BaseAddress = new Uri("http://localhost:5199");
        });

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

        services.RemoveAll<IOrderRepository>();
        services.RemoveAll<IIdempotencyStore>();
        services.RemoveAll<IOrderSaga>();
        services.RemoveAll<ISagaStateRepository>();
        services.RemoveAll<ISagaReservationRepository>();
        services.RemoveAll<IInventoryClient>();
        services.RemoveAll<IPaymentClient>();

        var inventoryBaseUrl = configuration["Saga:InventoryBaseUrl"] ?? "http://localhost:5140";
        var paymentBaseUrl = configuration["Saga:PaymentBaseUrl"] ?? "http://localhost:5199";

        services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderReadModelRepository, OrderRepository>();
        services.AddScoped<IOrderSaga, OrderSaga>();
        services.AddScoped<ISagaStateRepository, SagaStateRepository>();
        // Day 53 cần lưu row `saga_reservations` qua repository thực tế để resume lại sau crash không ghi trùng reserve và compensation đọc đúng dữ liệu DB.
        // Nếu thiếu đăng ký này, DI sẽ rơi về constructor 5 tham số dùng repo in-memory, nên hành vi chạy ngoài app khác với unit test và sẽ sai ở retry/crash.
        services.AddScoped<ISagaReservationRepository, SagaReservationRepository>();
        services.AddScoped<IInventoryClient, InventoryHttpClient>();
        services.AddScoped<IPaymentClient, PaymentHttpClient>();
        services.AddHttpClient("InventoryService", client =>
        {
            client.BaseAddress = new Uri(inventoryBaseUrl);
        });
        services.AddHttpClient("PaymentService", client =>
        {
            client.BaseAddress = new Uri(paymentBaseUrl);
        });
        services.AddScoped<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
