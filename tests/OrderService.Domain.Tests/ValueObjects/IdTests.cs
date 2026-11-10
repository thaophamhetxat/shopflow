// File này chứa test cho OrderId và CustomerId trong Domain của OrderService.
// Tạo ở Day 39 để chứng minh ID mạnh kiểu và validate Guid.Empty ngay khi tạo.
// Đây là Evidence Day 39 cho strongly typed ID và giá trị mới ngẫu nhiên.

using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Tests.ValueObjects;

public class IdTests
{
    // Chứng minh Evidence Day 39: OrderId.From(Guid.Empty) bị từ chối và OrderId.New() luôn tạo ra ID khác nhau.
    [Fact]
    public void OrderId_FromEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => OrderId.From(Guid.Empty));

        var first = OrderId.New();
        var second = OrderId.New();

        Assert.NotEqual(first, second);
    }

    // Chứng minh Evidence Day 39: CustomerId.From(Guid.Empty) bị từ chối và CustomerId.New() luôn tạo ra ID mới.
    [Fact]
    public void CustomerId_FromEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => CustomerId.From(Guid.Empty));

        var first = CustomerId.New();
        var second = CustomerId.New();

        Assert.NotEqual(first, second);
    }
}
