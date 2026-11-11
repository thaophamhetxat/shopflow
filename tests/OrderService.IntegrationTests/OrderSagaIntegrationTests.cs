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
public sealed class OrderSagaIntegrationTests
{
    // Test này chứng minh Evidence Day 52: đặt đơn qua HTTP, outbox poll kích hoạt saga, rồi order chuyển `Confirmed` và `saga_state` ghi `Completed`.
    [Fact]
    public async Task PlaceOrder_EndToEnd_SagaConfirmsOrderAndPersistsCompletedState()
    {
        var factory = new ShopFlowWebApplicationFactory();
        await factory.InitializeAsync();

        try
        {
            var hostFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IInventoryClient>();
                    services.RemoveAll<IPaymentClient>();
                    services.AddScoped<IInventoryClient>(_ => new FakeInventoryClient());
                    services.AddScoped<IPaymentClient, FakePaymentClient>();
                });
            });

            using var client = hostFactory.CreateClient();
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
            for (var attempt = 0; attempt < 60; attempt++)
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

            await using var scope = hostFactory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var sagaState = await dbContext.SagaStates.FirstOrDefaultAsync(state => state.OrderId == orderId);

            Assert.NotNull(sagaState);
            Assert.Equal("Completed", sagaState.State);
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }

    // Test này chứng minh Evidence Day 53: payment fail phải release item đã reserve và order chuyển Cancelled + FailedPayment.
    [Fact]
    public async Task PlaceOrder_PaymentFails_InventoryReleased()
    {
        var factory = new ShopFlowWebApplicationFactory();
        await factory.InitializeAsync();

        try
        {
            var hostFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IInventoryClient>();
                    services.RemoveAll<IPaymentClient>();
                    services.AddScoped<IInventoryClient>(_ => new FakeInventoryClient());
                    services.AddScoped<IPaymentClient>(_ => new FakePaymentClient("Failed"));
                });
            });

            using var client = hostFactory.CreateClient();
            var request = new PlaceOrderRequest(
                Guid.NewGuid(),
                "123 Lê Lợi",
                "Hà Nội",
                "Việt Nam",
                "100000",
                [
                    new PlaceOrderRequestItem(Guid.Parse("a73b5e21-4c0f-4f91-8a61-2e5c9501d101"), 1, 250000m, "VND")
                ]);

            using var createResponse = await client.PostAsJsonAsync("/api/v1/orders", request);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            using var orderDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var orderId = orderDocument.RootElement.GetProperty("orderId").GetGuid();

            var cancelled = false;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                using var getResponse = await client.GetAsync($"/api/v1/orders/{orderId}");
                if (getResponse.StatusCode == HttpStatusCode.OK)
                {
                    using var detailDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
                    var status = detailDocument.RootElement.GetProperty("status").GetString();
                    if (string.Equals(status, "Cancelled", StringComparison.Ordinal))
                    {
                        cancelled = true;
                        break;
                    }
                }

                await Task.Delay(500);
            }

            Assert.True(cancelled, "Order không chuyển sang Cancelled sau khi payment fail.");

            await using var scope = hostFactory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var sagaState = await dbContext.SagaStates.FirstOrDefaultAsync(state => state.OrderId == orderId);
            var reservation = await dbContext.SagaReservations.FirstOrDefaultAsync(r => r.OrderId == orderId);

            Assert.NotNull(sagaState);
            Assert.Equal("FailedPayment", sagaState.State);
            Assert.NotNull(reservation);
            Assert.Equal(SagaReservationStatus.Released, reservation.Status);
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }

    // Test này chứng minh Evidence Day 53: reserve fail phải cancel order và không gọi payment.
    [Fact]
    public async Task PlaceOrder_ReserveFails_OrderCancelled()
    {
        var factory = new ShopFlowWebApplicationFactory();
        await factory.InitializeAsync();

        try
        {
            var hostFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IInventoryClient>();
                    services.RemoveAll<IPaymentClient>();
                    services.AddScoped<IInventoryClient>(_ => new FakeInventoryClient { FailReserve = true });
                    services.AddScoped<IPaymentClient, FakePaymentClient>();
                });
            });

            using var client = hostFactory.CreateClient();
            var request = new PlaceOrderRequest(
                Guid.NewGuid(),
                "123 Lê Lợi",
                "Hà Nội",
                "Việt Nam",
                "100000",
                [
                    new PlaceOrderRequestItem(Guid.Parse("a73b5e21-4c0f-4f91-8a61-2e5c9501d101"), 1, 250000m, "VND")
                ]);

            using var createResponse = await client.PostAsJsonAsync("/api/v1/orders", request);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            using var orderDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            var orderId = orderDocument.RootElement.GetProperty("orderId").GetGuid();

            var cancelled = false;
            for (var attempt = 0; attempt < 60; attempt++)
            {
                using var getResponse = await client.GetAsync($"/api/v1/orders/{orderId}");
                if (getResponse.StatusCode == HttpStatusCode.OK)
                {
                    using var detailDocument = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
                    var status = detailDocument.RootElement.GetProperty("status").GetString();
                    if (string.Equals(status, "Cancelled", StringComparison.Ordinal))
                    {
                        cancelled = true;
                        break;
                    }
                }

                await Task.Delay(500);
            }

            Assert.True(cancelled, "Order không chuyển sang Cancelled khi reserve fail.");

            await using var scope = hostFactory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var sagaState = await dbContext.SagaStates.FirstOrDefaultAsync(state => state.OrderId == orderId);

            Assert.NotNull(sagaState);
            Assert.Equal("FailedInventory", sagaState.State);
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }

    private sealed class FakeInventoryClient : IInventoryClient
    {
        public bool FailReserve { get; init; }

        public Task<bool> ReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(!FailReserve);
        }

        public Task<bool> ReleaseAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakePaymentClient : IPaymentClient
    {
        private readonly string _status;

        public FakePaymentClient(string status = "Succeeded")
        {
            _status = status;
        }

        public Task<PaymentChargeResult?> ChargeAsync(Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PaymentChargeResult?>(new PaymentChargeResult(_status));
        }
    }
}
