// File này kiểm thử các invariant Reserve/Release của Product thuộc InventoryService Domain.
// Tạo ở Day 48 để cung cấp Evidence cho Reserve giảm kho, thiếu kho bị chặn và Release cộng lại đúng lượng.
// Các test này không cần PostgreSQL vì chỉ kiểm thử aggregate thuần.

using InventoryService.Domain.Aggregates;
using InventoryService.Domain.Exceptions;
using InventoryService.Domain.ValueObjects;

namespace InventoryService.Domain.Tests.Aggregates;

/// <summary>
/// Kiểm tra hành vi nghiệp vụ của Product theo Evidence Day 48.
/// </summary>
public sealed class ProductTests
{
    /// <summary>
    /// Tạo Product thử nghiệm có giá trị hợp lệ cho các test tồn kho.
    /// </summary>
    /// <param name="stock">Số lượng kho ban đầu.</param>
    /// <returns>Product có ID cố định trong phạm vi test.</returns>
    private static Product CreateProduct(int stock)
    {
        return Product.Create(ProductId.From(Guid.Parse("00000000-0000-0000-0000-000000000001")), "Kiếm thử", 100m, "VND", stock);
    }

    /// <summary>Chứng minh Reserve giảm tồn kho đúng số lượng theo Evidence Day 48.</summary>
    [Fact]
    public void Reserve_DecreasesStock()
    {
        // Evidence Day 48: chứng minh Reserve 3 sản phẩm làm tồn kho 10 giảm còn 7.
        var product = CreateProduct(10);

        product.Reserve(3);

        Assert.Equal(7, product.StockQuantity);
    }

    /// <summary>Chứng minh Reserve ném lỗi Domain khi kho không đủ theo Evidence Day 48.</summary>
    [Fact]
    public void Reserve_InsufficientStock_ThrowsInventoryDomainException()
    {
        // Evidence Day 48: chứng minh không thể giữ nhiều sản phẩm hơn số lượng đang còn trong kho.
        var product = CreateProduct(2);

        var exception = Assert.Throws<InventoryDomainException>(() => product.Reserve(3));

        Assert.Contains("Kiếm thử", exception.Message);
        Assert.Contains("yêu cầu giữ 3", exception.Message);
        Assert.Contains("hiện còn 2", exception.Message);
    }

    /// <summary>Chứng minh Reserve từ chối số lượng không dương theo Evidence Day 48.</summary>
    [Fact]
    public void Reserve_InvalidQuantity_ThrowsInventoryDomainException()
    {
        // Evidence Day 48: chứng minh Reserve từ chối cả số lượng 0 và số lượng âm.
        var product = CreateProduct(10);

        Assert.Throws<InventoryDomainException>(() => product.Reserve(0));
        Assert.Throws<InventoryDomainException>(() => product.Reserve(-1));
    }

    /// <summary>Chứng minh Release cộng lại số lượng đã trả theo Evidence Day 48.</summary>
    [Fact]
    public void Release_IncreasesStock()
    {
        // Evidence Day 48: chứng minh Release 3 sản phẩm cộng kho từ 5 lên 8.
        var product = CreateProduct(5);

        product.Release(3);

        Assert.Equal(8, product.StockQuantity);
    }

    /// <summary>Chứng minh Release từ chối số lượng không dương theo Evidence Day 48.</summary>
    [Fact]
    public void Release_InvalidQuantity_ThrowsInventoryDomainException()
    {
        // Evidence Day 48: chứng minh Release từ chối số lượng 0 để không tạo thao tác kho vô nghĩa.
        var product = CreateProduct(5);

        Assert.Throws<InventoryDomainException>(() => product.Release(0));
    }
}
