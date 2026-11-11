using System.Threading;

namespace OrderService.Infrastructure.Tests;

internal static class DatabaseTestLock
{
    public static readonly SemaphoreSlim Shared = new(1, 1);
}
