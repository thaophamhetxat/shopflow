// File này chứa design-time factory cho EF Core để dotnet ef biết cách tạo DbContext khi tạo migration hoặc update database.
// Tạo ở Day 42 để migration InitialCreate có thể chạy trên PostgreSQL local với connection string đúng theo environment dev.
// Factory này là phụ trợ cho tooling, không chứa nghiệp vụ ứng dụng hay validation.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// Design-time factory dùng bởi dotnet ef để khởi tạo OrderDbContext khi chưa có application startup host hoặc khi chạy migration từ terminal.
/// </summary>
public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    /// <summary>
    /// Tạo DbContext với connection string local dành cho PostgreSQL trên máy dev.
    /// </summary>
    /// <param name="args">Các tham số dòng lệnh từ dotnet ef.</param>
    /// <returns>DbContext đã cấu hình sẵn với connection string local.</returns>
    public OrderDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OrderDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Thiếu biến môi trường 'ConnectionStrings__OrderDb' cho PostgreSQL local.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<OrderDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new OrderDbContext(optionsBuilder.Options);
    }
}
