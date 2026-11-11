// File này chứa integration test Day 52 để chứng minh luồng đặt hàng end-to-end chạy thật qua outbox, reserve kho, charge tiền và confirm đơn.
// Layer Integration dùng `ShopFlowWebApplicationFactory` cùng PostgreSQL Testcontainers, nhưng thay 2 saga client bằng fake trong `ConfigureTestServices` để test không phụ thuộc API Inventory/Payment thật.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderService.API.Dtos;
using OrderService.Application.Sagas;
using OrderService.Infrastructure.Persistence;

namespace OrderService.IntegrationTests;

/// <summary>
/// Test end-to-end cho `OrderSaga` chạy qua outbox poll, lưu `saga_state` và cập nhật đơn từ Pending sang Confirmed.
/// </summary>
public sealed class OrderSagaIntegrationTests : IClassFixture<ShopFlowWebApplicationFactory>
{
    private readonly ShopFlowWebApplicationFactory _factory;

    /// <summary>
    /// Khởi tạo factory test với PostgreSQL Testcontainers để có môi trường Order API thật cho saga integration.
    /// </summary>
    public OrderSagaIntegrationTests(ShopFlowWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Test này chứng minh Evidence Day 52: đặt đơn qua HTTP, outbox poll kích hoạt saga, rồi order chuyển `Confirmed` và `saga_state` ghi `Completed`.
    [Fact]
    public async Task PlaceOrder_EndToEnd_SagaConfirmsOrderAndPersistsCompletedState()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Thay hai client saga bằng fake trả thành công để test không phụ thuộc vào Inventory/Payment service đang chạy thật bên ngoài.
                services.RemoveAll<IInventoryClient>();
                services.RemoveAll<IPaymentClient>();
                services.AddScoped<IInventoryClient, FakeInventoryClient>();
                services.AddScoped<IPaymentClient, FakePaymentClient>();
            });
        });

        using var client = factory.CreateClient();
        var customerId = Guid.NewGuid();
        var ironSwordId = Guid.Parse("a73b5e21-4c0f-4f91-8a61-2e5c9501d101");
        var request = new PlaceOrderRequest(
            customerId,
            "123 Lê Lợi",
            "Hà Nội",
            "Việt Nam",
            "100000",
            [
                new PlaceOrderRequestItem(ironSwordId, 1, 250000m, "VND")
            ]);

        using var createResponse = await client.PostAsJsonAsync("/api/v1/orders", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var orderDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var orderId = orderDocument.RootElement.GetProperty("orderId").GetGuid();

        var confirmed = false;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var getResponse = await client.GetAsync($"/api/v1/orders/{orderId}");
            if (getResponse.StatusCode == HttpStatusCode.OK)
            {
                using var detailDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
                var status = detailDocument.RootElement.GetProperty("status").GetString();
                if (string.Equals(status, "Confirmed", StringComparison.Ordinal))
                {
                    confirmed = true;
                    break;
                }
            }

            await Task.Delay(500);
        }

        Assert.True(confirmed, "Saga không kịp chuyển order sang Confirmed trong khoảng thời gian 15s cho phép.");

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var sagaState = await dbContext.SagaStates.FirstOrDefaultAsync(state => state.OrderId == orderId);

        Assert.NotNull(sagaState);
        Assert.Equal("Completed", sagaState.State);
    }

    private sealed class FakeInventoryClient : IInventoryClient
    {
        public Task<bool> ReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakePaymentClient : IPaymentClient
    {
        public Task<PaymentChargeResult?> ChargeAsync(Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PaymentChargeResult?>(new PaymentChargeResult("Succeeded"));
        }
    }
}
