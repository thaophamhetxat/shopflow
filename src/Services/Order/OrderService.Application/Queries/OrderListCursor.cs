// File này mã hóa và giải mã keyset cursor của danh sách Order ở Application layer.
// Tạo ở Day 61 để client gửi đồng thời CreatedAt và OrderId qua query string an toàn.
// Endpoint Day 61 dùng helper này để tiếp tục phân trang mà không tra CreatedAt của cursor trong database.

using System.Globalization;
using System.Text;

namespace OrderService.Application.Queries;

/// <summary>
/// Cursor keyset cho pagination của danh sách order theo customer.
/// Dùng cả CreatedAt và OrderId để tránh lỗi phân trang khi nhiều đơn cùng CreatedAt; `CreatedAt` là key chính còn `OrderId` là tie-breaker, do EF Core không dịch `Guid.CompareTo` xuống SQL.
/// Cursor được mã hóa theo RFC 4648 URL-safe base64 để an toàn khi đi qua query string và không phát sinh `+`, `/` hoặc `=` làm URL lỗi.
/// </summary>
public static class OrderListCursor
{
    /// <summary>
    /// Mã hóa keyset `(CreatedAt, OrderId)` thành chuỗi base64 URL-safe để client gửi qua query string.
    /// </summary>
    /// <param name="createdAt">Thời điểm tạo đơn, lưu theo UTC để giữ ổn định trên mọi môi trường.</param>
    /// <param name="orderId">Id của đơn cuối trang trước; dùng làm tie-breaker khi nhiều order cùng CreatedAt.</param>
    /// <returns>Chuỗi cursor URL-safe đã mã hóa.</returns>
    public static string Encode(DateTime createdAt, Guid orderId)
    {
        var utcCreatedAt = createdAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
            : createdAt.ToUniversalTime();

        var payload = $"{utcCreatedAt.ToString("O", CultureInfo.InvariantCulture)}|{orderId}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Giải mã cursor base64 URL-safe về `(CreatedAt, OrderId)`; thất bại trả false mà không ném exception.
    /// </summary>
    /// <param name="cursor">Cursor client gửi lên, có thể null, rỗng hoặc sai định dạng.</param>
    /// <param name="createdAt">Thời điểm CreatedAt đã giải mã.</param>
    /// <param name="orderId">OrderId đã giải mã.</param>
    /// <returns>True nếu cursor hợp lệ; false nếu không parse được hoặc thiếu dữ liệu.</returns>
    public static bool TryDecode(string? cursor, out DateTime createdAt, out Guid orderId)
    {
        createdAt = default;
        orderId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var normalized = cursor.Trim();
            normalized = normalized.Replace('-', '+').Replace('_', '/');
            while (normalized.Length % 4 != 0)
            {
                normalized += '=';
            }

            var payloadBytes = Convert.FromBase64String(normalized);
            var payload = Encoding.UTF8.GetString(payloadBytes);
            var separatorIndex = payload.IndexOf('|');
            if (separatorIndex <= 0 || separatorIndex == payload.Length - 1)
            {
                return false;
            }

            var createdAtText = payload.Substring(0, separatorIndex);
            var orderIdText = payload.Substring(separatorIndex + 1);

            if (!DateTime.TryParseExact(createdAtText, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedCreatedAt))
            {
                return false;
            }

            if (!Guid.TryParse(orderIdText, out var parsedOrderId) || parsedOrderId == Guid.Empty)
            {
                return false;
            }

            createdAt = parsedCreatedAt;
            orderId = parsedOrderId;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
