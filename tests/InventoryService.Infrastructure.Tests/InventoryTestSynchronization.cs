// File này đồng bộ hóa các test Infrastructure Inventory để không có race chéo giữa test cùng database.
// Dùng ở Day 58 để các test ProductRepositoryIntegrationTests và ConcurrentReserveTests không xóa/ghi dữ liệu của nhau.
// Layer: Infrastructure tests; đảm bảo database shared không bị test chồng lấn trong quá trình parallel execution.
// Day 58: cần synchronization để test inventory không phá dữ liệu của test khác khi chạy đồng thời.
using System.Threading;

namespace InventoryService.Infrastructure.Tests;

/// <summary>
/// Shared semaphore cho tất cả test InventoryService.Infrastructure, tránh xung đột khi chạy song song trên cùng PostgreSQL local.
/// </summary>
public static class InventoryTestSynchronization
{
    public static readonly SemaphoreSlim DatabaseLock = new(1, 1);
}
