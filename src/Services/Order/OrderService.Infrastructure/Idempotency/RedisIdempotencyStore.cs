// File này chứa implementation Redis cho cache idempotency của OrderService.Infrastructure.
// Tạo ở Day 45 để giữ key X-Idempotency-Key trong Redis 24h, tránh trùng lệnh mà không cần thêm dependency tool hoặc service khác.
// Infrastructure layer chịu trách nhiệm đưa cache vào Redis thật và chuyển đổi dữ liệu sang JSON lưu trong distributed cache.

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using OrderService.Application.Idempotency;
using OrderService.Domain.ValueObjects;

namespace OrderService.Infrastructure.Idempotency;

/// <summary>
/// Store idempotency dùng Redis để lưu trữ OrderId theo key đã nhận trong vòng TTL 24h.
/// Mỗi key đại diện cho một request lặp lại của client, giúp xử lý gửi trùng ổn định ngay cả khi request đi qua lại nhiều lần.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IDistributedCache _cache;

    /// <summary>
    /// Khởi tạo store với distributed cache đã được đăng ký bằng StackExchangeRedis ở infrastructure layer.
    /// </summary>
    /// <param name="cache">IDistributedCache đã được cấu hình để kết nối Redis.</param>
    public RedisIdempotencyStore(IDistributedCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Đọc OrderId từ Redis theo key idempotency; nếu key không có hoặc hết TTL thì trả về default/null.
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu cần đọc, thường là OrderId.</typeparam>
    /// <param name="key">Khóa idempotency do client cung cấp.</param>
    /// <param name="cancellationToken">Token hủy nếu thao tác bị hủy.</param>
    /// <returns>OrderId đã cache trước đó nếu còn hiệu lực, ngược lại default.</returns>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var value = await _cache.GetAsync(GetCacheKey(key), cancellationToken);
        if (value is null || value.Length == 0)
        {
            return default;
        }

        var json = Encoding.UTF8.GetString(value);

        if (typeof(T) == typeof(OrderId))
        {
            var orderId = JsonSerializer.Deserialize<Guid>(json);
            return (T?)(object)OrderId.From(orderId);
        }

        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// Ghi OrderId hoặc giá trị khác vào Redis với TTL cố định để chống gửi trùng trong 24h.
    /// </summary>
    /// <typeparam name="T">Kiểu giá trị cần cache.</typeparam>
    /// <param name="key">Khóa idempotency.</param>
    /// <param name="value">Giá trị cần lưu, thường là OrderId vừa tạo.</param>
    /// <param name="ttl">Thời gian hiệu lực của cache.</param>
    /// <param name="cancellationToken">Token hủy nếu thao tác bị hủy.</param>
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "TTL cho idempotency phải lớn hơn 0.");
        }

        var json = value is OrderId orderId
            ? JsonSerializer.Serialize(orderId.Value)
            : JsonSerializer.Serialize(value);

        await _cache.SetAsync(
            GetCacheKey(key),
            Encoding.UTF8.GetBytes(json),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            },
            cancellationToken);
    }

    private static string GetCacheKey(string key)
    {
        return $"order:idempotency:{key}";
    }
}
