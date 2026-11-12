// File này thực thi use case ReserveStock của InventoryService Application.
// Tạo ở Day 48 để validation luôn chạy trước khi tải aggregate và thay đổi tồn kho.
// Gateway Day 49 và Saga Day 52 sẽ gọi handler này để giữ hàng.

using FluentValidation;
using InventoryService.Application.Queries;
using InventoryService.Domain.Repositories;
using InventoryService.Domain.ValueObjects;
using MediatR;
using ShopFlow.BuildingBlocks;

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
    /// Kiểm tra command, gọi repository reserve atomic để giữ tồn kho và trả DTO sau khi commit thành công.
    /// </summary>
    /// <param name="request">Yêu cầu giữ sản phẩm và số lượng.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns>DTO tồn kho sau khi giữ thành công.</returns>
    /// <exception cref="ValidationException">Ném khi Quantity không lớn hơn không.</exception>
    /// <exception cref="ProductNotFoundException">Ném khi không tìm thấy sản phẩm.</exception>
    public async Task<ProductDto> Handle(ReserveStockCommand request, CancellationToken cancellationToken)
    {
        using var activity = ShopFlowActivitySource.Source.StartActivity(ShopFlowActivitySource.InventoryReserve);
        activity?.SetTag("product.id", request.ProductId.ToString());
        activity?.SetTag("reserve.quantity", request.Quantity.ToString());

        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        // Logic đọc -> kiểm tra -> trừ kho -> lưu đã được đóng gói vào repository ReserveAsync.
        // Ở đây handler chỉ cần invoke single atomic path để tránh race condition và giữ exception contract cũ.
        var product = await _repository.ReserveAsync(ProductId.From(request.ProductId), request.Quantity, cancellationToken);

        return new ProductDto(
            product.Id.Value,
            product.Name,
            product.PriceAmount,
            product.PriceCurrency,
            product.StockQuantity);
    }
}
