// File này chứa client HTTP để call PaymentService charge tiền, kiểm tra `Status` và không coi 201 là thành công nếu trạng thái trả về chưa phải `Succeeded`.
// Layer Infrastructure thực hiện việc `OrderSaga` không cần biết chi tiết endpoint cứng, chỉ cần `PaymentChargeResult` và `Status` đã parse đúng.

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using OrderService.Application.Sagas;

namespace OrderService.Infrastructure.Sagas;

/// <summary>
/// HTTP client của OrderService gửi request charge tới PaymentService và parse JSON `status` để quyết định tiếp tục hoặc dừng Saga.
/// </summary>
public sealed class PaymentHttpClient : IPaymentClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Khởi tạo client với named HttpClient đã được cấu hình BaseAddress riêng cho PaymentService.
    /// </summary>
    /// <param name="httpClientFactory">Factory tạo named client đã đăng ký trong DI.</param>
    public PaymentHttpClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("PaymentService");
    }

    /// <summary>
    /// Gửi request charge, đọc response JSON và trích `status` để xác định `Succeeded`/`Failed`.
    /// </summary>
    /// <param name="orderId">OrderId đang được charge.</param>
    /// <param name="amount">Số tiền cần thanh toán.</param>
    /// <param name="currency">Đơn vị tiền tệ của đơn.</param>
    /// <param name="cancellationToken">Token hủy khi request bị dừng.</param>
    /// <returns>PaymentChargeResult chứa status đúng từ body, hoặc null nếu không đọc được.</returns>
    public async Task<PaymentChargeResult?> ChargeAsync(Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/payments",
            new { orderId, amount, currency },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new PaymentChargeResult("Failed");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new PaymentChargeResult("Failed");
        }

        using var document = JsonDocument.Parse(payload);
        var statusValue = document.RootElement.TryGetProperty("status", out var statusProperty)
            ? statusProperty.GetString()
            : null;

        return new PaymentChargeResult(string.IsNullOrWhiteSpace(statusValue) ? "Failed" : statusValue);
    }
}
