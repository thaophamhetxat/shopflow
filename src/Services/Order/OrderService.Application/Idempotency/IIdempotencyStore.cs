// File này chứa interface cache idempotency cho OrderService.Application.
// Tạo ở Day 45 để handler có thể kiểm tra repeated request theo X-Idempotency-Key mà không phụ thuộc vào Redis cụ thể.
// Application layer định nghĩa contract này để Infrastructure đổi implementation mà không làm xấu sạch architecture.

namespace OrderService.Application.Idempotency;

/// <summary>
/// Contract lưu trữ giá trị idempotency theo key, giúp chống gửi trùng trong giới hạn TTL.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Đọc giá trị gắn với key idempotency. Trả về null hoặc default khi key chưa tồn tại hoặc hết thời gian hiệu lực.
    /// </summary>
    /// <typeparam name="T">Kiểu dữ liệu cần đọc, ví dụ OrderId.</typeparam>
    /// <param name="key">Khóa idempotency do client gửi lên.</param>
    /// <param name="cancellationToken">Token hủy nếu thao tác bị cancel.</param>
    /// <returns>Giá trị đã lưu trước đó nếu key còn hiệu lực, ngược lại giá trị mặc định.</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu giá trị idempotency với TTL cố định, thường là 24h theo yêu cầu bảo vệ lệnh gửi trùng.
    /// </summary>
    /// <typeparam name="T">Kiểu cần lưu, ví dụ OrderId.</typeparam>
    /// <param name="key">Khóa idempotency.</param>
    /// <param name="value">Giá trị cần cache, ví dụ OrderId mới tạo.</param>
    /// <param name="ttl">Thời gian hiệu lực của cache.</param>
    /// <param name="cancellationToken">Token hủy nếu thao tác bị cancel.</param>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);
}
