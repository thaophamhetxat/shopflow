// File này chứa Value Object cho Money trong Domain của OrderService.
// Tạo ở Day 39 để làm nền cho Aggregate Order ở Day 40.
// Domain này giữ số tiền bất biến và validate ngay khi khởi tạo.

namespace OrderService.Domain.ValueObjects;

/// <summary>
/// Số tiền trong một đơn hàng, gồm số tiền và đơn vị tiền tệ.
/// Bất biến: tạo xong không đổi được, muốn đổi phải tạo Money mới — tránh tiền bị sửa lén sau khi đã tính tổng.
/// Bất biến giữ: Amount không bao giờ âm; Currency luôn là mã 3 ký tự viết hoa, trim và chuẩn hóa trước khi lưu.
/// Ví dụ hợp lệ: Money(100, "USD"). Ví dụ không hợp lệ: Money(-1, "USD") sẽ ném lỗi.
/// </summary>
public readonly struct Money : IEquatable<Money>
{
    /// <summary>
    /// Số tiền bằng decimal, luôn giữ giá trị đã validate và không cho phép âm.
    /// </summary>
    public decimal Amount { get; }

    /// <summary>
    /// Mã tiền tệ chuẩn hóa theo 3 chữ cái in hoa, ví dụ USD, VND.
    /// </summary>
    public string Currency { get; }

    /// <summary>
    /// Tạo một Money mới với số tiền và mã tiền tệ đã được validate.
    /// </summary>
    /// <param name="amount">Số tiền cần lưu, không được âm.</param>
    /// <param name="currency">Mã tiền tệ phải là 3 ký tự chữ cái viết hoa sau khi trim.</param>
    /// <exception cref="ArgumentOutOfRangeException">Ném khi amount &lt; 0.</exception>
    /// <exception cref="ArgumentException">Ném khi currency rỗng hoặc không phải mã 3 ký tự chữ cái.</exception>
    public Money(decimal amount, string currency)
    {
        // Từ chối Amount âm: tiền âm không có nghĩa trong đơn hàng, phải chặn ngay lúc tạo thay vì để lọt xuống DB rồi mới phát hiện.
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Số tiền không được âm.");
        }

        // Từ chối currency trống: một đơn hàng cần mã tiền tệ rõ ràng để tránh cộng tiền giữa các loại tiền khác nhau.
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Mã tiền tệ không được rỗng.", nameof(currency));
        }

        var normalizedCurrency = currency.Trim();

        // Từ chối mã tiền tệ không đúng 3 chữ cái: chuẩn hóa theo mục tiêu nghiệp vụ giúp hệ thống tránh lưu ký hiệu sai hoặc ký tự đặc biệt.
        if (normalizedCurrency.Length != 3 || !normalizedCurrency.All(char.IsLetter))
        {
            throw new ArgumentException("Mã tiền tệ phải là 3 ký tự chữ cái viết hoa, ví dụ USD.", nameof(currency));
        }

        Amount = amount;
        Currency = normalizedCurrency.ToUpperInvariant();
    }

    /// <summary>
    /// Cộng Money với một giá trị cùng đơn vị tiền tệ.
    /// </summary>
    /// <param name="other">Money khác cùng mã tiền tệ.</param>
    /// <returns>Money mới với tổng Amount và cùng Currency.</returns>
    /// <exception cref="ArgumentException">Ném khi other.Currency khác Currency hiện tại.</exception>
    public Money Add(Money other)
    {
        // Cấm cộng tiền chéo: cộng khác loại tiền sẽ làm sai giá trị nghiệp vụ và mâu thuẫn với quy tắc kinh doanh của hệ thống.
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new ArgumentException("Không thể cộng hai Money có đơn vị tiền tệ khác nhau.", nameof(other));
        }

        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>
    /// Nhân số tiền với số lượng sản phẩm, phục vụ tính LineTotal cho một OrderItem.
    /// </summary>
    /// <param name="quantity">Số lượng sản phẩm, phải lớn hơn hoặc bằng 0.</param>
    /// <returns>Money mới sau khi nhân theo số lượng.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Ném khi quantity &lt; 0.</exception>
    public Money Multiply(int quantity)
    {
        // Từ chối quantity âm: số lượng âm không có nghĩa trong đơn hàng, vì vậy cần chặn ngay khi tính tổng tiền.
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Số lượng không được âm.");
        }

        return new Money(Amount * quantity, Currency);
    }

    public bool Equals(Money other)
    {
        return Amount == other.Amount && string.Equals(Currency, other.Currency, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is Money other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Amount, Currency);
    }

    public override string ToString()
    {
        return $"{Amount} {Currency}";
    }

    public static bool operator ==(Money left, Money right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Money left, Money right)
    {
        return !(left == right);
    }
}
