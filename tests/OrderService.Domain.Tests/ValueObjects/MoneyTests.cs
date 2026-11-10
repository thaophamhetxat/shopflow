// File này chứa test cho Money trong Domain của OrderService.
// Tạo ở Day 39 để chứng minh các quy tắc validate của Value Object.
// Đây là Evidence Day 39 cho tiền âm và cộng tiền đồng bộ.

using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Tests.ValueObjects;

public class MoneyTests
{
    // Chứng minh Evidence Day 39: Money từ chối số âm ngay lúc tạo.
    [Fact]
    public void Money_NegativeAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-1, "USD"));
    }

    // Chứng minh Evidence Day 39: số tiền bằng 0 vẫn hợp lệ, chỉ âm mới bị từ chối.
    [Fact]
    public void Money_ZeroAmount_Allowed()
    {
        var money = new Money(0, "USD");

        Assert.Equal(0m, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    // Chứng minh Evidence Day 39: cộng tiền khác loại tiền tệ phải bị từ chối ngay lập tức.
    [Fact]
    public void Money_DifferentCurrency_Add_Throws()
    {
        var usd = new Money(10, "USD");
        var vnd = new Money(5, "VND");

        Assert.Throws<ArgumentException>(() => usd.Add(vnd));
    }
}
