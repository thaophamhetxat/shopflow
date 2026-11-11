// File này định nghĩa cổng gọi thanh toán và kết quả trả về trong Application.
// Tạo ở Day 48 để handler phụ thuộc abstraction; Infrastructure cung cấp mock gateway thay vì HTTP thật.
// Gateway Day 49 sẽ thay implementation, còn Saga Day 52 vẫn gọi cùng hợp đồng Charge này.

namespace PaymentService.Application.Abstractions;

/// <summary>
/// Kết quả nghiệp vụ do gateway trả về; thất bại được biểu diễn bằng dữ liệu thay vì exception.
/// </summary>
/// <param name="Succeeded">Cho biết gateway có chấp nhận khoản thu hay không.</param>
/// <param name="FailureReason">Lý do từ chối nếu Succeeded là false.</param>
public sealed record PaymentGatewayResult(bool Succeeded, string? FailureReason);

/// <summary>
/// Abstraction cho thao tác thu tiền từ gateway.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Gửi yêu cầu thu tiền và nhận kết quả thành công hoặc thất bại nghiệp vụ.
    /// </summary>
    /// <param name="orderId">OrderId cần thu tiền.</param>
    /// <param name="amount">Số tiền cần thu.</param>
    /// <param name="currency">Mã tiền tệ của khoản thu.</param>
    /// <param name="cancellationToken">Token hủy yêu cầu.</param>
    /// <returns>Kết quả thu tiền; lỗi gateway nghiệp vụ nằm trong FailureReason.</returns>
    Task<PaymentGatewayResult> ChargeAsync(
        Guid orderId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);
}
