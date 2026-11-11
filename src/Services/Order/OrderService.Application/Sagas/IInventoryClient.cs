// File này định nghĩa giao diện client Inventory mà Saga sử dụng để giữ hàng trước khi Charge.
// Layer Application dùng contract này để an toàn gọi service bên ngoài qua HTTP tới Inventory API mà không phụ thuộc vào HttpClient cụ thể.

namespace OrderService.Application.Sagas;

/// <summary>
/// Client giao tiếp với InventoryService để reserve từng sản phẩm theo item của order.
/// </summary>
public interface IInventoryClient
{
    /// <summary>
    /// Gọi Inventory API reserve một sản phẩm với số lượng tương ứng.
    /// </summary>
    /// <param name="productId">ProductId cần giữ kho.</param>
    /// <param name="quantity">Số lượng cần giữ.</param>
    /// <param name="cancellationToken">Token hủy khi request bị ngắt.</param>
    /// <returns>True khi reserve thành công, false khi Inventory từ chối hoặc trả lỗi.</returns>
    Task<bool> ReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gọi Inventory API release kho đã reserve trước đó để bù trừ khi saga fail hoặc timeout.
    /// </summary>
    /// <param name="productId">ProductId cần release kho.</param>
    /// <param name="quantity">Số lượng release tương ứng với reservation.</param>
    /// <param name="cancellationToken">Token hủy khi request bị ngắt.</param>
    /// <returns>True khi Inventory xác nhận release thành công, false khi không thể release hoặc response báo lỗi.</returns>
    Task<bool> ReleaseAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);
}
