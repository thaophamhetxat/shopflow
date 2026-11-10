// File này chứa Value Object cho Address trong Domain của OrderService.
// Tạo ở Day 39 để làm nền cho Aggregate Order ở Day 40.
// Domain này giữ địa chỉ bất biến và validate ngay khi khởi tạo.

namespace OrderService.Domain.ValueObjects;

/// <summary>
/// Địa chỉ giao hàng của đơn hàng, gồm đường, thành phố, quốc gia và mã bưu điện tùy chọn.
/// Bất biến: tạo xong không đổi được, muốn thay đổi phải tạo Address mới — tránh địa chỉ bị sửa lén sau khi đơn hàng đã lưu.
/// Bất biến giữ: Street, City và Country không được rỗng sau khi trim; PostalCode được phép null hoặc rỗng để linh hoạt với quốc gia khác nhau.
/// Ví dụ hợp lệ: Address("123 Lê Lợi", "Hanoi", "Vietnam", "100000"). Ví dụ không hợp lệ: Address("", "Hanoi", "Vietnam") sẽ ném lỗi.
/// </summary>
public readonly struct Address : IEquatable<Address>
{
    /// <summary>
    /// Tên đường hoặc địa chỉ chi tiết của khách hàng.
    /// </summary>
    public string Street { get; }

    /// <summary>
    /// Tên thành phố của địa chỉ giao hàng.
    /// </summary>
    public string City { get; }

    /// <summary>
    /// Tên quốc gia của địa chỉ giao hàng.
    /// </summary>
    public string Country { get; }

    /// <summary>
    /// Mã bưu điện nếu có, cho phép null hoặc rỗng để phù hợp từng quốc gia.
    /// </summary>
    public string? PostalCode { get; }

    /// <summary>
    /// Tạo Address mới với các trường bắt buộc được validate ngay lúc khởi tạo.
    /// </summary>
    /// <param name="street">Tên đường hoặc số nhà, không được rỗng sau khi trim.</param>
    /// <param name="city">Tên thành phố, không được rỗng sau khi trim.</param>
    /// <param name="country">Tên quốc gia, không được rỗng sau khi trim.</param>
    /// <param name="postalCode">Mã bưu điện tùy chọn, nếu null hoặc rỗng thì được chấp nhận.</param>
    /// <exception cref="ArgumentException">Ném khi street, city hoặc country rỗng sau khi trim.</exception>
    public Address(string street, string city, string country, string? postalCode = null)
    {
        // Từ chối Street rỗng: địa chỉ giao hàng không thể thiếu đường để xác định điểm nhận hàng.
        if (string.IsNullOrWhiteSpace(street))
        {
            throw new ArgumentException("Street không được rỗng.", nameof(street));
        }

        // Từ chối City rỗng: thành phố là thành phần cốt lõi để phân loại địa điểm giao hàng.
        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("City không được rỗng.", nameof(city));
        }

        // Từ chối Country rỗng: quốc gia là thông tin bắt buộc để xác định chính sách thuế và vận chuyển.
        if (string.IsNullOrWhiteSpace(country))
        {
            throw new ArgumentException("Country không được rỗng.", nameof(country));
        }

        Street = street.Trim();
        City = city.Trim();
        Country = country.Trim();
        PostalCode = string.IsNullOrWhiteSpace(postalCode) ? null : postalCode.Trim();
    }

    public bool Equals(Address other)
    {
        return string.Equals(Street, other.Street, StringComparison.Ordinal)
            && string.Equals(City, other.City, StringComparison.Ordinal)
            && string.Equals(Country, other.Country, StringComparison.Ordinal)
            && string.Equals(PostalCode, other.PostalCode, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is Address other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Street, City, Country, PostalCode);
    }

    public override string ToString()
    {
        return $"{Street}, {City}, {Country}{(string.IsNullOrWhiteSpace(PostalCode) ? string.Empty : $", {PostalCode}")}";
    }

    public static bool operator ==(Address left, Address right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Address left, Address right)
    {
        return !(left == right);
    }
}
