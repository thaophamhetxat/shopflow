// File này chứa Entity cho OrderItem trong Domain của OrderService.
// Tạo ở Day 39 để làm nền cho Aggregate Order ở Day 40.
// Domain này giữ dòng hàng đơn lẻ với identity riêng và tính toán LineTotal theo Quantity × UnitPrice.

using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Entities;

/// <summary>
/// Một dòng hàng trong đơn hàng, đại diện cho một sản phẩm được đặt với số lượng và đơn giá riêng.
/// Bất biến: sau khi tạo, các thông tin cốt lõi không đổi để tránh hàng hóa bị sửa trong lúc xử lý thanh toán hoặc giao hàng.
/// Bất biến giữ: Id luôn duy nhất; ProductId không rỗng; Quantity phải > 0; UnitPrice luôn là Money hợp lệ và không âm.
/// Ví dụ hợp lệ: OrderItem(Guid.NewGuid(), 2, new Money(10, "USD")). Ví dụ không hợp lệ: OrderItem(Guid.Empty, 0, new Money(10, "USD")) sẽ ném lỗi.
/// </summary>
public sealed class OrderItem : IEquatable<OrderItem>
{
    /// <summary>
    /// Mã định danh duy nhất cho dòng hàng, tự sinh khi tạo mới.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Mã sản phẩm được đặt trong dòng hàng này.
    /// </summary>
    public Guid ProductId { get; }

    /// <summary>
    /// Số lượng sản phẩm đặt mua trong dòng hàng, phải lớn hơn 0.
    /// </summary>
    public int Quantity { get; }

    /// <summary>
    /// Đơn giá của mỗi sản phẩm trong dòng hàng.
    /// </summary>
    public Money UnitPrice { get; }

    /// <summary>
    /// Tổng tiền của dòng hàng theo công thức UnitPrice × Quantity, luôn dùng cùng đơn vị tiền tệ.
    /// </summary>
    public Money LineTotal => UnitPrice.Multiply(Quantity);

    /// <summary>
    /// Tạo một OrderItem mới với identity, số lượng và đơn giá đã validate.
    /// </summary>
    /// <param name="productId">Mã sản phẩm, không được rỗng.</param>
    /// <param name="quantity">Số lượng đặt mua, phải > 0.</param>
    /// <param name="unitPrice">Đơn giá sản phẩm, phải là Money hợp lệ.</param>
    /// <exception cref="ArgumentException">Ném khi productId là Guid.Empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Ném khi quantity &lt;= 0.</exception>
    public OrderItem(Guid productId, int quantity, Money unitPrice)
    {
        // Từ chối productId rỗng: sản phẩm không thể không xác định vì khi đó không có dòng hàng nào hợp lệ.
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("ProductId không được là Guid.Empty.", nameof(productId));
        }

        // Từ chối Quantity <= 0: một dòng hàng phải có ít nhất 1 sản phẩm, số lượng 0 không tạo ra giá trị kinh doanh nào.
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity phải lớn hơn 0.");
        }

        Id = Guid.NewGuid();
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public bool Equals(OrderItem? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        return Id == other.Id;
    }

    public override bool Equals(object? obj)
    {
        return obj is OrderItem other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public static bool operator ==(OrderItem? left, OrderItem? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
        {
            return false;
        }

        return left.Equals(right);
    }

    public static bool operator !=(OrderItem? left, OrderItem? right)
    {
        return !(left == right);
    }
}
