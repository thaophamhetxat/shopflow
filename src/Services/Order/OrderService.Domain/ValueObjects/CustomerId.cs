// File này chứa Value Object cho CustomerId trong Domain của OrderService.
// Tạo ở Day 39 để làm nền cho Aggregate Order ở Day 40.
// Domain này giữ các ID mạnh kiểu và validate ngay khi khởi tạo.

namespace OrderService.Domain.ValueObjects;

/// <summary>
/// Mã định danh duy nhất của khách hàng, được bọc trong Guid để tránh lẫn với OrderId hay các ID khác trong hệ thống.
/// Bất biến: tạo xong không đổi được, muốn thay đổi phải tạo CustomerId mới — tránh khách hàng bị gán nhầm ID sau khi đã lưu.
/// Bất biến giữ: Value luôn là Guid hợp lệ, không chấp nhận Guid.Empty vì đó là giá trị chưa có thực thể.
/// Ví dụ hợp lệ: CustomerId.From(Guid.NewGuid()). Ví dụ không hợp lệ: CustomerId.From(Guid.Empty) sẽ ném lỗi.
/// </summary>
public readonly struct CustomerId : IEquatable<CustomerId>
{
    /// <summary>
    /// Giá trị Guid gốc của CustomerId, lưu cố định sau khi tạo.
    /// </summary>
    public Guid Value { get; }

    private CustomerId(Guid value)
    {
        Value = value;
    }

    /// <summary>
    /// Tạo một CustomerId mới bằng Guid ngẫu nhiên.
    /// </summary>
    /// <returns>CustomerId mới và không trùng với các khách hàng khác.</returns>
    public static CustomerId New()
    {
        return new CustomerId(Guid.NewGuid());
    }

    /// <summary>
    /// Tạo CustomerId từ Guid đã có, nhưng từ chối Guid.Empty để không cho phép khách hàng không tồn tại.
    /// </summary>
    /// <param name="value">Guid cần bọc trong CustomerId.</param>
    /// <returns>CustomerId chứa Guid hợp lệ.</returns>
    /// <exception cref="ArgumentException">Ném khi value là Guid.Empty.</exception>
    public static CustomerId From(Guid value)
    {
        // Từ chối Guid.Empty: ID khách hàng rỗng không đại diện cho ai, nếu cho qua sẽ gây trùng ID giữa khách hàng thật và đối tượng không tồn tại.
        if (value == Guid.Empty)
        {
            throw new ArgumentException("CustomerId không được là Guid.Empty.", nameof(value));
        }

        return new CustomerId(value);
    }

    /// <summary>
    /// So sánh hai CustomerId bằng giá trị Guid nội bộ.
    /// </summary>
    /// <param name="other">CustomerId cần so sánh.</param>
    /// <returns>True nếu cùng Value; ngược lại là false.</returns>
    public bool Equals(CustomerId other)
    {
        return Value.Equals(other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is CustomerId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    public static bool operator ==(CustomerId left, CustomerId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CustomerId left, CustomerId right)
    {
        return !(left == right);
    }
}
