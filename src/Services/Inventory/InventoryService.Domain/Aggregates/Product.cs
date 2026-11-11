// File này chứa aggregate Product và các quy tắc quản lý tồn kho của InventoryService.
// Tạo ở Day 48 để Reserve/Release chỉ thay đổi số lượng qua hành vi nghiệp vụ, không qua setter công khai.
// Gateway Day 49 và Saga Day 52 sẽ dùng aggregate này để giữ hàng và bù trừ tồn kho.

using InventoryService.Domain.Exceptions;
using InventoryService.Domain.ValueObjects;

namespace InventoryService.Domain.Aggregates;

/// <summary>
/// Aggregate sản phẩm, giữ thông tin giá và bảo vệ bất biến số lượng tồn kho.
/// </summary>
public sealed class Product
{
    private Product()
    {
        Name = string.Empty;
        PriceCurrency = string.Empty;
    }

    private Product(ProductId id, string name, decimal priceAmount, string priceCurrency, int stockQuantity)
    {
        Id = id;
        Name = name;
        PriceAmount = priceAmount;
        PriceCurrency = priceCurrency;
        StockQuantity = stockQuantity;
    }

    /// <summary>Mã định danh duy nhất của sản phẩm.</summary>
    public ProductId Id { get; private set; } = null!;

    /// <summary>Tên hiển thị của sản phẩm.</summary>
    public string Name { get; private set; }

    /// <summary>Giá sản phẩm theo đơn vị tiền tệ PriceCurrency.</summary>
    public decimal PriceAmount { get; private set; }

    /// <summary>Mã tiền tệ gồm ba chữ cái in hoa.</summary>
    public string PriceCurrency { get; private set; }

    /// <summary>Số lượng hiện có thể được giữ trong kho.</summary>
    public int StockQuantity { get; private set; }

    /// <summary>
    /// Tạo Product sau khi kiểm tra tên, giá, tiền tệ và số lượng ban đầu.
    /// </summary>
    /// <param name="id">Mã định danh của sản phẩm.</param>
    /// <param name="name">Tên không được để trống.</param>
    /// <param name="priceAmount">Giá không được âm.</param>
    /// <param name="priceCurrency">Mã tiền tệ gồm ba chữ cái in hoa.</param>
    /// <param name="stockQuantity">Số lượng ban đầu không được âm.</param>
    /// <returns>Product hợp lệ ở trạng thái tồn kho ban đầu.</returns>
    /// <exception cref="InventoryDomainException">Ném khi bất kỳ dữ liệu sản phẩm nào vi phạm quy tắc nghiệp vụ.</exception>
    public static Product Create(ProductId id, string name, decimal priceAmount, string priceCurrency, int stockQuantity)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InventoryDomainException("Tên sản phẩm không được để trống.");
        }

        if (priceAmount < 0)
        {
            throw new InventoryDomainException("Giá sản phẩm không được âm.");
        }

        if (!IsCurrencyCode(priceCurrency))
        {
            throw new InventoryDomainException("Mã tiền tệ phải gồm đúng ba chữ cái in hoa.");
        }

        if (stockQuantity < 0)
        {
            throw new InventoryDomainException("Tồn kho ban đầu không được âm.");
        }

        return new Product(id, name, priceAmount, priceCurrency, stockQuantity);
    }

    /// <summary>
    /// Giữ hàng nếu kho còn đủ số lượng yêu cầu.
    /// </summary>
    /// <param name="quantity">Số lượng cần giữ, phải lớn hơn không.</param>
    /// <exception cref="InventoryDomainException">Ném khi số lượng không hợp lệ hoặc kho không đủ hàng.</exception>
    public void Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new InventoryDomainException("Số lượng cần giữ phải lớn hơn 0.");
        }

        if (StockQuantity < quantity)
        {
            throw new InventoryDomainException(
                $"Sản phẩm '{Name}' không đủ hàng: yêu cầu giữ {quantity}, hiện còn {StockQuantity}.");
        }

        // Trừ kho ngay lúc giữ để các bước Saga Day 52 nhìn thấy lượng hàng còn thực sự khả dụng.
        StockQuantity -= quantity;
    }

    /// <summary>
    /// Trả lại kho số lượng đã được giữ trước đó.
    /// </summary>
    /// <param name="quantity">Số lượng cần trả, phải lớn hơn không.</param>
    /// <exception cref="InventoryDomainException">Ném khi số lượng không dương hoặc vượt giới hạn biểu diễn của int.</exception>
    public void Release(int quantity)
    {
        if (quantity <= 0)
        {
            throw new InventoryDomainException("Số lượng cần trả kho phải lớn hơn 0.");
        }

        // Giữ bất biến không âm khi số lượng int vượt giới hạn mà kiểu lưu trữ có thể biểu diễn.
        if (quantity > int.MaxValue - StockQuantity)
        {
            throw new InventoryDomainException("Tổng số lượng tồn kho vượt giới hạn lưu trữ.");
        }

        // Chỉ cộng lại số lượng được trả; không áp trần vì kho phải nhận lại hàng đã giữ.
        StockQuantity += quantity;
    }

    /// <summary>Kiểm tra mã tiền tệ có đúng ba chữ cái ASCII in hoa hay không.</summary>
    /// <param name="currency">Mã tiền tệ cần kiểm tra.</param>
    /// <returns>True nếu mã hợp lệ; ngược lại false.</returns>
    private static bool IsCurrencyCode(string currency)
    {
        return currency is { Length: 3 } && currency.All(character => character is >= 'A' and <= 'Z');
    }
}
