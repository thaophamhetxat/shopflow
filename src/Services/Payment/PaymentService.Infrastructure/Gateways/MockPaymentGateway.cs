// File này triển khai mock gateway cho use case Charge của PaymentService.
// Tạo ở Day 48 để giả lập gateway từ chối khoảng 20% mà không gọi HTTP thật.
// Gateway Day 49 sẽ thay thế mock; Saga Day 52 sẽ cần cả hai nhánh thành công/thất bại để bù trừ.

using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;

namespace PaymentService.Infrastructure.Gateways;

/// <summary>
/// Gateway giả lập kết quả Charge, trả thất bại nghiệp vụ thay vì ném exception.
/// </summary>
public sealed class MockPaymentGateway : IPaymentGateway
{
    private readonly Random _random;
    private readonly ILogger<MockPaymentGateway> _logger;

    /// <summary>
    /// Tạo mock gateway với bộ sinh số ngẫu nhiên tùy chọn để test điều khiển được kết quả.
    /// </summary>
    /// <param name="logger">Logger ghi lại mọi lần gọi Charge.</param>
    /// <param name="random">Bộ sinh số tùy chọn; mặc định dùng Random.Shared an toàn khi dùng singleton.</param>
    public MockPaymentGateway(ILogger<MockPaymentGateway> logger, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _random = random ?? Random.Shared;
    }

    /// <summary>
    /// Mô phỏng Charge với xác suất từ chối 20%, ghi log rồi trả kết quả nghiệp vụ.
    /// </summary>
    /// <param name="orderId">OrderId cần thu tiền.</param>
    /// <param name="amount">Số tiền cần thu.</param>
    /// <param name="currency">Mã tiền tệ của khoản thu.</param>
    /// <param name="cancellationToken">Token hủy thao tác.</param>
    /// <returns>Kết quả thành công hoặc thất bại kèm lý do.</returns>
    public Task<PaymentGatewayResult> ChargeAsync(
        Guid orderId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var failed = _random.Next(1, 101) <= 20;
        var reason = failed
            ? "Mock gateway từ chối thanh toán (mô phỏng lỗi cổng)."
            : null;

        _logger.LogInformation(
            "Mock gateway Charge OrderId={OrderId}, Amount={Amount} {Currency}, Succeeded={Succeeded}, FailureReason={FailureReason}",
            orderId,
            amount,
            currency,
            !failed,
            reason);

        return Task.FromResult(new PaymentGatewayResult(!failed, reason));
    }
}
