// File này chứa test cho Address trong Domain của OrderService.
// Tạo ở Day 39 để chứng minh yêu cầu validate trường địa chỉ bắt buộc.
// Đây là Evidence Day 39 cho địa chỉ thiếu trường bắt buộc.

using OrderService.Domain.ValueObjects;

namespace OrderService.Domain.Tests.ValueObjects;

public class AddressTests
{
    // Chứng minh Evidence Day 39: Street trống phải bị từ chối khi khởi tạo Address.
    [Fact]
    public void Address_EmptyStreet_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Address("", "Hanoi", "Vietnam", "100000"));
    }

    // Chứng minh Evidence Day 39: City trống phải bị từ chối ngay vì nó là phần bắt buộc của địa chỉ.
    [Fact]
    public void Address_EmptyCity_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Address("123 Le Loi", "", "Vietnam", "100000"));
    }

    // Chứng minh Evidence Day 39: Country rỗng phải bị từ chối ngay khi tạo để tránh sai bản đồ vận chuyển.
    [Fact]
    public void Address_EmptyCountry_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Address("123 Le Loi", "Hanoi", "", "100000"));
    }
}
