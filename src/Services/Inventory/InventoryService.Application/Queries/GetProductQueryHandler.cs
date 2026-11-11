// File này thực thi query đọc Product trong InventoryService Application.
// Tạo ở Day 48 để query không thay đổi aggregate và biểu diễn sản phẩm không tồn tại bằng null.
// Gateway Day 49 và Saga Day 52 sẽ dùng kết quả DTO thay vì tham chiếu entity EF.

using InventoryService.Domain.Repositories;
using InventoryService.Domain.ValueObjects;
using MediatR;

namespace InventoryService.Application.Queries;

/// <summary>
/// Đọc một Product và chuyển thành DTO; trả null khi repository không tìm thấy ID.
/// </summary>
public sealed class GetProductQueryHandler : IRequestHandler<GetProductQuery, ProductDto?>
{
    private readonly IProductRepository _repository;

    /// <summary>
    /// Tạo handler với repository Product.
    /// </summary>
    /// <param name="repository">Repository dùng để đọc sản phẩm.</param>
    public GetProductQueryHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Tải Product theo ID và trả DTO hoặc null nếu ID chưa tồn tại.
    /// </summary>
    /// <param name="request">Query chứa ProductId cần đọc.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns>DTO sản phẩm nếu tìm thấy; ngược lại null.</returns>
    public async Task<ProductDto?> Handle(GetProductQuery request, CancellationToken cancellationToken)
    {
        var product = await _repository.GetByIdAsync(ProductId.From(request.ProductId), cancellationToken);
        return product is null
            ? null
            : new ProductDto(
                product.Id.Value,
                product.Name,
                product.PriceAmount,
                product.PriceCurrency,
                product.StockQuantity);
    }
}
