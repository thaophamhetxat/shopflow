// File này định nghĩa giao diện client Payment mà Saga gọi sau khi Inventory reserve xong.
// Layer Application giữ contract tối giản: saga quan tâm chỉ tới status kết quả Charge, không phụ thuộc vào DTO gốc của PaymentService.

namespace OrderService.Application.Sagas;

/// <summary>
/// Client giao tiếp với PaymentService để charge tiền theo order và kiểm tra status trả về.
/// </summary>
public interface IPaymentClient
{
    /// <summary>
    /// Gọi Payment API processing tiền cho order hiện tại rồi trả về status thu được.
    /// </summary>
    /// <param name="orderId">OrderId cần thanh toán.</param>
    /// <param name="amount">Số tiền tương ứng với total order.</param>
    /// <param name="currency">Đơn vị tiền tệ của order.</param>
    /// <param name="cancellationToken">Token hủy khi request bị dừng.</param>
    /// <returns>Kết quả Charge với `Status` dạng chuỗi, hoặc null nếu response không đọc được.</returns>
    Task<PaymentChargeResult?> ChargeAsync(Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default);
}
