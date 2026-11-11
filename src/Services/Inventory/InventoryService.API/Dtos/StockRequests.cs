// File này chứa request DTO cho các thao tác Reserve/Release của Inventory API.
// Tạo ở Day 48 để API nhận input HTTP riêng, sau đó chuyển sang command Application.
// Gateway Day 49 và Saga Day 52 sẽ gửi các request theo hợp đồng endpoint này.

namespace InventoryService.API.Dtos;

/// <summary>
/// Body của yêu cầu Reserve hàng.
/// </summary>
/// <param name="ProductId">Mã sản phẩm cần giữ.</param>
/// <param name="Quantity">Số lượng cần giữ.</param>
public sealed record ReserveStockRequest(Guid ProductId, int Quantity);

/// <summary>
/// Body của yêu cầu Release số hàng đã giữ.
/// </summary>
/// <param name="ProductId">Mã sản phẩm cần trả kho.</param>
/// <param name="Quantity">Số lượng cần trả.</param>
public sealed record ReleaseStockRequest(Guid ProductId, int Quantity);
