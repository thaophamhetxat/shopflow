// File này cung cấp factory design-time cho dotnet ef của PaymentService.
// Tạo ở Day 48 để migration dùng connection string local qua biến môi trường, không lưu mật khẩu trong mã nguồn.
// Migration và database này sẽ được dùng khi Gateway Day 49 và Saga Day 52 gọi Payment API.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaymentService.Infrastructure.Persistence;

/// <summary>
/// Khởi tạo PaymentDbContext cho lệnh dotnet ef khi chưa có application host.
/// </summary>
public sealed class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    /// <summary>
    /// Tạo context từ biến môi trường ConnectionStrings__PaymentDb.
    /// </summary>
    /// <param name="args">Tham số dòng lệnh của dotnet ef.</param>
    /// <returns>Context đã cấu hình với provider PostgreSQL.</returns>
    /// <exception cref="InvalidOperationException">Ném nếu biến môi trường không có connection string.</exception>
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PaymentDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Thiếu biến môi trường 'ConnectionStrings__PaymentDb' cho PostgreSQL local.");
        }

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PaymentDbContext(options);
    }
}
