// File này định nghĩa query đọc sản phẩm trong InventoryService Application.
// Tạo ở Day 48 để API đọc tồn kho bằng MediatR thay vì truy cập Infrastructure trực tiếp.
// Gateway Day 49 và Saga Day 52 có thể dùng query này để tra cứu thông tin sản phẩm.

using MediatR;

namespace InventoryService.Application.Queries;

/// <summary>
/// Yêu cầu đọc một sản phẩm theo ID; kết quả null nếu không có sản phẩm tương ứng.
/// </summary>
/// <param name="ProductId">ID sản phẩm cần đọc.</param>
public sealed record GetProductQuery(Guid ProductId) : IRequest<ProductDto?>;
