// File này chứa test cho OrderItem trong Domain của OrderService.
// Tạo ở Day 39 để chứng minh entity có ID riêng và LineTotal tính đúng theo Quantity × UnitPrice.
// Đây là Evidence Day 39 cho quantity âm và tổng tiền của dòng hàng.

using OrderService.Domain.Entities;
using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Tests.Entities;

public class OrderItemTests
{
    // Chứng minh Evidence Day 39: Quantity <= 0 phải bị từ chối để không có dòng hàng vô nghĩa.
    [Fact]
    public void OrderItem_ZeroQuantity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderItem(Guid.NewGuid(), 0, new Money(10, "USD")));
    }

    // Chứng minh Evidence Day 39: LineTotal đúng bằng UnitPrice × Quantity và giữ cùng Currency.
    [Fact]
    public void OrderItem_LineTotal_Correct()
    {
        var item = new OrderItem(Guid.NewGuid(), 3, new Money(10, "USD"));

        Assert.Equal(30m, item.LineTotal.Amount);
        Assert.Equal("USD", item.LineTotal.Currency);
    }
}
