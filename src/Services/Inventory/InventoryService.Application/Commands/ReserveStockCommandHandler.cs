// File này thực thi use case ReserveStock của InventoryService Application.
// Tạo ở Day 48 để validation luôn chạy trước khi tải aggregate và thay đổi tồn kho.
// Gateway Day 49 và Saga Day 52 sẽ gọi handler này để giữ hàng.

using FluentValidation;
using InventoryService.Application.Queries;
using InventoryService.Domain.Repositories;
using InventoryService.Domain.ValueObjects;
using MediatR;

namespace InventoryService.Application.Commands;

/// <summary>
/// Xử lý yêu cầu giữ hàng; báo lỗi khi sản phẩm thiếu hoặc quy tắc tồn kho bị vi phạm.
/// </summary>
public sealed class ReserveStockCommandHandler : IRequestHandler<ReserveStockCommand, ProductDto>
{
    private readonly IProductRepository _repository;
    private readonly IValidator<ReserveStockCommand> _validator;

    /// <summary>
    /// Tạo handler với repository và validator của ReserveStock.
    /// </summary>
    /// <param name="repository">Repository truy cập Product.</param>
    /// <param name="validator">Validator kiểm tra số lượng yêu cầu.</param>
    public ReserveStockCommandHandler(IProductRepository repository, IValidator<ReserveStockCommand> validator)
    {
        _repository = repository;
        _validator = validator;
    }

    /// <summary>
    /// Kiểm tra command, giữ tồn kho và lưu thay đổi.
    /// </summary>
    /// <param name="request">Yêu cầu giữ sản phẩm và số lượng.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns>DTO tồn kho sau khi giữ thành công.</returns>
    /// <exception cref="ValidationException">Ném khi Quantity không lớn hơn không.</exception>
    /// <exception cref="ProductNotFoundException">Ném khi không tìm thấy sản phẩm.</exception>
    public async Task<ProductDto> Handle(ReserveStockCommand request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        var product = await _repository.GetByIdAsync(ProductId.From(request.ProductId), cancellationToken);
        if (product is null)
        {
            throw new ProductNotFoundException(request.ProductId);
        }

        product.Reserve(request.Quantity);
        await _repository.SaveChangesAsync(cancellationToken);

        return new ProductDto(
            product.Id.Value,
            product.Name,
            product.PriceAmount,
            product.PriceCurrency,
            product.StockQuantity);
    }
}
