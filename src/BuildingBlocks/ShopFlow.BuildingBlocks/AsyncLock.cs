// File này cung cấp primitive async-aware cho BuildingBlocks để tránh dùng lock(obj) trong code awaitable.
// Dùng ở Day 58 để chứng minh `SemaphoreSlim` thích hợp cho async, còn row lock DB mới là fix đúng cho oversell.
// Khối này không thay thế transaction database; nó chỉ hỗ trợ test/consumer future khi cần ghim resource trong memory.
// Layer: BuildingBlocks; dùng cho code bất đồng bộ cần giới hạn truy cập bằng semaphore trong bộ nhớ.
// Day 58: quyết định cách khoá đúng cho async, không dùng lock(obj) vì gây deadlock khi await.
namespace ShopFlow.BuildingBlocks;

/// <summary>
/// Khóa đồng bộ hóa bất đồng bộ bằng SemaphoreSlim, không dùng `lock(obj)` vì `lock` giữ thread và không hỗ trợ await an toàn.
/// </summary>
public sealed class AsyncLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Xin khóa bất đồng bộ; khi hoàn tất, caller phải dispose releaser để Release semaphore.
    /// </summary>
    /// <param name="cancellationToken">Token hủy chờ xin khóa.</param>
    /// <returns>Disposable giải phóng khóa sau khi dùng xong.</returns>
    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _semaphore.Release();
            _disposed = true;
        }
    }
}
