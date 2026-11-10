// File này chứa Value Object cho OrderId trong Domain của OrderService.
// Tạo ở Day 39 để làm nền cho Aggregate Order ở Day 40.
// Domain này giữ các ID mạnh kiểu và validate ngay khi khởi tạo.

namespace OrderService.Domain.ValueObjects;

/// <summary>
/// Mã định danh duy nhất cho một đơn hàng, được bọc trong Guid để tránh gán nhầm giữa các entity khác nhau.
/// Bất biến: tạo xong không đổi được, muốn thay đổi phải tạo OrderId mới — đảm bảo đơn hàng không bị sửa ID lén trong bộ nhớ.
/// Bất biến giữ: Value luôn là Guid hợp lệ, không chấp nhận Guid.Empty vì đó là ID rỗng chưa có thực thể nào.
/// Ví dụ hợp lệ: OrderId.From(Guid.NewGuid()). Ví dụ không hợp lệ: OrderId.From(Guid.Empty) sẽ ném lỗi.
/// </summary>
public readonly struct OrderId : IEquatable<OrderId>
{
    /// <summary>
    /// Giá trị Guid gốc của OrderId, lưu cố định sau khi tạo.
    /// </summary>
    public Guid Value { get; }

    private OrderId(Guid value)
    {
        Value = value;
    }

    /// <summary>
    /// Tạo một OrderId mới bằng Guid ngẫu nhiên.
    /// </summary>
    /// <returns>OrderId mới và khác với hầu hết các ID khác trong hệ thống.</returns>
    public static OrderId New()
    {
        return new OrderId(Guid.NewGuid());
    }

    /// <summary>
    /// Tạo OrderId từ Guid đã có, nhưng từ chối Guid.Empty vì ID rỗng không đại diện cho đơn hàng thật.
    /// </summary>
    /// <param name="value">Guid cần bọc trong OrderId.</param>
    /// <returns>OrderId chứa Guid hợp lệ.</returns>
    /// <exception cref="ArgumentException">Ném khi value là Guid.Empty.</exception>
    public static OrderId From(Guid value)
    {
        // Từ chối Guid.Empty: ID rỗng nghĩa là chưa có đơn hàng thật, nếu cho qua thì hai đơn hàng khác nhau sẽ trùng ID.
        if (value == Guid.Empty)
        {
            throw new ArgumentException("OrderId không được là Guid.Empty.", nameof(value));
        }

        return new OrderId(value);
    }

    /// <summary>
    /// So sánh hai OrderId bằng giá trị Guid nội bộ.
    /// </summary>
    /// <param name="other">OrderId cần so sánh.</param>
    /// <returns>True nếu cùng Value; ngược lại là false.</returns>
    public bool Equals(OrderId other)
    {
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is OrderId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static bool operator ==(OrderId left, OrderId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(OrderId left, OrderId right)
    {
        return !(left == right);
    }
}
