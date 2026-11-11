// File này chứa client HTTP để gọi Inventory API reserve stock từ trong Saga của OrderService.
// Layer Infrastructure này triển khai contract `IInventoryClient` bằng named HttpClient đã được đăng ký trong DI, không trực tiếp new `HttpClient` ở nhiều chỗ.

using System.Net.Http;
using System.Net.Http.Json;
using OrderService.Application.Sagas;

namespace OrderService.Infrastructure.Sagas;

/// <summary>
/// HTTP client của OrderService gửi request reserve tới InventoryService.
/// </summary>
public sealed class InventoryHttpClient : IInventoryClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Khởi tạo client với named HttpClient từ DI.
    /// </summary>
    /// <param name="httpClient">Named HttpClient đã có BaseAddress InventoryService.</param>
    public InventoryHttpClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("InventoryService");
    }

    /// <summary>
    /// Reserve stock từng item của order qua endpoint `/inventory/reserve`.
    /// </summary>
    /// <param name="productId">ProductId cần giữ kho.</param>
    /// <param name="quantity">Số lượng cần giữ.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns>True khi inventory cho biết reserve thành công, false khi trả lỗi/không tìm thấy sản phẩm hoặc stock không đủ.</returns>
    public async Task<bool> ReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/inventory/reserve",
            new { productId, quantity },
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Release kho đã reserve trước đó nếu saga cần bù trừ khi Reserve/Charge fail hoặc timeout.
    /// </summary>
    /// <param name="productId">ProductId cần trả kho.</param>
    /// <param name="quantity">Số lượng sẽ trả lại.</param>
    /// <param name="cancellationToken">Token hủy request.</param>
    /// <returns>True khi Inventory xác nhận release là thành công.</returns>
    public async Task<bool> ReleaseAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/inventory/release",
            new { productId, quantity },
            cancellationToken);

        return response.IsSuccessStatusCode;
    }
}
