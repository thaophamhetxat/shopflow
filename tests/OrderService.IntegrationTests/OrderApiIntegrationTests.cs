// File này kiểm thử Order API thật qua HTTP với PostgreSQL 16 do Testcontainers dựng riêng.
// Các scenario đi qua endpoint, MediatR, validator, repository và Redis idempotency đang chạy thật.
// Tạo ở Day 50 để làm bằng chứng integration cho Order trước khi bắt đầu Saga ở Day 52.
// Day 61 cập nhật smoke coverage phân trang API sang cursor Base64Url mà không đổi response shape.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderService.API.Dtos;
using OrderService.Application.Queries;

namespace OrderService.IntegrationTests;

/// <summary>
/// Các integration test cho contract HTTP v1/v2 và nghiệp vụ Order đã có.
/// </summary>
public sealed class OrderApiIntegrationTests : IClassFixture<ShopFlowWebApplicationFactory>, IDisposable
{
    private readonly ShopFlowWebApplicationFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Tạo HttpClient trỏ vào host Order API của factory dùng chung trong class.
    /// </summary>
    /// <param name="factory">Factory chứa API test và PostgreSQL Testcontainers.</param>
    public OrderApiIntegrationTests(ShopFlowWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Giải phóng HttpClient sau khi test instance hoàn tất.
    /// </summary>
    public void Dispose()
    {
        _client.Dispose();
    }

    [Fact]
    // Evidence Day 65: readiness trả 200 khi PostgreSQL, Redis và RabbitMQ của test host đều khỏe.
    public async Task HealthReady_Returns200_WhenDependenciesHealthy()
    {
        using var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        var report = await _factory.Services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(check => check.Tags.Contains("ready"));
        var checks = string.Join(", ", report.Entries.Select(entry =>
            $"{entry.Key}={entry.Value.Status}:{entry.Value.Exception?.GetType().Name ?? "none"}"));

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{body}; {checks}");
    }

    [Fact]
    // Evidence Day 65: override OrderDb tới port không có listener chứng minh dependency fail sẽ làm readiness trả 503 mà không dừng PostgreSQL thật.
    public async Task HealthReady_Returns503_WhenDatabaseUnavailable()
    {
        using var unavailableFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:OrderDb"] = "Host=localhost;Port=5999;Database=shopflow_order;Username=shopflow;Password=shopflow123"
                })));
        using var client = unavailableFactory.CreateClient();
        using var response = await client.GetAsync("/health/ready");
        var report = await unavailableFactory.Services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(check => check.Tags.Contains("ready"));
        var configuredOrderDb = unavailableFactory.Services
            .GetRequiredService<IConfiguration>()
            .GetConnectionString("OrderDb");
        var checks = string.Join(", ", report.Entries.Select(entry =>
            $"{entry.Key}={entry.Value.Status}:{entry.Value.Exception?.GetType().Name ?? "none"}"));

        Assert.True(
            response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Expected 503 but got {(int)response.StatusCode}; configuredPort5999={configuredOrderDb?.Contains("Port=5999")}; {checks}");
    }

    [Fact]
    // Evidence Day 65: endpoint scrape Prometheus công bố đúng hai counter nghiệp vụ custom.
    public async Task MetricsEndpoint_ExposesCustomMetrics()
    {
        using var response = await _client.GetAsync("/metrics");
        var metrics = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("shopflow_orders_created_total", metrics);
        Assert.Contains("shopflow_saga_runs_total", metrics);
    }

    [Fact]
    // Customer riêng giúp test độc lập, không trộn dữ liệu giữa lần chạy; chứng minh Evidence Day 50: đặt đơn qua HTTP tạo được đơn và GET trả lại cùng đơn.
    public async Task PlaceOrder_ThroughHttp_Returns201_AndGetReturnsSameOrder()
    {
        var customerId = Guid.NewGuid();
        using var createResponse = await PostOrderAsync(CreateOrderRequest(customerId), Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var orderId = await ReadOrderIdAsync(createResponse);
        Assert.Equal($"/api/v1/orders/{orderId}", createResponse.Headers.Location?.ToString());

        using var getResponse = await _client.GetAsync($"/api/v1/orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var document = await ReadJsonAsync(getResponse);
        var order = document.RootElement;
        Assert.Equal(orderId, order.GetProperty("orderId").GetGuid());
        Assert.Equal(customerId, order.GetProperty("customerId").GetGuid());
        Assert.Equal("Pending", order.GetProperty("status").GetString());
        Assert.Equal(250000m, order.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    // Customer riêng giữ test độc lập; chứng minh Evidence Day 50: gửi lại cùng idempotency key trả cùng OrderId và chỉ lưu một đơn.
    public async Task PlaceOrder_SameIdempotencyKeyTwice_CreatesOnlyOneOrder()
    {
        var customerId = Guid.NewGuid();
        var request = CreateOrderRequest(customerId);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var firstResponse = await PostOrderAsync(request, idempotencyKey);
        using var secondResponse = await PostOrderAsync(request, idempotencyKey);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var firstOrderId = await ReadOrderIdAsync(firstResponse);
        var secondOrderId = await ReadOrderIdAsync(secondResponse);
        Assert.Equal(firstOrderId, secondOrderId);

        var orderIds = await GetOrderPageAsync($"/api/v1/orders?customerId={customerId}&pageSize=50");
        Assert.Equal(new[] { firstOrderId }, orderIds.Select(item => item.OrderId));
    }

    [Fact]
    // Guid đơn hàng chưa từng tồn tại giúp test độc lập; chứng minh Evidence Day 50: GET trả Problem Details 404.
    public async Task GetOrderById_NotFound_Returns404ProblemDetails()
    {
        using var response = await _client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = await ReadJsonAsync(response);
        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", document.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    // Customer riêng giữ đơn test độc lập; chứng minh Evidence Day 50: DELETE hủy mềm đơn, GET vẫn thấy Cancelled và lần hủy sau trả 409.
    public async Task CancelOrder_ThroughDelete_Returns204_ThenGetShowsCancelled_AndSecondDeleteReturns409()
    {
        var customerId = Guid.NewGuid();
        var orderId = await CreateOrderAsync(customerId);

        using var cancelResponse = await _client.DeleteAsync($"/api/v1/orders/{orderId}");
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);
        Assert.Empty(await cancelResponse.Content.ReadAsByteArrayAsync());

        using var getResponse = await _client.GetAsync($"/api/v1/orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var getDocument = await ReadJsonAsync(getResponse);
        Assert.Equal("Cancelled", getDocument.RootElement.GetProperty("status").GetString());

        using var secondCancelResponse = await _client.DeleteAsync($"/api/v1/orders/{orderId}");
        Assert.Equal(HttpStatusCode.Conflict, secondCancelResponse.StatusCode);
        Assert.Equal("application/problem+json", secondCancelResponse.Content.Headers.ContentType?.MediaType);
        using var problemDocument = await ReadJsonAsync(secondCancelResponse);
        Assert.Equal(409, problemDocument.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    // Customer dùng chung trong test này chỉ để phân trang một tập dữ liệu tự tạo; chứng minh Evidence Day 50: cursor trả đủ 5 đơn, không trùng và không thiếu.
    public async Task ListOrders_Pagination_ReturnsAllOrdersWithoutDuplicates()
    {
        var customerId = Guid.NewGuid();
        var request = CreateOrderRequest(customerId);
        var expectedOrderIds = new List<Guid>();

        for (var index = 0; index < 5; index++)
        {
            using var response = await PostOrderAsync(request, Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            expectedOrderIds.Add(await ReadOrderIdAsync(response));
        }

        var firstPage = await GetOrderPageAsync($"/api/v1/orders?customerId={customerId}&pageSize=2");
        Assert.Equal(2, firstPage.Count);
        var secondCursor = OrderListCursor.Encode(firstPage[^1].CreatedAt, firstPage[^1].OrderId);
        var secondPage = await GetOrderPageAsync($"/api/v1/orders?customerId={customerId}&cursor={secondCursor}&pageSize=2");
        Assert.Equal(2, secondPage.Count);
        var thirdCursor = OrderListCursor.Encode(secondPage[^1].CreatedAt, secondPage[^1].OrderId);
        var thirdPage = await GetOrderPageAsync($"/api/v1/orders?customerId={customerId}&cursor={thirdCursor}&pageSize=2");
        Assert.Single(thirdPage);

        var actualOrderIds = firstPage.Select(item => item.OrderId).Concat(secondPage.Select(item => item.OrderId)).Concat(thirdPage.Select(item => item.OrderId)).ToList();
        Assert.Equal(5, actualOrderIds.Distinct().Count());
        Assert.Equal(expectedOrderIds.OrderBy(id => id), actualOrderIds.OrderBy(id => id));
    }

    [Fact]
    // Customer riêng giữ test v2 độc lập; chứng minh Evidence Day 50: payload v2 giữ đủ field v1 và EstimatedDelivery bằng CreatedAt cộng 3 ngày.
    public async Task GetOrderV2_ReturnsEstimatedDeliveryPlus3Days()
    {
        var customerId = Guid.NewGuid();
        var orderId = await CreateOrderAsync(customerId);

        using var response = await _client.GetAsync($"/api/v2/orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ReadJsonAsync(response);
        var order = document.RootElement;
        var requiredFields = new[]
        {
            "orderId", "customerId", "status", "totalAmount", "totalCurrency", "createdAt", "items", "estimatedDelivery"
        };
        foreach (var field in requiredFields)
        {
            Assert.True(order.TryGetProperty(field, out _), $"Thiếu field {field} trong response v2.");
        }

        var createdAt = order.GetProperty("createdAt").GetDateTime();
        var estimatedDelivery = order.GetProperty("estimatedDelivery").GetDateTime();
        Assert.Equal(createdAt.AddDays(3), estimatedDelivery);
    }

    [Fact]
    // Customer riêng khiến request sai không ảnh hưởng test khác; chứng minh Evidence Day 50: request không có item bị validator từ chối bằng Problem Details 422.
    public async Task PlaceOrder_InvalidRequest_Returns422()
    {
        var customerId = Guid.NewGuid();
        using var response = await PostOrderAsync(CreateOrderRequest(customerId, includeItems: false), Guid.NewGuid().ToString("N"));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = await ReadJsonAsync(response);
        Assert.Equal(422, document.RootElement.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// Tạo một request hợp lệ hoặc request rỗng dùng riêng cho scenario validation.
    /// </summary>
    /// <param name="customerId">Khách hàng riêng của test đang chạy.</param>
    /// <param name="includeItems">Chỉ đặt false khi kiểm tra validator từ chối danh sách hàng rỗng.</param>
    /// <returns>Contract JSON đặt đơn với các field đúng theo Order API v1.</returns>
    private static PlaceOrderRequest CreateOrderRequest(Guid customerId, bool includeItems = true)
    {
        var items = includeItems
            ? new List<PlaceOrderRequestItem>
            {
                new(Guid.NewGuid(), 1, 250000m, "VND")
            }
            : new List<PlaceOrderRequestItem>();

        return new PlaceOrderRequest(customerId, "12 Test Street", "Hanoi", "Vietnam", "10000", items);
    }

    /// <summary>
    /// Gửi request đặt đơn qua HTTP kèm idempotency key trong header theo contract của API.
    /// </summary>
    /// <param name="request">Payload đặt đơn cần gửi.</param>
    /// <param name="idempotencyKey">Giá trị gửi trong header X-Idempotency-Key.</param>
    /// <returns>Phản hồi HTTP để test kiểm tra status, header và body.</returns>
    private async Task<HttpResponseMessage> PostOrderAsync(PlaceOrderRequest request, string idempotencyKey)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("X-Idempotency-Key", idempotencyKey);
        return await _client.SendAsync(httpRequest);
    }

    /// <summary>
    /// Tạo một đơn hợp lệ để các scenario GET, DELETE và versioning dùng chung cách gửi request.
    /// </summary>
    /// <param name="customerId">Khách hàng riêng của test đang chạy.</param>
    /// <returns>Mã đơn được API trả về sau khi tạo thành công.</returns>
    private async Task<Guid> CreateOrderAsync(Guid customerId)
    {
        using var response = await PostOrderAsync(CreateOrderRequest(customerId), Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadOrderIdAsync(response);
    }

    /// <summary>
    /// Đọc orderId từ response JSON của endpoint tạo đơn.
    /// </summary>
    /// <param name="response">Phản hồi tạo đơn cần đọc.</param>
    /// <returns>OrderId được serialize trong body.</returns>
    private static async Task<Guid> ReadOrderIdAsync(HttpResponseMessage response)
    {
        using var document = await ReadJsonAsync(response);
        return document.RootElement.GetProperty("orderId").GetGuid();
    }

    /// <summary>
    /// Đọc danh sách mã đơn từ response mảng của endpoint list.
    /// </summary>
    /// <param name="requestUri">Đường dẫn list kèm customerId và tham số phân trang.</param>
    /// <returns>Mã đơn theo thứ tự thật API trả về.</returns>
    private async Task<List<(Guid OrderId, DateTime CreatedAt)>> GetOrderPageAsync(string requestUri)
    {
        using var response = await _client.GetAsync(requestUri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await ReadJsonAsync(response);
        return document.RootElement
            .EnumerateArray()
            .Select(order => (
                OrderId: order.GetProperty("orderId").GetGuid(),
                CreatedAt: order.GetProperty("createdAt").GetDateTime()))
            .ToList();
    }

    /// <summary>
    /// Đọc nội dung HTTP thành JsonDocument để assert đúng payload do endpoint trả về.
    /// </summary>
    /// <param name="response">Phản hồi HTTP có body JSON.</param>
    /// <returns>Tài liệu JSON đã phân tích để caller tự dispose.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
