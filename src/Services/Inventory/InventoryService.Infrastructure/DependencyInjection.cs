// File này đăng ký DbContext PostgreSQL và repository cho InventoryService.Infrastructure.
// Tạo ở Day 48 để API dùng database shopflow_inventory thay cho repository trong bộ nhớ.
// Gateway Day 49 và Saga Day 52 sẽ đi qua repository này khi gọi các use case Inventory.

using InventoryService.Domain.Repositories;
using InventoryService.Infrastructure.Persistence;
using InventoryService.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace InventoryService.Infrastructure;

/// <summary>
/// Đăng ký các thành phần persistence của InventoryService.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký PostgreSQL DbContext và repository scoped theo request.
    /// </summary>
    /// <param name="services">Container dịch vụ cần bổ sung đăng ký.</param>
    /// <param name="configuration">Cấu hình ứng dụng chứa ConnectionStrings:InventoryDb.</param>
    /// <returns>Container sau khi đã đăng ký Infrastructure.</returns>
    /// <exception cref="InvalidOperationException">Ném nếu connection string InventoryDb bị thiếu.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("InventoryDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Thiếu connection string 'InventoryDb' trong cấu hình.");
        }

        services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(connectionString));

        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));

        // Decorator pattern: handler chỉ resolve IProductRepository nhưng inner ProductRepository vẫn còn sẵn cho test và direct wiring.
        services.AddScoped<ProductRepository>();
        services.AddScoped<IProductRepository>(provider => new CachedInventoryRepository(
            provider.GetRequiredService<ProductRepository>(),
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<ILogger<CachedInventoryRepository>>()));

        return services;
    }
}
