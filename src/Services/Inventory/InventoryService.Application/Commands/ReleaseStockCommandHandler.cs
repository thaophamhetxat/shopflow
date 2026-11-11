// File này thực thi use case ReleaseStock của InventoryService Application.
// Tạo ở Day 48 để chỉ trả lại đúng số lượng do command yêu cầu và lưu thay đổi qua repository.
// Gateway Day 49 và Saga Day 52 sẽ gọi handler này để bù trừ tồn kho.

using FluentValidation;
using InventoryService.Application.Queries;
using InventoryService.Domain.Repositories;
using InventoryService.Domain.ValueObjects;
using MediatR;

namespace InventoryService.Application.Commands;

/// <summary>
/// Xử lý yêu cầu trả hàng; báo lỗi khi sản phẩm không tồn tại hoặc số lượng vi phạm quy tắc.
/// </summary>
public sealed class ReleaseStockCommandHandler : IRequestHandler<ReleaseStockCommand, ProductDto>
{
    private readonly IProductRepository _repository;
    private readonly IValidator<ReleaseStockCommand> _validator;

    /// <summary>
    /// Tạo handler với repository và validator của ReleaseStock.
    /// </summary>
    /// <param name="repository">Repository truy cập Product.</param>
    /// <param name="validator">Validator kiểm tra số lượng cần trả.</param>
    public ReleaseStockCommandHandler(IProductRepository repository, IValidator<ReleaseStockCommand> validator)
    {
        _repository = repository;
        _validator = validator;
    }

    /// <summary>
    /// Kiểm tra command, cộng lại tồn kho và lưu thay đổi.
    /// </summary>
    /// <param name="request">Yêu cầu trả sản phẩm và số lượng.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns>DTO tồn kho sau khi trả hàng thành công.</returns>
    /// <exception cref="ValidationException">Ném khi Quantity không lớn hơn không.</exception>
    /// <exception cref="ProductNotFoundException">Ném khi không tìm thấy sản phẩm.</exception>
    public async Task<ProductDto> Handle(ReleaseStockCommand request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        var product = await _repository.GetByIdAsync(ProductId.From(request.ProductId), cancellationToken);
        if (product is null)
        {
            throw new ProductNotFoundException(request.ProductId);
        }

        product.Release(request.Quantity);
        await _repository.SaveChangesAsync(cancellationToken);

        return new ProductDto(
            product.Id.Value,
            product.Name,
            product.PriceAmount,
            product.PriceCurrency,
            product.StockQuantity);
    }
}
