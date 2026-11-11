// File này định nghĩa command giữ hàng trong InventoryService Application.
// Tạo ở Day 48 để API chuyển yêu cầu Reserve thành một use case MediatR có kiểu rõ ràng.
// Gateway Day 49 và Saga Day 52 sẽ gọi command này khi bắt đầu giữ tồn kho.

using InventoryService.Application.Queries;
using MediatR;

namespace InventoryService.Application.Commands;

/// <summary>
/// Yêu cầu giữ một số lượng Product cụ thể.
/// </summary>
/// <param name="ProductId">ID sản phẩm cần giữ.</param>
/// <param name="Quantity">Số lượng cần giữ, phải lớn hơn không.</param>
public sealed record ReserveStockCommand(Guid ProductId, int Quantity) : IRequest<ProductDto>;
