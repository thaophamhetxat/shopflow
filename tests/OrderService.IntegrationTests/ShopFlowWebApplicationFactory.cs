// File này tạo WebApplicationFactory cho Order API và dựng PostgreSQL riêng bằng Testcontainers.
// Các test dùng schema thật, handler, repository và migrations thật thay vì mock các tầng xử lý.
// Tạo ở Day 50; đây là nền để kiểm chứng luồng HTTP của Order trước khi Saga được nối ở Day 52.
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OrderService.IntegrationTests;

/// <summary>
/// Factory dùng chung cho test HTTP Order API với PostgreSQL 16 riêng do Testcontainers quản lý.
/// </summary>
public sealed class ShopFlowWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    /// <summary>
    /// Tạo container PostgreSQL 16 cho cả class test để tránh khởi động một database cho từng test.
    /// </summary>
    public ShopFlowWebApplicationFactory()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("shopflow_order")
            .WithUsername("shopflow")
            // Mật khẩu này chỉ dành cho PostgreSQL dùng trong container test, không phải thông tin production.
            .WithPassword("shopflow123")
            .Build();
    }

    /// <summary>
    /// Khởi động database test và áp dụng mọi migration trước khi các request HTTP được gửi.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    /// <summary>
    /// Cấu hình host test để Order API dùng connection string của container thay vì PostgreSQL local.
    /// </summary>
    /// <param name="builder">Builder của host do WebApplicationFactory khởi tạo.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override OrderDb để mọi thao tác EF Core trong test đi vào PostgreSQL Testcontainers, không đụng DB Compose local.
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderDb"] = _container.GetConnectionString()
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Order API map /health nhưng chưa đăng ký health-check services; đăng ký chỉ trong host test để factory có thể khởi động.
            services.AddHealthChecks();

            // Giữ Redis thật từ Compose để test idempotency đi qua RedisIdempotencyStore đã đăng ký, không thay bằng cache giả.
        });
    }

    /// <summary>
    /// Dừng host test rồi hủy container PostgreSQL để không để lại tài nguyên Testcontainers.
    /// </summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            await _container.DisposeAsync();
        }
    }
}
