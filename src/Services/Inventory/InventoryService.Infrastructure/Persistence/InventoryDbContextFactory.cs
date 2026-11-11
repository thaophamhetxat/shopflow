// File này cung cấp factory design-time cho dotnet ef của InventoryService.
// Tạo ở Day 48 để migration dùng đúng connection string local qua biến môi trường, không ghi mật khẩu vào mã nguồn.
// Migration và cập nhật database là bước chuẩn bị để Gateway Day 49/Saga Day 52 dùng API thật.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventoryService.Infrastructure.Persistence;

/// <summary>
/// Khởi tạo InventoryDbContext cho lệnh dotnet ef khi chưa có application host.
/// </summary>
public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    /// <summary>
    /// Tạo context từ biến môi trường ConnectionStrings__InventoryDb.
    /// </summary>
    /// <param name="args">Tham số dòng lệnh của dotnet ef.</param>
    /// <returns>Context đã cấu hình với provider PostgreSQL.</returns>
    /// <exception cref="InvalidOperationException">Ném nếu biến môi trường không có connection string.</exception>
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__InventoryDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Thiếu biến môi trường 'ConnectionStrings__InventoryDb' cho PostgreSQL local.");
        }

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new InventoryDbContext(options);
    }
}
