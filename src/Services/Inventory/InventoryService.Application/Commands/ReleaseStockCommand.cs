// File này định nghĩa command trả hàng đã giữ trong InventoryService Application.
// Tạo ở Day 48 để việc Release là use case nghiệp vụ, không phải thao tác xóa sản phẩm.
// Gateway Day 49 và Saga Day 52 sẽ gọi command này khi cần bù trừ tồn kho.

using InventoryService.Application.Queries;
using MediatR;

namespace InventoryService.Application.Commands;

/// <summary>
/// Yêu cầu trả lại số lượng đã giữ của một Product.
/// </summary>
/// <param name="ProductId">ID sản phẩm cần trả kho.</param>
/// <param name="Quantity">Số lượng cần trả, phải lớn hơn không.</param>
public sealed record ReleaseStockCommand(Guid ProductId, int Quantity) : IRequest<ProductDto>;
