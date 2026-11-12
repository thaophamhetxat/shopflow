// File này kiểm tra mã hóa và giải mã keyset cursor của danh sách Order.
// Layer Application dùng cursor `(CreatedAt, OrderId)` để client tiếp tục phân trang mà không tra cứu order cuối trang trước.
// Tạo ở Day 61 làm Evidence cho format URL-safe base64 và kiểm tra input sai được từ chối an toàn.

using System.Text;
using OrderService.Application.Queries;
using Xunit;

namespace OrderService.Application.Tests;

/// <summary>
/// Unit test cho cursor keyset của danh sách Order, không cần database.
/// </summary>
public sealed class OrderListCursorTests
{
    /// <summary>
    /// Chứng minh Evidence Day 61: cursor round-trip giữ nguyên CreatedAt/OrderId và mọi payload sai định dạng đều bị từ chối.
    /// </summary>
    [Fact]
    public void OrderListCursor_EncodeDecode_RoundTrips()
    {
        var createdAt = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc).AddTicks(1234);
        var orderId = Guid.NewGuid();

        var cursor = OrderListCursor.Encode(createdAt, orderId);

        Assert.DoesNotContain("+", cursor);
        Assert.DoesNotContain("/", cursor);
        Assert.DoesNotContain("=", cursor);
        Assert.True(OrderListCursor.TryDecode(cursor, out var decodedCreatedAt, out var decodedOrderId));
        Assert.Equal(createdAt, decodedCreatedAt);
        Assert.Equal(orderId, decodedOrderId);

        var malformedCursors = new[]
        {
            "not-base64!",
            EncodePayload("missing-separator"),
            EncodePayload($"not-a-date|{orderId}"),
            EncodePayload($"2026-11-12T10:00:00.0000000Z|not-a-guid"),
            EncodePayload($"2026-11-12T10:00:00.0000000Z|{Guid.Empty}")
        };

        foreach (var malformedCursor in malformedCursors)
        {
            Assert.False(OrderListCursor.TryDecode(malformedCursor, out _, out _));
        }
    }

    private static string EncodePayload(string payload)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
